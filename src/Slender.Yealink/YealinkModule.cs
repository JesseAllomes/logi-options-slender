using Slender.Engine.Devices;

namespace Slender.Yealink;

/// <summary>
/// The Yealink WH64 headset base: hook, mute, flash, reject and Teams-button events, and (when enabled) the mute LED. While
/// stopped it holds nothing: no link, threads, handles or buffers. While started it has one link (one read thread) and nothing
/// else; no polling. If Yealink's own USB Connect app is running at Start or Wake it opens nothing, so the two never fight over
/// the device.
/// MuteSync: the headset's mute press toggles the Windows default microphone, and the mute LED (when LED writes are on) follows
/// the Windows mute, including changes made elsewhere. The microphone object and its notification exist only while a link is open.
/// Events are raised on the read thread and must return quickly. LedWritesEnabled and MuteSync are read at Start.
/// </summary>
public sealed class YealinkModule : IDeviceModule
{
    public const string YealinkConnectProcess = "YealinkUSBConnect";

    private readonly IYealinkTransport _transport;
    private readonly Action<string> _log;
    private readonly Func<bool> _standDown;
    private readonly Func<IMicMute> _micFactory;
    private readonly object _gate = new();
    private readonly object _ledLock = new();

    private bool _started;
    private bool _ledsOn;
    private bool _syncOn;
    private volatile IMicMute? _mic;
    private volatile IYealinkLink? _link;
    private YealinkInputDecoder? _decoder;
    private YealinkLedBuilder? _led;
    private volatile YealinkEventSink? _sink;
    private bool _ledSent;

    /// <param name="standDown">True when another app owns the headset. Default: YealinkUSBConnect is running.</param>
    /// <param name="micFactory">Creates the Windows microphone mute when MuteSync needs it. Default: the Core Audio default capture device.</param>
    public YealinkModule(IYealinkTransport transport, Action<string> log, Func<bool>? standDown = null, Func<IMicMute>? micFactory = null)
    {
        _micFactory = micFactory ?? (() => new Windows.WindowsMicMute());
        _transport = transport;
        _log = log;
        _standDown = standDown ?? IsYealinkConnectRunning;
    }

    public string DeviceId => DeviceCatalog.YealinkWh64;

    /// <summary>When false (default) no output report is ever written to the headset.</summary>
    public bool LedWritesEnabled { get; set; }

    /// <summary>When true (default false) MutePressed toggles the Windows microphone and the mute LED follows it.</summary>
    public bool MuteSync { get; set; }

    public event Action<bool>? HookChanged;
    public event Action? MutePressed;
    public event Action? FlashPressed;
    public event Action? RejectPressed;
    public event Action? TeamsButtonPressed;

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
            TryOpen();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
            Close();
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
        }
    }

    /// <summary>
    /// Sets both flags and, if they differ from what the running link was opened with, reopens it so they take effect now
    /// (Stop and Start under the module's own lock, so serial with the registry's calls). Nothing happens while stopped.
    /// </summary>
    public void ApplySettings(bool ledWrites, bool muteSync)
    {
        lock (_gate)
        {
            LedWritesEnabled = ledWrites;
            MuteSync = muteSync;
            if (!_started || (_ledsOn == ledWrites && _syncOn == muteSync)) return;
            Close();
            _ledsOn = ledWrites;
            _syncOn = muteSync;
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

            var link = _transport.Open(_ledsOn, OnReport, _log);
            if (link == null)
            {
                _log("Yealink: headset interfaces not found");
                return;
            }

            _decoder = new YealinkInputDecoder(link.Telephony);
            _sink = Raise;
            _led = _ledsOn && link.Telephony is { } caps ? YealinkLedBuilder.TryCreate(caps) : null;
            if (_ledsOn && _led == null) _log("Yealink: no mute LED on the telephony report; LED writes off");
            _link = link;
            link.Start();
            if (_syncOn) StartMuteSync();
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
        try { link?.Dispose(); }
        catch (Exception ex) { _log($"Yealink: close failed: {ex.Message}"); }
        DisposeMic(mic);
        _decoder = null;
        lock (_ledLock) { _led = null; _ledSent = false; }
    }

    private void OnReport(YealinkCollection collection, ReadOnlySpan<byte> report)
    {
        var decoder = _decoder;
        var sink = _sink;
        if (decoder == null || sink == null) return;
        if (collection == YealinkCollection.Telephony) decoder.DecodeTelephony(report, sink);
        else decoder.DecodeTeams(report, sink);
    }

    private void Raise(YealinkEventKind kind, bool value)
    {
        try
        {
            if (kind == YealinkEventKind.MutePressed) ToggleWindowsMute();
            switch (kind)
            {
                case YealinkEventKind.HookChanged: HookChanged?.Invoke(value); break;
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
