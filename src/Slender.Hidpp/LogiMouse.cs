namespace Slender.Hidpp;

public sealed class LogiMouseOptions : LogiDeviceOptions
{
    /// <summary>Thumb wheel direction to apply on every connect (0x2150 invert bit); null keeps whatever the device has.</summary>
    public bool? ThumbWheelInvert { get; init; }
    /// <summary>SmartShift auto free-spin to apply on every connect (0x2111); null keeps whatever the device has.</summary>
    public SmartShiftSetting? SmartShift { get; init; }
    /// <summary>Pointer speed to apply on every connect (0x2201); null keeps the device value. DPI does not survive a power cycle.</summary>
    public int? Dpi { get; init; }
    /// <summary>High-resolution wheel (0x2121 bit1) to apply on every connect; bit0 (target) is always kept cleared. null keeps the device's value.</summary>
    public bool? SmoothScroll { get; init; }
    /// <summary>SmartShift tunable torque 0-100 ("wheel resistance", 0x2111) to apply on every connect; null keeps the device's value.</summary>
    public int? WheelResistance { get; init; }
    /// <summary>Sense Panel force (0x19C0 fn3, device units) to apply on every connect, clamped to the safe band; null keeps the device's value.</summary>
    public int? SensePanelForce { get; init; }
    /// <summary>First force reading Slender saw (stored by the tray); the safe band is derived from it. null = take it from the next read.</summary>
    public int? SensePanelBaseline { get; init; }
    /// <summary>Haptic strength 0-100 (0x19B0 fn2) to apply on every connect; null keeps the device's value.</summary>
    public int? HapticStrength { get; init; }
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
    private bool? _smoothDesired;
    private int? _dpiDesired;
    private int? _torqueDesired;
    private int? _forceDesired;
    private int? _forceBaseline;
    private int? _hapticDesired;

    public LogiMouse(IHidppTransportFactory factory, LogiMouseOptions? options = null)
        : base(factory, options ?? new LogiMouseOptions())
    {
        _thumbInvertDesired = (options ?? new LogiMouseOptions()).ThumbWheelInvert;
        _smartShiftDesired = (options ?? new LogiMouseOptions()).SmartShift;
        _smoothDesired = (options ?? new LogiMouseOptions()).SmoothScroll;
        _dpiDesired = (options ?? new LogiMouseOptions()).Dpi;
        _torqueDesired = (options ?? new LogiMouseOptions()).WheelResistance;
        _forceDesired = (options ?? new LogiMouseOptions()).SensePanelForce;
        _forceBaseline = (options ?? new LogiMouseOptions()).SensePanelBaseline;
        _hapticDesired = (options ?? new LogiMouseOptions()).HapticStrength;
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
        bool? smooth;
        int? dpi, torque, force, haptic;
        lock (State)
        {
            thumb = _thumbDesired; invert = _thumbInvertDesired; smartShift = _smartShiftDesired; smooth = _smoothDesired; dpi = _dpiDesired;
            torque = _torqueDesired; force = _forceDesired; haptic = _hapticDesired;
        }
        if (await TrySetThumbAsync(s, thumb, invert, ct).ConfigureAwait(false) && thumb)
            lock (State) _thumbApplied = true;
        if (smartShift is { } ss) await TrySetSmartShiftAsync(s, ss, ct).ConfigureAwait(false);
        if (smooth is { } sm) await TrySetSmoothScrollAsync(s, sm, ct).ConfigureAwait(false);
        if (dpi is { } d) await TrySetDpiAsync(s, d, ct).ConfigureAwait(false);
        if (torque is { } t) await TrySetWheelResistanceAsync(s, t, ct).ConfigureAwait(false);
        await EnsureForceBaselineAsync(s, ct).ConfigureAwait(false);
        if (force is { } f) await TrySetSensePanelForceAsync(s, f, ct).ConfigureAwait(false);
        if (haptic is { } h) await TrySetHapticStrengthAsync(s, h, ct).ConfigureAwait(false);
    }

    // ---- wheel resistance (0x2111 tunable torque) ---------------------------------------------

    /// <summary>0x2111 read-modify-write: keeps the wheel mode and auto-disengage, sets the torque byte (0-100). No write when equal.</summary>
    private static async Task<bool> TrySetWheelResistanceAsync(Session s, int resistance, CancellationToken ct)
    {
        try
        {
            var value = (byte)Math.Clamp(resistance, 0, 100);
            var p = (await s.Device.CallAsync(FeatureId.SmartShiftEnhanced, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params;
            if (p[2] == value) return true;
            await s.Device.CallAsync(FeatureId.SmartShiftEnhanced, 2, new byte[] { p[0], p[1], value }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>Current wheel resistance 0-100 (0x2111 fn1 torque byte); null when disconnected or the read fails.</summary>
    public async Task<int?> GetWheelResistanceAsync(CancellationToken ct)
    {
        var s = CurrentSession;
        if (s == null || !IsConnected) return null;
        try
        {
            return (await s.Device.CallAsync(FeatureId.SmartShiftEnhanced, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[2];
        }
        catch (HidppException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>Sets wheel resistance now (if connected) and on every reconnect; null leaves the device value alone.</summary>
    public async Task SetWheelResistanceAsync(int? resistance, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _torqueDesired = resistance;
            var s = CurrentSession;
            if (s == null || !IsConnected || resistance is not { } r) return;
            await TrySetWheelResistanceAsync(s, r, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    // ---- Sense Panel force (0x19C0) -----------------------------------------------------------

    /// <summary>
    /// The protocol exposes no min/max for 0x19C0 (fn2 returns one u16; fn0/fn1 layouts are unknown and not allowlisted), so
    /// the safe band is 50%-200% of the first reading Slender saw (3887 on the user's mouse: 1943-7774). Wide enough to feel
    /// clearly lighter and firmer, narrow enough that a stray slider value cannot make the panel dead or hair-trigger.
    /// </summary>
    public static (int Min, int Max) SensePanelBand(int baseline) =>
        (Math.Max(1, baseline / 2), Math.Min(0xFFFF, baseline * 2));

    /// <summary>The reading the safe band is built on (from options, else the first successful read); null until known.</summary>
    public int? SensePanelBaseline { get { lock (State) return _forceBaseline; } }

    private async Task EnsureForceBaselineAsync(Session s, CancellationToken ct)
    {
        lock (State) { if (_forceBaseline != null) return; }
        try
        {
            var v = await ReadForceAsync(s, ct).ConfigureAwait(false);
            if (v > 0) lock (State) _forceBaseline ??= v;
        }
        catch (HidppException) { }
    }

    private static async Task<int> ReadForceAsync(Session s, CancellationToken ct)
    {
        var p = (await s.Device.CallAsync(FeatureId.ForceSensing, 2, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params;
        return (p[0] << 8) | p[1];
    }

    private async Task<bool> TrySetSensePanelForceAsync(Session s, int force, CancellationToken ct)
    {
        try
        {
            int? baseline;
            lock (State) baseline = _forceBaseline;
            if (baseline is not { } b) return false; // no reading to anchor a safe band on: touch nothing
            var (min, max) = SensePanelBand(b);
            var value = Math.Clamp(force, min, max);
            if (await ReadForceAsync(s, ct).ConfigureAwait(false) == value) return true;
            await s.Device.CallAsync(FeatureId.ForceSensing, 3, new byte[] { 0, (byte)(value >> 8), (byte)value }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>Current Sense Panel force reading (0x19C0 fn2); null when disconnected or the read fails.</summary>
    public async Task<int?> GetSensePanelForceAsync(CancellationToken ct)
    {
        var s = CurrentSession;
        if (s == null || !IsConnected) return null;
        try
        {
            var v = await ReadForceAsync(s, ct).ConfigureAwait(false);
            return v > 0 ? v : null;
        }
        catch (HidppException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>Sets the Sense Panel force now (if connected) and on every reconnect, clamped to the safe band; null leaves the device alone.</summary>
    public async Task SetSensePanelForceAsync(int? force, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _forceDesired = force;
            var s = CurrentSession;
            if (s == null || !IsConnected) return;
            await EnsureForceBaselineAsync(s, ct).ConfigureAwait(false);
            if (force is { } f) await TrySetSensePanelForceAsync(s, f, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    // ---- haptic strength (0x19B0) -------------------------------------------------------------

    /// <summary>0x19B0 fn1 read, fn2 write [enabled, level 0-100] only when the level differs. The device's enabled flag is set on.</summary>
    private static async Task<bool> TrySetHapticStrengthAsync(Session s, int strength, CancellationToken ct)
    {
        try
        {
            var value = (byte)Math.Clamp(strength, 0, 100);
            var p = (await s.Device.CallAsync(FeatureId.Haptics, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params;
            if ((p[0] & 1) != 0 && p[1] == value) return true;
            await s.Device.CallAsync(FeatureId.Haptics, 2, new byte[] { 1, value }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>Current haptic strength 0-100 (0x19B0 fn1); null when disconnected or the read fails.</summary>
    public async Task<int?> GetHapticStrengthAsync(CancellationToken ct)
    {
        var s = CurrentSession;
        if (s == null || !IsConnected) return null;
        try
        {
            return (await s.Device.CallAsync(FeatureId.Haptics, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[1];
        }
        catch (HidppException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>Sets haptic strength now (if connected) and on every reconnect; null leaves the device value alone.</summary>
    public async Task SetHapticStrengthAsync(int? strength, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _hapticDesired = strength;
            var s = CurrentSession;
            if (s == null || !IsConnected || strength is not { } v) return;
            await TrySetHapticStrengthAsync(s, v, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    /// <summary>0x2201 sensor 0: read, then fn3 [0, hi, lo] only when different. Clamped to 200-8000.</summary>
    private static async Task<bool> TrySetDpiAsync(Session s, int dpi, CancellationToken ct)
    {
        try
        {
            var value = Math.Clamp(dpi, 200, 8000);
            var p = (await s.Device.CallAsync(FeatureId.AdjustableDpi, 2, new byte[] { 0 }, ct).ConfigureAwait(false)).Params;
            if (((p[1] << 8) | p[2]) == value) return true;
            await s.Device.CallAsync(FeatureId.AdjustableDpi, 3, new byte[] { 0, (byte)(value >> 8), (byte)value }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>Reads the current sensor DPI (0x2201 fn2, read only); null when disconnected or the read fails.</summary>
    public async Task<int?> GetDpiAsync(CancellationToken ct)
    {
        var s = CurrentSession;
        if (s == null || !IsConnected) return null;
        try
        {
            var p = (await s.Device.CallAsync(FeatureId.AdjustableDpi, 2, new byte[] { 0 }, ct).ConfigureAwait(false)).Params;
            var dpi = (p[1] << 8) | p[2];
            return dpi > 0 ? dpi : null;
        }
        catch (HidppException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>Sets pointer speed now (if connected) and on every reconnect; null leaves the device value alone.</summary>
    public async Task SetDpiAsync(int? dpi, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _dpiDesired = dpi;
            var s = CurrentSession;
            if (s == null || !IsConnected || dpi is not { } d) return;
            await TrySetDpiAsync(s, d, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// 0x2121 read-modify-write: bit1 = high-resolution wheel on/off; bit0 (divert to software) is always cleared so the
    /// wheel stays native HID. Other bits are kept. No write when the byte already matches.
    /// </summary>
    internal static byte ComputeWheelMode(byte current, bool hiRes) =>
        (byte)((hiRes ? current | 0x02 : current & ~0x02) & ~0x01);

    private static async Task<bool> TrySetSmoothScrollAsync(Session s, bool hiRes, CancellationToken ct)
    {
        try
        {
            var mode = (await s.Device.CallAsync(FeatureId.HiResWheel, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[0];
            var wanted = ComputeWheelMode(mode, hiRes);
            if (wanted != mode)
                await s.Device.CallAsync(FeatureId.HiResWheel, 2, new[] { wanted }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    /// <summary>Sets smooth (hi-res) scrolling now (if connected) and on every reconnect.</summary>
    public async Task SetSmoothScrollAsync(bool enabled, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _smoothDesired = enabled;
            var s = CurrentSession;
            if (s == null || !IsConnected) return;
            await TrySetSmoothScrollAsync(s, enabled, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
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
