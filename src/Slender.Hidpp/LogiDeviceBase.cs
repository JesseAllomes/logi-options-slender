using System.Diagnostics;

namespace Slender.Hidpp;

public class LogiDeviceOptions
{
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(2000);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelayBase { get; init; } = TimeSpan.FromMilliseconds(100);
    public int ReadPollMs { get; init; } = 250;
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan BackoffInitial { get; init; } = TimeSpan.FromMilliseconds(1500);
    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>How often the monitor pings when no report has arrived in between.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int AsleepAfterTimeouts { get; init; } = 3;
    public TimeSpan AsleepReconnectAfter { get; init; } = TimeSpan.FromSeconds(300);
    public TimeSpan DisposeUndivertBudget { get; init; } = TimeSpan.FromMilliseconds(1500);
    /// <summary>Device indices tried in order by the ping probe (docs/protocol.md: PROBE).</summary>
    public byte[] DeviceIndexCandidates { get; init; } = [0xFF, 0x01];
}

/// <summary>
/// Shared HID++ 2.0 device lifecycle for the mouse and the keyboard: connect/backoff loop, sleep detection, re-apply of
/// volatile state, 0x1B04 divert bookkeeping (temporary flags only), held-control tracking, battery and shutdown.
/// Events fire on background threads; handlers must not call back synchronously.
/// </summary>
public abstract class LogiDeviceBase : IDisposable
{
    private const byte DivertFlags = 0x03;   // temporary divert only; never the persistent bit (0x04)
    private const byte UndivertFlags = 0x02;
    // Raw XY (gesture button only, see HidppAllowlist): bit4 raw XY + bit5 its valid bit. 0x33 = divert + raw XY, 0x22 = undivert + raw XY off.
    private const byte DivertRawXyFlags = 0x33;
    private const byte UndivertRawXyFlags = 0x22;

    /// <summary>One open channel plus its device index.</summary>
    protected sealed class Session(HidppChannel channel, HidppDevice device)
    {
        public HidppChannel Channel { get; } = channel;
        public HidppDevice Device { get; } = device;
        public int Ended;
    }

    private readonly IHidppTransportFactory _factory;
    private readonly LogiDeviceOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private HashSet<ushort> _desiredDiverts = [];
    private readonly HashSet<ushort> _appliedDiverts = [];
    private HashSet<ushort> _desiredRawXy = [];
    private readonly HashSet<ushort> _appliedRawXy = []; // subset of _appliedDiverts whose divert also carries raw XY
    private HashSet<ushort> _held = [];
    private volatile Session? _session;
    private volatile bool _connected;
    private int _activity;
    private int _started;
    private int _disposed;
    private Task? _runTask;

    protected LogiDeviceBase(IHidppTransportFactory factory, LogiDeviceOptions options)
    {
        _factory = factory;
        _options = options;
    }

    public abstract string Name { get; }
    public bool IsConnected => _connected;

    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>Serialises device writes. Held while applying state.</summary>
    protected SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Guards the bookkeeping sets and any subclass state that is read under it.</summary>
    protected object State { get; } = new();

    protected LogiDeviceOptions Options => _options;
    protected Session? CurrentSession => _session;

    /// <summary>
    /// The running firmware: the active firmware entity of 0x0003 as "NAME major.minor.build" (Solaar hidpp20.py get_firmware
    /// layout: p[0] kind, p[1-3] name, p[4-5] version BCD, p[6-7] build, p[8] bit0 active). Null when disconnected or unreadable.
    /// </summary>
    public async Task<string?> GetFirmwareVersionAsync(CancellationToken ct)
    {
        var s = CurrentSession;
        if (s == null) return null;
        try
        {
            int count = (await s.Device.CallAsync(FeatureId.DeviceFwVersion, 0, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[0];
            string? first = null;
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                var r = (await s.Device.CallAsync(FeatureId.DeviceFwVersion, 1, new byte[] { (byte)i }, ct).ConfigureAwait(false)).Params;
                if ((r[0] & 0x0F) != 0) continue; // 0 = main firmware (bootloader and hardware entities are skipped)
                string name = System.Text.Encoding.ASCII.GetString(r.Slice(1, 3)).Trim('\0', ' ');
                string version = $"{name} {r[4]:X2}.{r[5]:X2}.{r[6]:X2}{r[7]:X2}".Trim();
                if ((r[8] & 1) != 0) return version;
                first ??= version;
            }
            return first;
        }
        catch (HidppException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>Controls that may be left diverted by another driver; reset to native on every connect unless wanted.</summary>
    protected abstract IReadOnlyCollection<ushort> KnownDivertableControls { get; }

    /// <summary>Raises the subclass's button/key event. Exceptions are caught by the caller.</summary>
    protected abstract void DispatchControl(ButtonEvent e);

    /// <summary>Runs inside the apply gate before diverts are written.</summary>
    protected virtual Task ApplyBeforeDivertsAsync(Session s, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Runs inside the apply gate after diverts are written.</summary>
    protected virtual Task ApplyAfterDivertsAsync(Session s, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Called under <see cref="State"/> when device-side state is lost (new connect, end of session).</summary>
    protected virtual void ResetAppliedState() { }

    /// <summary>Notifications other than 0x1B04 diverted-control events.</summary>
    protected virtual void HandleOtherNotification(Session s, HidppMessage msg) { }

    /// <summary>Best-effort extra undo on dispose, within the budget token.</summary>
    protected virtual Task UndoOnDisposeAsync(Session s, CancellationToken ct) => Task.CompletedTask;

    public Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            _runTask = Task.Run(() => RunAsync(linked.Token));
        }
        return Task.CompletedTask;
    }

    // ---- connect / monitor loop -------------------------------------------------------------

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = _options.BackoffInitial;
        while (!ct.IsCancellationRequested)
        {
            Session? session = null;
            bool established = false;
            try
            {
                session = await ConnectAsync(ct).ConfigureAwait(false);
                if (session != null)
                {
                    established = true;
                    await MonitorAsync(session, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Debug.WriteLine($"{Name} loop: {ex.Message}"); }
            finally
            {
                if (session != null) EndSession(session);
            }

            var wait = established ? _options.BackoffInitial : backoff;
            backoff = established ? _options.BackoffInitial : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.BackoffMax.Ticks));
            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<Session?> ConnectAsync(CancellationToken ct)
    {
        var transport = _factory.TryOpen();
        if (transport == null) return null;

        var channel = new HidppChannel(transport, new HidppChannelOptions
        {
            RequestTimeout = _options.RequestTimeout,
            MaxRetries = _options.MaxRetries,
            RetryDelayBase = _options.RetryDelayBase,
            ReadPollMs = _options.ReadPollMs,
        });
        channel.ReportReceived += (_, _) => Volatile.Write(ref _activity, 1);
        channel.Start();

        Session? session = null;
        try
        {
            HidppDevice? device = null;
            foreach (var idx in _options.DeviceIndexCandidates)
            {
                var candidate = new HidppDevice(channel, idx);
                if (await candidate.PingAsync(_options.ProbeTimeout, ct).ConfigureAwait(false) == PingResult.Answered)
                {
                    device = candidate;
                    break;
                }
            }
            if (device == null) return null;

            session = new Session(channel, device);
            channel.Notification += OnNotification;
            _session = session;
            await ApplyAllAsync(session, ct).ConfigureAwait(false);
            _connected = true;
            RaiseConnection(true);
            return session;
        }
        catch (Exception ex) when (ex is HidppException or ObjectDisposedException)
        {
            Debug.WriteLine($"{Name} connect: {ex.Message}");
            if (session != null) EndSession(session); else channel.Dispose();
            return null;
        }
        catch
        {
            if (session != null) EndSession(session); else channel.Dispose();
            throw;
        }
    }

    private async Task MonitorAsync(Session s, CancellationToken ct)
    {
        int timeouts = 0;
        DateTime? asleepSince = null;
        while (true)
        {
            await Task.WhenAny(Task.Delay(_options.HeartbeatInterval, ct), s.Channel.Faulted).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (s.Channel.Faulted.IsCompleted) return;

            bool alive = Interlocked.Exchange(ref _activity, 0) == 1; // any report = awake
            if (!alive)
            {
                var ping = await s.Device.PingAsync(_options.ProbeTimeout, ct).ConfigureAwait(false);
                alive = ping != PingResult.Timeout;
            }

            if (alive)
            {
                timeouts = 0;
                if (asleepSince != null)
                {
                    asleepSince = null;
                    await ApplyAllAsync(s, ct).ConfigureAwait(false); // state may be lost across sleep
                    _connected = true;
                    RaiseConnection(true);
                }
            }
            else
            {
                timeouts++;
                if (asleepSince == null && timeouts >= _options.AsleepAfterTimeouts)
                {
                    asleepSince = DateTime.UtcNow;
                    _connected = false;
                    RaiseConnection(false);
                }
            }

            if (asleepSince != null && DateTime.UtcNow - asleepSince >= _options.AsleepReconnectAfter) return;
        }
    }

    private void EndSession(Session s)
    {
        if (Interlocked.Exchange(ref s.Ended, 1) != 0) return;
        bool wasConnected = _connected;
        _connected = false;
        if (ReferenceEquals(_session, s)) _session = null;
        s.Channel.Notification -= OnNotification;
        s.Channel.Dispose();

        lock (State)
        {
            _appliedDiverts.Clear();
            _appliedRawXy.Clear();
            ResetAppliedState();
        }
        ReleaseHeldControls();
        if (wasConnected) RaiseConnection(false);
    }

    private void RaiseConnection(bool connected)
    {
        try { ConnectionChanged?.Invoke(this, connected); }
        catch (Exception ex) { Debug.WriteLine($"ConnectionChanged handler threw: {ex.Message}"); }
    }

    // ---- applying device state --------------------------------------------------------------

    /// <summary>Re-applies everything that is volatile on the device. Best effort per step.</summary>
    private async Task ApplyAllAsync(Session s, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyBeforeDivertsAsync(s, ct).ConfigureAwait(false);

            ushort[] desired;
            HashSet<ushort> rawWanted;
            lock (State)
            {
                _appliedDiverts.Clear();
                _appliedRawXy.Clear();
                ResetAppliedState();
                desired = [.. _desiredDiverts];
                rawWanted = [.. _desiredRawXy];
            }
            foreach (var cid in desired)
                await ApplyDivertAsync(s, cid, rawWanted.Contains(cid), ct).ConfigureAwait(false);
            // Another driver (Options+) may have left controls diverted; a diverted control we do not handle is
            // dead, so put everything we do not want back to native.
            foreach (var cid in KnownDivertableControls)
                if (Array.IndexOf(desired, cid) < 0)
                    await TrySetDivertAsync(s, cid, false, ct).ConfigureAwait(false);

            await ApplyAfterDivertsAsync(s, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    protected static async Task<bool> TrySetDivertAsync(Session s, ushort cid, bool divert, CancellationToken ct, bool rawXy = false)
    {
        // Undiverting a raw-XY-capable control also clears raw XY (valid bit set, value 0), so a raw divert can never be left behind.
        var flags = divert ? (rawXy ? DivertRawXyFlags : DivertFlags)
            : HidppAllowlist.RawXyControls.Contains(cid) ? UndivertRawXyFlags : UndivertFlags;
        try
        {
            await s.Device.CallAsync(FeatureId.ReprogControlsV4, 3,
                new byte[] { (byte)(cid >> 8), (byte)cid, flags, 0, 0 }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>
    /// Diverts one control, with raw XY when wanted and allowed. A failed raw-XY divert falls back to the plain divert so the
    /// button still works (the engine then uses the mouse-hook gesture path). Records the applied state.
    /// </summary>
    private async Task ApplyDivertAsync(Session s, ushort cid, bool rawXy, CancellationToken ct)
    {
        rawXy &= HidppAllowlist.RawXyControls.Contains(cid);
        if (rawXy && await TrySetDivertAsync(s, cid, true, ct, rawXy: true).ConfigureAwait(false))
        {
            lock (State) { _appliedDiverts.Add(cid); _appliedRawXy.Add(cid); }
        }
        else if (await TrySetDivertAsync(s, cid, true, ct).ConfigureAwait(false))
        {
            lock (State) { _appliedDiverts.Add(cid); _appliedRawXy.Remove(cid); }
        }
    }

    /// <summary>True while the control is diverted WITH raw XY on the current connection (its movement arrives as raw XY events).</summary>
    public bool IsRawXyActive(ushort controlId)
    {
        lock (State) return _connected && _appliedRawXy.Contains(controlId);
    }

    /// <summary>Records the desired set and applies the difference when connected (otherwise applied on (re)connect).</summary>
    protected async Task SetDivertedCoreAsync(IReadOnlyCollection<ushort> controlIds, CancellationToken ct, IReadOnlyCollection<ushort>? rawXyControlIds = null)
    {
        var wanted = new HashSet<ushort>(controlIds);
        var rawWanted = new HashSet<ushort>((rawXyControlIds ?? []).Where(c => wanted.Contains(c) && HidppAllowlist.RawXyControls.Contains(c)));
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ushort[] toUndivert, toDivert;
            lock (State)
            {
                _desiredDiverts = wanted;
                _desiredRawXy = rawWanted;
                toUndivert = [.. _appliedDiverts.Where(c => !wanted.Contains(c))];
                // Also re-divert a control whose raw-XY state must change (plain <-> raw XY).
                toDivert = [.. wanted.Where(c => !_appliedDiverts.Contains(c) || _appliedRawXy.Contains(c) != rawWanted.Contains(c))];
            }
            var s = _session;
            if (s == null || !_connected) return;
            foreach (var cid in toUndivert)
            {
                if (await TrySetDivertAsync(s, cid, false, ct).ConfigureAwait(false))
                    lock (State) { _appliedDiverts.Remove(cid); _appliedRawXy.Remove(cid); }
            }
            foreach (var cid in toDivert)
                await ApplyDivertAsync(s, cid, rawWanted.Contains(cid), ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    // ---- battery ------------------------------------------------------------------------------

    public async Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct)
    {
        var s = _session;
        if (s == null || !_connected) return null;
        try
        {
            if (await s.Device.GetFeatureIndexAsync(FeatureId.UnifiedBattery, ct).ConfigureAwait(false) != null)
                return ParseBattery((await s.Device.CallAsync(FeatureId.UnifiedBattery, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params);
            if (await s.Device.GetFeatureIndexAsync(FeatureId.Battery, ct).ConfigureAwait(false) != null)
                return ParseBattery((await s.Device.CallAsync(FeatureId.Battery, 0, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params);
        }
        catch (HidppException) { }
        catch (ObjectDisposedException) { }
        return null;
    }

    /// <summary>0x1004 fn1 / 0x1000 fn0: p[0] = level, p[2] = charging status (1..4 = charging).</summary>
    public static BatteryStatus ParseBattery(ReadOnlySpan<byte> p) => new(Math.Min((int)p[0], 100), p[2] is >= 1 and <= 4);

    // ---- notifications ------------------------------------------------------------------------

    private void OnNotification(object? sender, HidppMessage msg)
    {
        var s = _session;
        if (s == null || msg.SwId == s.Channel.SwId) return; // our own late response, not an event

        if (s.Device.TryGetCachedIndex(FeatureId.ReprogControlsV4, out var reprog) && msg.FeatureIndex == reprog && msg.Function == 0)
            HandleControls(ParseDivertedControls(msg));
        else if (reprog != 0 && msg.FeatureIndex == reprog && msg.Function == 1)
            RaiseRawXy(ParseRawXy(msg));
        else
            HandleOtherNotification(s, msg);
    }

    /// <summary>0x1B04 fn0 diverted-button event: u16 BE CID pairs, terminated by 0x0000 (all zero = all released).</summary>
    public static HashSet<ushort> ParseDivertedControls(HidppMessage msg)
    {
        var set = new HashSet<ushort>();
        var p = msg.Params;
        for (int i = 0; i + 1 < p.Length; i += 2)
        {
            ushort cid = (ushort)((p[i] << 8) | p[i + 1]);
            if (cid == 0) break;
            set.Add(cid);
        }
        return set;
    }

    /// <summary>0x1B04 event 1 (divertedRawMouseXY): p[0-1] dx int16 BE, p[2-3] dy int16 BE; positive dy = down. Layout per docs/protocol.md.</summary>
    public static RawXyEvent ParseRawXy(HidppMessage msg)
    {
        var p = msg.Params;
        return new RawXyEvent((short)((p[0] << 8) | p[1]), (short)((p[2] << 8) | p[3]));
    }

    private void RaiseRawXy(RawXyEvent e)
    {
        try { DispatchRawXy(e); }
        catch (Exception ex) { Debug.WriteLine($"{Name} raw XY handler threw: {ex.Message}"); }
    }

    /// <summary>Raw XY deltas while a raw-XY-diverted control is held. Default: ignored.</summary>
    protected virtual void DispatchRawXy(RawXyEvent e) { }

    private void HandleControls(HashSet<ushort> now)
    {
        List<ButtonEvent> events = [];
        lock (State)
        {
            foreach (var cid in _held) if (!now.Contains(cid)) events.Add(new ButtonEvent(cid, false));
            foreach (var cid in now) if (!_held.Contains(cid)) events.Add(new ButtonEvent(cid, true));
            _held = now;
        }
        foreach (var e in events) Raise(e);
    }

    private void ReleaseHeldControls()
    {
        ushort[] held;
        lock (State) { held = [.. _held]; _held = []; }
        foreach (var cid in held) Raise(new ButtonEvent(cid, false));
    }

    private void Raise(ButtonEvent e)
    {
        try { DispatchControl(e); }
        catch (Exception ex) { Debug.WriteLine($"{Name} control handler threw: {ex.Message}"); }
    }

    // ---- shutdown -----------------------------------------------------------------------------

    /// <summary>Undiverts everything best-effort (bounded), then stops. Do not call from an event handler.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var s = _session;
        if (s != null && _connected)
        {
            try
            {
                Task.Run(async () =>
                {
                    using var budget = new CancellationTokenSource(_options.DisposeUndivertBudget);
                    ushort[] applied;
                    lock (State) applied = [.. _appliedDiverts];
                    foreach (var cid in applied) await TrySetDivertAsync(s, cid, false, budget.Token).ConfigureAwait(false);
                    await UndoOnDisposeAsync(s, budget.Token).ConfigureAwait(false);
                }).Wait(_options.DisposeUndivertBudget + TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex) { Debug.WriteLine($"{Name} dispose undivert: {ex.Message}"); }
        }

        _cts.Cancel();
        if (s != null) EndSession(s);
        try { _runTask?.Wait(TimeSpan.FromSeconds(1)); } catch (Exception) { }
    }
}
