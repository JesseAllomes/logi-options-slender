namespace Slender.Hidpp;

/// <summary>HID++ 2.0 feature IDs used by Slender. See docs/protocol.md.</summary>
public static class FeatureId
{
    public const ushort Root = 0x0000;
    public const ushort DeviceFwVersion = 0x0003;
    public const ushort FeatureSet = 0x0001;
    public const ushort DeviceName = 0x0005;
    public const ushort Battery = 0x1000;
    public const ushort UnifiedBattery = 0x1004;
    public const ushort Haptics = 0x19B0;
    public const ushort ForceSensing = 0x19C0;
    public const ushort ReprogControlsV4 = 0x1B04;
    public const ushort SmartShift = 0x2110;
    public const ushort SmartShiftEnhanced = 0x2111;
    public const ushort HiResWheel = 0x2121;
    public const ushort ThumbWheel = 0x2150;
    public const ushort AdjustableDpi = 0x2201;
}

/// <summary>MX Master 4 control IDs. Entries marked PROBE in docs/protocol.md are unconfirmed.</summary>
public static class ControlId
{
    public const ushort Middle = 0x0052;
    public const ushort Back = 0x0053;
    public const ushort Forward = 0x0056;
    public const ushort Gesture = 0x00C3;
    public const ushort ModeShift = 0x00C4;
    public const ushort VirtualGesture = 0x00D7;
    public const ushort DpiSwitch = 0x00FD;
    public const ushort SensePanel = 0x01A0;
}

/// <summary>Raw HID report pipe to one HID++ collection. Long reports (0x11, 20 bytes) only.</summary>
public interface IHidppTransport : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Writes one 20-byte long report (byte 0 = 0x11).</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken ct);

    /// <summary>Reads one input report. Returns bytes read, or 0 on timeout.</summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, int timeoutMs, CancellationToken ct);
}

/// <summary>Finds HID++ collections for supported Logitech devices.</summary>
public interface IHidppTransportFactory
{
    /// <summary>Opens the first matching MX Master 4 HID++ collection, or null if none present.</summary>
    IHidppTransport? TryOpen();
}

public readonly record struct ButtonEvent(ushort ControlId, bool Pressed);

/// <summary>
/// Diverted thumb wheel report. Rotation is signed, positive = right. Status is the device's rotation status: 0 no rotation,
/// 1 a turn starts, 2 turning, 3 the turn ended (that report always says "no touch", even with the thumb still on the wheel).
/// </summary>
public readonly record struct ThumbWheelEvent(int Rotation, bool Touch, bool Proximity, bool SingleTap, int Status = 0);

/// <summary>0x1B04 divertedRawMouseXY: movement since the last report while a raw-XY-diverted control is held (device counts; +dy = down).</summary>
public readonly record struct RawXyEvent(int Dx, int Dy);

/// <summary>
/// Diverted thumb wheel counts per native wheel step (one WHEEL_DELTA of 120): 0x2150 getInfo divertedResolution 120 over
/// nativeResolution 20 = 6. A raw capture on 2026-10-19 confirmed it (a short turn = about 78 counts in 1-2 count reports);
/// the old value 120 made every diverted turn 20x too small, so the wheel seemed dead in apps that divert it (Edge).
/// </summary>
public static class ThumbWheelUnits
{
    public const int PerNotch = 6;
}

public readonly record struct BatteryStatus(int Percent, bool Charging);

/// <summary>
/// High-level MX Master 4 surface consumed by Slender.Engine. Implementations must raise events
/// on a background thread and never block it; handlers must not call back synchronously.
/// </summary>
public interface ILogiMouse : IDisposable
{
    string Name { get; }
    bool IsConnected { get; }

    event EventHandler<bool>? ConnectionChanged;
    event EventHandler<ButtonEvent>? ButtonChanged;
    event EventHandler<ThumbWheelEvent>? ThumbWheelMoved;

    /// <summary>Raw movement deltas while a raw-XY-diverted control (the gesture button) is held.</summary>
    event EventHandler<RawXyEvent>? RawXyMoved { add { } remove { } }

    /// <summary>Starts the connect/reconnect loop. Re-applies diverts after every reconnect.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Replaces the set of diverted controls (others are undiverted).</summary>
    Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> controlIds, CancellationToken ct);

    /// <summary>
    /// Like <see cref="SetDivertedControlsAsync(IReadOnlyCollection{ushort}, CancellationToken)"/>, but the controls in
    /// <paramref name="rawXyControlIds"/> (gesture button only) are diverted with raw XY. Falls back to a plain divert when refused.
    /// </summary>
    Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> controlIds, IReadOnlyCollection<ushort> rawXyControlIds, CancellationToken ct) =>
        SetDivertedControlsAsync(controlIds, ct);

    /// <summary>True while the control is diverted with raw XY on this connection, i.e. its movement arrives as <see cref="RawXyMoved"/>.</summary>
    bool IsRawXyActive(ushort controlId) => false;

    Task SetThumbWheelDivertedAsync(bool diverted, CancellationToken ct);

    /// <summary>
    /// Puts the thumb wheel (0x2150 fn2, divert bit) into HID++ reporting or back to native, keeping the invert setting.
    /// Returns true only when the device accepted it right now: false (disconnected, rejected, unsupported) means the OS
    /// still scrolls natively, so callers keep using the mouse hook. The default does the call and reports "not diverted".
    /// </summary>
    async Task<bool> TrySetThumbWheelDivertedAsync(bool diverted, CancellationToken ct)
    {
        await SetThumbWheelDivertedAsync(diverted, ct).ConfigureAwait(false);
        return false;
    }

    /// <summary>Whether 0x2150 getInfo caps include touch. null until read (or when unsupported).</summary>
    bool? ThumbWheelTouchSupported => null;

    /// <summary>Fire-and-forget haptic pulse. No-op when unsupported or disconnected.</summary>
    Task PlayHapticAsync(byte waveform, CancellationToken ct);

    Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct);

    /// <summary>Current sensor DPI (0x2201 read); null when disconnected or unsupported.</summary>
    Task<int?> GetDpiAsync(CancellationToken ct) => Task.FromResult<int?>(null);

    /// <summary>The running firmware, e.g. "RBM 27.03.0019" (0x0003 reads); null when disconnected or unsupported.</summary>
    Task<string?> GetFirmwareVersionAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    /// <summary>Current wheel resistance 0-100 (0x2111 torque); null when disconnected or unsupported.</summary>
    Task<int?> GetWheelResistanceAsync(CancellationToken ct) => Task.FromResult<int?>(null);

    /// <summary>
    /// Flips the scroll wheel between ratchet and free-spin (0x2111 mode), the top button's own job, for when Slender holds that
    /// button diverted. False when disconnected or unsupported.
    /// </summary>
    Task<bool> ToggleWheelModeAsync(CancellationToken ct) => Task.FromResult(false);

    /// <summary>Current Sense Panel force reading (0x19C0); null when disconnected or unsupported.</summary>
    Task<int?> GetSensePanelForceAsync(CancellationToken ct) => Task.FromResult<int?>(null);

    /// <summary>Current haptic strength 0-100 (0x19B0); null when disconnected or unsupported.</summary>
    Task<int?> GetHapticStrengthAsync(CancellationToken ct) => Task.FromResult<int?>(null);

    /// <summary>Sets the stored haptic strength 0-100 now and on every reconnect (null leaves the device value alone). The default does nothing.</summary>
    Task SetHapticStrengthAsync(int? strength, CancellationToken ct) => Task.CompletedTask;

    /// <summary>First Sense Panel force reading seen (the anchor of the safe band); null until known.</summary>
    int? SensePanelBaseline => null;
}
