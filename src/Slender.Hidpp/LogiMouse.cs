namespace Slender.Hidpp;

public sealed class LogiMouseOptions : LogiDeviceOptions
{
    /// <summary>Thumb wheel direction to apply on every connect (0x2150 invert bit); null keeps whatever the device has.</summary>
    public bool? ThumbWheelInvert { get; init; }
    /// <summary>SmartShift auto free-spin to apply on every connect (0x2111); null keeps whatever the device has.</summary>
    public SmartShiftSetting? SmartShift { get; init; }
}

/// <summary>SmartShift: Enabled = auto switch to free-spin when the wheel is flicked; Threshold 1-50 (Options+ sensitivity).</summary>
public readonly record struct SmartShiftSetting(bool Enabled, byte Threshold);

/// <summary>MX Master 4 over HID++ 2.0. Events fire on background threads; handlers must not call back synchronously.</summary>
public sealed class LogiMouse : LogiDeviceBase, ILogiMouse
{
    /// <summary>0x19B0 function that plays a waveform. fn4 confirmed on device 2026-10-15 (fn3 is accepted but silent).</summary>
    public const byte HapticPlayFunction = 4;

    /// <summary>MX Master 4 controls that can be diverted (0x1B04 flags 0x31, confirmed on device 2026-10-15).</summary>
    private static readonly ushort[] KnownDivertable =
        [ControlId.Middle, ControlId.Back, ControlId.Forward, ControlId.Gesture, ControlId.ModeShift, ControlId.SensePanel];

    private bool _thumbDesired;
    private bool _thumbApplied;
    private bool? _thumbInvertDesired;
    private SmartShiftSetting? _smartShiftDesired;

    public LogiMouse(IHidppTransportFactory factory, LogiMouseOptions? options = null)
        : base(factory, options ?? new LogiMouseOptions())
    {
        _thumbInvertDesired = (options ?? new LogiMouseOptions()).ThumbWheelInvert;
        _smartShiftDesired = (options ?? new LogiMouseOptions()).SmartShift;
    }

    public override string Name => "MX Master 4";

    public event EventHandler<ButtonEvent>? ButtonChanged;
    public event EventHandler<ThumbWheelEvent>? ThumbWheelMoved;

    protected override IReadOnlyCollection<ushort> KnownDivertableControls => KnownDivertable;

    protected override void DispatchControl(ButtonEvent e) => ButtonChanged?.Invoke(this, e);

    protected override void ResetAppliedState() => _thumbApplied = false;

    // ---- applying device state --------------------------------------------------------------

    protected override Task ApplyBeforeDivertsAsync(Session s, CancellationToken ct) => ClearNativeWheelTargetAsync(s, ct);

    protected override async Task ApplyAfterDivertsAsync(Session s, CancellationToken ct)
    {
        bool thumb;
        bool? invert;
        SmartShiftSetting? smartShift;
        lock (State) { thumb = _thumbDesired; invert = _thumbInvertDesired; smartShift = _smartShiftDesired; }
        if (await TrySetThumbAsync(s, thumb, invert, ct).ConfigureAwait(false) && thumb)
            lock (State) _thumbApplied = true;
        if (smartShift is { } ss) await TrySetSmartShiftAsync(s, ss, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 0x2111 read-modify-write: keeps the current wheel mode and force, sets the auto-disengage threshold
    /// (0xFF = SmartShift off). Layout per docs/protocol.md; read on device 2026-10-15: [mode 1, threshold 22, force 100].
    /// </summary>
    private static async Task<bool> TrySetSmartShiftAsync(Session s, SmartShiftSetting setting, CancellationToken ct)
    {
        try
        {
            var p = (await s.Device.CallAsync(FeatureId.SmartShiftEnhanced, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params;
            var threshold = setting.Enabled ? Math.Clamp(setting.Threshold, (byte)1, (byte)50) : (byte)0xFF;
            if (p[1] == threshold) return true;
            await s.Device.CallAsync(FeatureId.SmartShiftEnhanced, 2, new byte[] { p[0], threshold, p[2] }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>Sets SmartShift now (if connected) and on every reconnect.</summary>
    public async Task SetSmartShiftAsync(SmartShiftSetting setting, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _smartShiftDesired = setting;
            var s = CurrentSession;
            if (s == null || !IsConnected) return;
            await TrySetSmartShiftAsync(s, setting, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    /// <summary>0x2121 read-modify-write: clear bit0 (native HID target) as Mouser does. Survives process exit on device.</summary>
    private static async Task ClearNativeWheelTargetAsync(Session s, CancellationToken ct)
    {
        try
        {
            var mode = (await s.Device.CallAsync(FeatureId.HiResWheel, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[0];
            if ((mode & 0x01) != 0)
                await s.Device.CallAsync(FeatureId.HiResWheel, 2, new[] { (byte)(mode & ~0x01) }, ct).ConfigureAwait(false);
        }
        catch (HidppException) { }
    }

    private static async Task<bool> TrySetThumbAsync(Session s, bool diverted, bool? invertWanted, CancellationToken ct)
    {
        try
        {
            // Invert comes from config when set; otherwise keep the device's current bit (getConfig p[1] bit0).
            var current = await s.Device.CallAsync(FeatureId.ThumbWheel, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
            var invert = invertWanted is { } w ? (byte)(w ? 1 : 0) : (byte)(current.Params[1] & 0x01);
            await s.Device.CallAsync(FeatureId.ThumbWheel, 2, new byte[] { diverted ? (byte)1 : (byte)0, invert, 0 }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    public Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> controlIds, CancellationToken ct) =>
        SetDivertedCoreAsync(controlIds, ct);

    /// <summary>Sets the thumb wheel direction now (if connected) and on every reconnect.</summary>
    public async Task SetThumbWheelInvertAsync(bool invert, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool diverted;
            lock (State) { _thumbInvertDesired = invert; diverted = _thumbDesired; }
            var s = CurrentSession;
            if (s == null || !IsConnected) return;
            await TrySetThumbAsync(s, diverted, invert, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    public async Task SetThumbWheelDivertedAsync(bool diverted, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool? invert;
            lock (State) { _thumbDesired = diverted; invert = _thumbInvertDesired; }
            var s = CurrentSession;
            if (s == null || !IsConnected) return;
            if (await TrySetThumbAsync(s, diverted, invert, ct).ConfigureAwait(false))
                lock (State) _thumbApplied = diverted;
        }
        finally { Gate.Release(); }
    }

    // ---- haptics ------------------------------------------------------------------------------

    public async Task PlayHapticAsync(byte waveform, CancellationToken ct)
    {
        var s = CurrentSession;
        if (s == null || !IsConnected) return;
        try
        {
            await s.Device.SendAsync(FeatureId.Haptics, HapticPlayFunction, new[] { waveform }, ct).ConfigureAwait(false);
        }
        catch (HidppException) { }
        catch (ObjectDisposedException) { }
    }

    // ---- notifications ------------------------------------------------------------------------

    protected override void HandleOtherNotification(Session s, HidppMessage msg)
    {
        if (s.Device.TryGetCachedIndex(FeatureId.ThumbWheel, out var thumb) && msg.FeatureIndex == thumb && msg.Function == 0)
        {
            if (ParseThumbWheel(msg) is { } ev)
            {
                try { ThumbWheelMoved?.Invoke(this, ev); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ThumbWheelMoved handler threw: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// 0x2150 fn0 event. PROBE: layout is the Logitech-spec expectation (docs/protocol.md), not confirmed on device:
    /// p[0-1] int16 BE rotation, p[2-3] timestamp, p[4] rotation status, p[5] flags (bit0 touch, bit1 proximity, bit2 single tap).
    /// Keep all layout knowledge in this one method.
    /// </summary>
    public static ThumbWheelEvent? ParseThumbWheel(HidppMessage msg)
    {
        var p = msg.Params;
        int rotation = (short)((p[0] << 8) | p[1]);
        byte flags = p[5];
        return new ThumbWheelEvent(rotation, (flags & 0x01) != 0, (flags & 0x02) != 0, (flags & 0x04) != 0);
    }

    protected override async Task UndoOnDisposeAsync(Session s, CancellationToken ct)
    {
        bool thumb;
        bool? invert;
        lock (State) { thumb = _thumbApplied; invert = _thumbInvertDesired; }
        if (thumb) await TrySetThumbAsync(s, false, invert, ct).ConfigureAwait(false);
    }
}
