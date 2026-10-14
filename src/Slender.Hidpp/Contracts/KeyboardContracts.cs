namespace Slender.Hidpp;

/// <summary>Keyboard-only HID++ feature IDs (kept apart from <see cref="FeatureId"/>). See docs/protocol.md, ERGO K860.</summary>
public static class KeyboardFeatureId
{
    public const ushort FnInversion = 0x40A0;
    public const ushort FnInversionWithDefaultState = 0x40A2;
    public const ushort FnInversionMultiHost = 0x40A3;
}

/// <summary>ERGO K860 control IDs (decimal CIDs from Options+ slot ids).</summary>
public static class KeyboardId
{
    public const ushort BrightnessDown = 199;
    public const ushort BrightnessUp = 200;
    public const ushort TaskView = 224;
    public const ushort SwitchApps = 256;
    public const ushort ShowDesktop = 110;
    public const ushort Search = 212;
    public const ushort PreviousTrack = 228;
    public const ushort PlayPause = 229;
    public const ushort NextTrack = 230;
    public const ushort Mute = 231;
    public const ushort VolumeDown = 232;
    public const ushort VolumeUp = 233;
    public const ushort ScreenCapture = 191;
    public const ushort Calculator = 10;
    public const ushort LookUp = 234;
    public const ushort Lock = 111;
    public const ushort EasySwitch1 = 209;
    public const ushort EasySwitch2 = 210;
    public const ushort EasySwitch3 = 211;
}

/// <summary>
/// High-level ERGO K860 surface consumed by Slender.Engine. Implementations raise events on a background thread and
/// never block it; handlers must not call back synchronously.
/// </summary>
public interface ILogiKeyboard : IDisposable
{
    string Name { get; }
    bool IsConnected { get; }

    event EventHandler<bool>? ConnectionChanged;

    /// <summary>A diverted key went down or up (ControlId = the key's CID, see <see cref="KeyboardId"/>).</summary>
    event EventHandler<ButtonEvent>? KeyChanged;

    /// <summary>Starts the connect/reconnect loop. Re-applies diverts and Fn lock after every reconnect.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Replaces the set of diverted keys (others are returned to native).</summary>
    Task SetDivertedKeysAsync(IReadOnlyCollection<ushort> keyIds, CancellationToken ct);

    /// <summary>true = the F-row sends F1..F12 (Fn inversion on); false = media/special keys.</summary>
    Task SetFnLockAsync(bool enabled, CancellationToken ct);

    /// <summary>Battery state, or null when unavailable or disconnected.</summary>
    Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct);
}
