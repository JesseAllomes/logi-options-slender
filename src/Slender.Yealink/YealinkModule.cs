using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.Yealink;

/// <summary>
/// The Yealink WH64 headset base: hook, mute, flash, reject and Teams-button events, and (when enabled) the mute LED. While
/// stopped it holds nothing: no link, threads, handles or buffers. While started it has one link (one read thread) and nothing
/// else; no polling. If Yealink's own USB Connect app is running at Start or Wake it opens nothing, so the two never fight over
/// the device.
/// VendorReads (default on): the vendor channel is opened and the base is asked for its battery, signal and settings at Start,
/// on Wake and when Settings asks (<see cref="RefreshSettings"/>, at most every few seconds); between those the base pushes
/// battery and signal on its own. With it off the channel is never opened and nothing is sent. Only allowlisted requests exist: the
/// reads, and (<see cref="SetSetting"/>) a write of a catalogued, editable setting to a value the catalog lists.
/// Mute, hook and reject also arrive on the standard telephony collection, which is read (never written) beside the private one.
/// MuteSync: the headset's mute press toggles the Windows default microphone, and the mute LED (when LED writes are on) follows
/// the Windows mute, including changes made elsewhere. The microphone object and its notification exist only while a link is open.
/// BaseRingEnabled (default off): the consumer keys collection (COL03) is opened read-only, on the same read thread, and the base's ring
/// bursts raise <see cref="BaseRingChanged"/>. With it off that collection is never opened.
/// Events are raised on the read thread and must return quickly. LedWritesEnabled, MuteSync, VendorReadsEnabled and BaseRingEnabled are read at Start.
/// </summary>
public sealed class YealinkModule : IDeviceModule
{
    public const string YealinkConnectProcess = "YealinkUSBConnect";

    /// <summary>Settings may ask for a re-read no more often than this.</summary>
    public const int RefreshMinMs = 5000;

    private readonly IYealinkTransport _transport;
    private readonly Action<string> _log;
    private readonly Func<bool> _standDown;
    private readonly Func<IMicMute> _micFactory;
    private readonly Func<long> _tick;
    private readonly object _gate = new();
    private readonly object _ledLock = new();

    private bool _started;
    private bool _ledsOn;
    private bool _syncOn;
    private bool _vendorOn;
    private bool _baseRingOn;
    private readonly BaseRingDetector _ringDetector;
    private long _lastRefresh;
    private volatile YealinkVendorSession? _vendor;
    private volatile bool _offHook;
    private volatile string? _refusal;
    private volatile IMicMute? _mic;
    private volatile IYealinkLink? _link;
    private YealinkInputDecoder? _decoder;
    private YealinkLedBuilder? _led;
    private volatile YealinkEventSink? _sink;
    private bool _ledSent;

    /// <param name="standDown">True when another app owns the headset. Default: YealinkUSBConnect is running.</param>
    /// <param name="micFactory">Creates the Windows microphone mute when MuteSync needs it. Default: the Core Audio default capture device.</param>
    /// <param name="tick">Milliseconds clock for the refresh limit. Default: Environment.TickCount64.</param>
    public YealinkModule(IYealinkTransport transport, Action<string> log, Func<bool>? standDown = null, Func<IMicMute>? micFactory = null, Func<long>? tick = null)
    {
        _ringDetector = new BaseRingDetector(tick);
        _ringDetector.Changed += OnBaseRing;
        _tick = tick ?? (static () => Environment.TickCount64);
        _micFactory = micFactory ?? (() => new Windows.WindowsMicMute());
        _transport = transport;
        _log = log;
        _standDown = standDown ?? IsYealinkConnectRunning;
    }

    public string DeviceId => DeviceCatalog.YealinkWh64;

    /// <summary>When false (default) no output report is ever written to the headset.</summary>
    public bool LedWritesEnabled { get; set; }

    /// <summary>When false no vendor request is ever sent and the vendor channel is not opened. Default true.</summary>
    public bool VendorReadsEnabled { get; set; } = true;

    /// <summary>When true (default false) MutePressed toggles the Windows microphone and the mute LED follows it.</summary>
    public bool MuteSync { get; set; }

    /// <summary>When true (default false) the base's ringing is listened for and raises <see cref="BaseRingChanged"/>.</summary>
    public bool BaseRingEnabled { get; set; }

    public event Action<bool>? HookChanged;
    public event Action? MutePressed;
    public event Action? FlashPressed;
    public event Action? RejectPressed;
    public event Action? TeamsButtonPressed;

    /// <summary>The base started (true) or stopped (false) ringing. Read thread; return quickly.</summary>
    public event Action<bool>? BaseRingChanged;

    /// <summary>Ignore base ring bursts for this long (the preview ring makes the base send them too).</summary>
    public void SuppressBaseRing(TimeSpan span) => _ringDetector.SuppressFor(span);

    /// <summary>The base reported a new battery, signal or setting (or the link closed and they were cleared). May come from any thread; return quickly.</summary>
    public event Action? HeadsetChanged;

    /// <summary>True while a link is open (read thread running).</summary>
    public bool IsOpen => _link != null;

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _ledsOn = LedWritesEnabled;
            _syncOn = MuteSync;
            _vendorOn = VendorReadsEnabled;
            _baseRingOn = BaseRingEnabled;
            TryOpen();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
            Close();
            _offHook = false;
        }
    }

    /// <summary>The interfaces just arrived: open if nothing is open (or the link died), unless another app owns the headset.</summary>
    public void Wake()
    {
        lock (_gate)
        {
            if (!_started) return;
            if (_link is { IsFaulted: true }) Close();
            if (_link == null) TryOpen();
            else BeginVendor(VendorRead.Status, checkStandDown: true);
        }
    }

    /// <summary>
    /// Settings asked (its Headset page opened): read the base's status and settings again. At most once every <see cref="RefreshMinMs"/>,
    /// and nothing while a read is running, with vendor reads off, or while another app owns the headset.
    /// </summary>
    public void RefreshSettings()
    {
        if (_refusal != null) { _refusal = null; OnVendorChanged(); }
        long now = _tick();
        lock (_gate)
        {
            if (!_started || _vendor == null || now - _lastRefresh < RefreshMinMs) return;
            _lastRefresh = now;
            BeginVendor(VendorRead.All, checkStandDown: true);
        }
    }

    /// <summary>The base's own report for status.json and the tray menu; null while vendor reads are off or the base has not answered (a refused setting change still shows its problem).</summary>
    public HeadsetState? GetHeadset()
    {
        var vendor = _vendor;
        if (vendor == null) return _refusal is { } why ? new HeadsetState(null, null, null, null, null, null, WriteProblem: why) : null;
        var s = vendor.Status;
        var settings = vendor.Settings;
        var problem = _refusal ?? vendor.WriteProblem;
        if (s == default && settings.Count == 0 && problem == null) return null;
        return new HeadsetState(s.Registered, s.BatteryPercent, s.Charging, s.Docked, s.Signal, s.InRange, settings.Count > 0 ? settings : null, s.Model, problem);
    }

    /// <summary>
    /// Settings asked to change a base setting (catalog index and the value as <see cref="YealinkSettingDef.RawFor"/> takes it).
    /// Refused, with the reason in <see cref="HeadsetState.WriteProblem"/> and nothing sent, for a value the catalog does not allow,
    /// with vendor reads off, while another app owns the headset, without a vendor channel, or for a setting that restarts the
    /// base or switches its mode during a call. Otherwise written and read back by the session.
    /// </summary>
    public void SetSetting(int catalogIndex, long value)
    {
        string? problem = null;
        YealinkVendorSession? vendor = null;
        string? raw = null;
        YealinkSettingDef? def = catalogIndex >= 0 && catalogIndex < YealinkSettingsCatalog.All.Count ? YealinkSettingsCatalog.All[catalogIndex] : null;
        lock (_gate)
        {
            if (def == null || (raw = def.RawFor(value)) == null) problem = "That setting cannot be changed to that value.";
            else if (_started && !_vendorOn) problem = "Turn on Talk to the base (on the base page) to change these settings.";
            else if (_standDown()) problem = $"Close {YealinkConnectProcess} to change base settings.";
            else if ((vendor = _vendor) == null || _link == null) problem = "The headset base is not connected.";
            else if (def.Caution is YealinkCaution.Restarts or YealinkCaution.ModeSwitch && _offHook) problem = $"Finish the call before changing {def.Label}.";
        }

        if (problem != null)
        {
            _log($"Yealink: refused {def?.Key ?? "setting " + catalogIndex}={raw ?? value.ToString()}: {problem}");
            _refusal = problem;
            OnVendorChanged();
            return;
        }
        _refusal = null;
        try
        {
            if (!vendor!.Set(catalogIndex, raw!))
            {
                _refusal = "The headset base is not connected.";
                OnVendorChanged();
            }
        }
        catch (Exception ex) { _log($"Yealink: setting change failed: {ex.Message}"); }
    }

    private void BeginVendor(VendorRead what, bool checkStandDown)
    {
        var vendor = _vendor;
        if (vendor == null || !_vendorOn) return;
        try
        {
            if (checkStandDown && _standDown())
            {
                _log($"Yealink: {YealinkConnectProcess} is running; not reading the base");
                return;
            }
            vendor.Begin(what);
        }
        catch (Exception ex) { _log($"Yealink: vendor read failed: {ex.Message}"); }
    }

    /// <summary>
    /// Sets the flags and, if they differ from what the running link was opened with, reopens it so they take effect now
    /// (Stop and Start under the module's own lock, so serial with the registry's calls). Nothing happens while stopped.
    /// </summary>
    public void ApplySettings(bool ledWrites, bool muteSync, bool vendorReads = true, bool baseRing = false)
    {
        lock (_gate)
        {
            LedWritesEnabled = ledWrites;
            MuteSync = muteSync;
            VendorReadsEnabled = vendorReads;
            BaseRingEnabled = baseRing;
            if (!_started || (_ledsOn == ledWrites && _syncOn == muteSync && _vendorOn == vendorReads && _baseRingOn == baseRing)) return;
            Close();
            _ledsOn = ledWrites;
            _syncOn = muteSync;
            _vendorOn = vendorReads;
            _baseRingOn = baseRing;
            TryOpen();
        }
    }

    /// <summary>The Windows default microphone's mute for the tray menu; null when it cannot be read. Uses the open link's microphone, else a short-lived one.</summary>
    public bool? GetMicMuted()
    {
        try { return WithMic(m => m.GetMute()); }
        catch (Exception ex) { _log($"Yealink: microphone read failed: {ex.Message}"); return null; }
    }

    /// <summary>Toggles the Windows microphone mute (the tray's "Mute microphone"); the headset light follows when it is on. Returns the new state, null on failure.</summary>
    public bool? ToggleMicMute()
    {
        try
        {
            bool muted = WithMic(m => { bool next = !m.GetMute(); m.SetMute(next); return next; });
            SetMuteLed(muted);
            return muted;
        }
        catch (Exception ex) { _log($"Yealink: microphone toggle failed: {ex.Message}"); return null; }
    }

    private T WithMic<T>(Func<IMicMute, T> use)
    {
        if (_mic is { } open) return use(open);
        var temp = _micFactory();
        try { return use(temp); }
        finally { DisposeMic(temp); }
    }

    private void TryOpen()
    {
        try
        {
            if (_standDown())
            {
                _log($"Yealink: {YealinkConnectProcess} is running; not opening the headset");
                return;
            }

            var link = _transport.Open(_ledsOn, _vendorOn, OnReport, _log, _baseRingOn);
            if (link == null)
            {
                _log("Yealink: headset interfaces not found");
                return;
            }

            _decoder = new YealinkInputDecoder(link.Telephony, link.Standard, hookState: _offHook);
            _sink = Raise;
            _led = _ledsOn && link.Telephony is { } caps ? YealinkLedBuilder.TryCreate(caps) : null;
            if (_ledsOn && _led == null) _log("Yealink: no mute LED on the telephony report; LED writes off");
            _link = link;
            if (_vendorOn)
            {
                var vendor = new YealinkVendorSession(link.WriteVendor, _log);
                vendor.Changed += OnVendorChanged;
                _vendor = vendor;
            }
            link.Start();
            if (_syncOn) StartMuteSync();
            _lastRefresh = _tick();
            BeginVendor(VendorRead.All, checkStandDown: false);
        }
        catch (Exception ex)
        {
            _log($"Yealink: open failed: {ex.Message}");
            Close();
        }
    }

    private void StartMuteSync()
    {
        IMicMute? mic = null;
        try
        {
            mic = _micFactory();
            mic.Subscribe(SetMuteLed);
            _mic = mic;
            SetMuteLed(mic.GetMute());
        }
        catch (Exception ex)
        {
            _log($"Yealink: microphone mute sync unavailable: {ex.Message}");
            _mic = null;
            DisposeMic(mic);
        }
    }

    private void DisposeMic(IMicMute? mic)
    {
        try { mic?.Dispose(); }
        catch (Exception ex) { _log($"Yealink: microphone release failed: {ex.Message}"); }
    }

    private void Close()
    {
        // Stop the Windows notification first so nothing calls into a closing link; the mic is released after the read thread has joined.
        var mic = _mic;
        _mic = null;
        try { mic?.Unsubscribe(); }
        catch (Exception ex) { _log($"Yealink: microphone unsubscribe failed: {ex.Message}"); }
        var link = _link;
        _link = null;
        _sink = null;
        var vendor = _vendor;
        _vendor = null;
        _refusal = null;
        try { link?.Dispose(); }
        catch (Exception ex) { _log($"Yealink: close failed: {ex.Message}"); }
        if (vendor != null)
        {
            vendor.Changed -= OnVendorChanged;
            vendor.Dispose();
            OnVendorChanged(); // what it knew is gone with it
        }
        DisposeMic(mic);
        _decoder = null;
        lock (_ledLock) { _led = null; _ledSent = false; }
    }

    private void OnBaseRing(bool start)
    {
        try { BaseRingChanged?.Invoke(start); }
        catch (Exception ex) { _log($"Yealink: base ring handler failed: {ex.Message}"); }
    }

    private void OnVendorChanged()
    {
        try { HeadsetChanged?.Invoke(); }
        catch (Exception ex) { _log($"Yealink: headset handler failed: {ex.Message}"); }
    }

    private void OnReport(YealinkCollection collection, ReadOnlySpan<byte> report)
    {
        if (collection == YealinkCollection.Vendor)
        {
            _vendor?.OnReport(report);
            return;
        }
        if (collection == YealinkCollection.ConsumerKeys)
        {
            if (_baseRingOn) _ringDetector.Feed(report);
            return;
        }
        var decoder = _decoder;
        var sink = _sink;
        if (decoder == null || sink == null) return;
        switch (collection)
        {
            case YealinkCollection.Telephony: decoder.DecodeTelephony(report, sink); break;
            case YealinkCollection.Standard: decoder.DecodeStandard(report, sink); break;
            default: decoder.DecodeTeams(report, sink); break;
        }
    }

    private void Raise(YealinkEventKind kind, bool value)
    {
        try
        {
            if (kind == YealinkEventKind.MutePressed) ToggleWindowsMute();
            switch (kind)
            {
                case YealinkEventKind.HookChanged: _offHook = value; if (!value && _refusal != null) { _refusal = null; OnVendorChanged(); } HookChanged?.Invoke(value); break;
                case YealinkEventKind.MutePressed: MutePressed?.Invoke(); break;
                case YealinkEventKind.FlashPressed: FlashPressed?.Invoke(); break;
                case YealinkEventKind.RejectPressed: RejectPressed?.Invoke(); break;
                case YealinkEventKind.TeamsButtonPressed: TeamsButtonPressed?.Invoke(); break;
            }
        }
        catch (Exception ex) { _log($"Yealink: {kind} handler failed: {ex.Message}"); }
    }

    private void ToggleWindowsMute()
    {
        var mic = _mic;
        if (mic == null) return;
        try
        {
            bool muted = !mic.GetMute();
            mic.SetMute(muted);
            SetMuteLed(muted);
        }
        catch (Exception ex) { _log($"Yealink: microphone toggle failed: {ex.Message}"); }
    }

    /// <summary>Sets the headset's mute LED. No-op unless LED writes are enabled and the link is open; repeated values are not rewritten.</summary>
    public void SetMuteLed(bool on)
    {
        lock (_ledLock)
        {
            var led = _led;
            var link = _link;
            if (led == null || link == null) return;
            if (_ledSent && led.Get(YealinkLed.Mute) == on) return;
            led.Set(YealinkLed.Mute, on);
            try { _ledSent = link.Write(led.Report); }
            catch (Exception ex) { _log($"Yealink: LED write failed: {ex.Message}"); }
        }
    }

    private static bool IsYealinkConnectRunning()
    {
        var found = System.Diagnostics.Process.GetProcessesByName(YealinkConnectProcess);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }
}
