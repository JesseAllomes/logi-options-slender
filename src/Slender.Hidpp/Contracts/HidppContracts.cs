namespace Slender.Hidpp;

/// <summary>HID++ 2.0 feature IDs used by Slender. See docs/protocol.md.</summary>
public static class FeatureId
{
    public const ushort Root = 0x0000;
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

/// <summary>Diverted thumb wheel report. Rotation is signed, positive = right.</summary>
public readonly record struct ThumbWheelEvent(int Rotation, bool Touch, bool Proximity, bool SingleTap);

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

    /// <summary>Starts the connect/reconnect loop. Re-applies diverts after every reconnect.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Replaces the set of diverted controls (others are undiverted).</summary>
    Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> controlIds, CancellationToken ct);

    Task SetThumbWheelDivertedAsync(bool diverted, CancellationToken ct);

    /// <summary>Fire-and-forget haptic pulse. No-op when unsupported or disconnected.</summary>
    Task PlayHapticAsync(byte waveform, CancellationToken ct);

    Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct);

    /// <summary>Current sensor DPI (0x2201 read); null when disconnected or unsupported.</summary>
    Task<int?> GetDpiAsync(CancellationToken ct) => Task.FromResult<int?>(null);
}
