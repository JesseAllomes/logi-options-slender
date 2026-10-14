namespace Slender.Hidpp;

public sealed class LogiMouseOptions : LogiDeviceOptions;

/// <summary>MX Master 4 over HID++ 2.0. Events fire on background threads; handlers must not call back synchronously.</summary>
public sealed class LogiMouse : LogiDeviceBase, ILogiMouse
{
    /// <summary>0x19B0 function that plays a waveform. Docs say fn4, a comment says fn3 (PROBE); switch here.</summary>
    public const byte HapticPlayFunction = 4;

    /// <summary>MX Master 4 controls that can be diverted (0x1B04 flags 0x31, confirmed on device 2026-10-15).</summary>
    private static readonly ushort[] KnownDivertable =
        [ControlId.Middle, ControlId.Back, ControlId.Forward, ControlId.Gesture, ControlId.ModeShift, ControlId.SensePanel];

    private bool _thumbDesired;
    private bool _thumbApplied;

    public LogiMouse(IHidppTransportFactory factory, LogiMouseOptions? options = null)
        : base(factory, options ?? new LogiMouseOptions()) { }

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
        lock (State) thumb = _thumbDesired;
        if (await TrySetThumbAsync(s, thumb, ct).ConfigureAwait(false) && thumb)
            lock (State) _thumbApplied = true;
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

    private static async Task<bool> TrySetThumbAsync(Session s, bool diverted, CancellationToken ct)
    {
        try
        {
            // Keep the user's invert setting (getConfig p[1] bit0); only the mode changes.
            var current = await s.Device.CallAsync(FeatureId.ThumbWheel, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
            var invert = (byte)(current.Params[1] & 0x01);
            await s.Device.CallAsync(FeatureId.ThumbWheel, 2, new byte[] { diverted ? (byte)1 : (byte)0, invert, 0 }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    public Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> controlIds, CancellationToken ct) =>
        SetDivertedCoreAsync(controlIds, ct);

    public async Task SetThumbWheelDivertedAsync(bool diverted, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _thumbDesired = diverted;
            var s = CurrentSession;
            if (s == null || !IsConnected) return;
            if (await TrySetThumbAsync(s, diverted, ct).ConfigureAwait(false))
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
        lock (State) thumb = _thumbApplied;
        if (thumb) await TrySetThumbAsync(s, false, ct).ConfigureAwait(false);
    }
}
