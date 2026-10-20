namespace Slender.Hidpp;

/// <summary>Keyboard-only HID++ feature IDs (kept apart from <see cref="FeatureId"/>). See docs/protocol.md, ERGO K860.</summary>
public static class KeyboardFeatureId
{
    public const ushort FnInversion = 0x40A0;
    public const ushort FnInversionWithDefaultState = 0x40A2;
    public const ushort FnInversionMultiHost = 0x40A3;

    /// <summary>Keyboard Disable Keys (0x4521). Layout: Solaar lib/logitech_receiver/settings_templates.py DisableKeyboardKeys.</summary>
    public const ushort DisableKeys = 0x4521;
}

/// <summary>
/// Keys the K860 can switch off in its own firmware (0x4521). Bit values are Solaar's special_keys.DISABLE
/// (lib/logitech_receiver/special_keys.py): Caps_Lock 0x01, Num_Lock 0x02, Scroll_Lock 0x04, Insert 0x08, Win 0x10.
/// The K860 reports capability mask 0x1F (all five).
/// </summary>
[Flags]
public enum DisabledKeys : byte
{
    None = 0,
    CapsLock = 0x01,
    NumLock = 0x02,
    ScrollLock = 0x04,
    Insert = 0x08,
    Windows = 0x10,
    All = 0x1F,
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

    /// <summary>
    /// Divertable controls the 2026-10-15 inventory found unused on the K860 (docs/devices/ergo-k860-6b359.inventory.md).
    /// Task ids 0x00B6 / 0x00B8 / 0x00D6 (182 / 184 / 214); names from Solaar special_keys.py TASK (see <see cref="SpareKeys"/>).
    /// </summary>
    public const ushort Spare00EB = 0x00EB;
    public const ushort Spare00EC = 0x00EC;
    public const ushort Spare0101 = 0x0101;
}

/// <summary>The three spare K860 controls with the task name Solaar's special_keys.TASK table gives them, when it has one.</summary>
public static class SpareKeys
{
    /// <summary>(control id, task id, Solaar task name or null). Task 0x00B6 (182) is absent from Solaar's TASK table.</summary>
    public static readonly (ushort Cid, ushort Task, string? SolaarTask)[] All =
    [
        (KeyboardId.Spare00EB, 0x00B6, null),
        // special_keys.py TASK: "Second_Left_Click": 0x00B8 (K400 Plus)
        (KeyboardId.Spare00EC, 0x00B8, "Second left click"),
        // special_keys.py TASK: "MultiPlatform_Menu__Show__Hide_Virtual_Keyboard__Launchpad": 0x00D6
        (KeyboardId.Spare0101, 0x00D6, "Menu / virtual keyboard / Launchpad"),
    ];

    public static bool IsSpare(ushort cid) => Array.Exists(All, k => k.Cid == cid);

    /// <summary>"Second left click", or "Key 0x00EB" when Solaar does not name the task.</summary>
    public static string Label(ushort cid)
    {
        foreach (var k in All)
            if (k.Cid == cid) return k.SolaarTask ?? $"Key 0x{cid:X4}";
        return $"Key 0x{cid:X4}";
    }
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

    /// <summary>
    /// true = the F-row sends F1..F12 (Fn inversion on); false = media/special keys. Written once (on the next connect when
    /// offline); reconnects never re-write it, so Fn+Esc on the keyboard stays in charge.
    /// </summary>
    Task SetFnLockAsync(bool enabled, CancellationToken ct);

    /// <summary>The keyboard's Fn lock as last read or written; null when unknown.</summary>
    bool? FnLockNow => null;

    /// <summary>Raised with the keyboard's Fn lock whenever it is read (every connect, and <see cref="ReadFnLockAsync"/>).</summary>
    event EventHandler<bool>? FnLockRead { add { } remove { } }

    /// <summary>Reads the keyboard's Fn lock now (catches Fn+Esc); null when offline or unsupported.</summary>
    Task<bool?> ReadFnLockAsync(CancellationToken ct) => Task.FromResult<bool?>(null);

    /// <summary>
    /// Turns the chosen keys off on the keyboard itself (0x4521) and returns the mask read back, or null when the keyboard lacks
    /// the feature or is offline (the value is remembered and applied on every (re)connect).
    /// </summary>
    Task<DisabledKeys?> SetDisabledKeysAsync(DisabledKeys keys, CancellationToken ct);

    /// <summary>
    /// Fn inversion per Easy-Switch channel (0x40A3, host index 0-2). A null entry leaves that channel alone. Remembered and applied
    /// on every (re)connect.
    /// </summary>
    Task SetChannelFnLockAsync(IReadOnlyList<bool?> channels, CancellationToken ct);

    /// <summary>The Easy-Switch channel (0-2) the keyboard is on now, as far as it could be told from 0x40A3 reads; null when unknown.</summary>
    int? CurrentChannel { get; }

    /// <summary>
    /// Easy-Switch channels and the real current channel from 0x1814/0x1815 reads (display only); null until read or when the
    /// keyboard lacks them. <see cref="CurrentChannel"/> prefers this value over its 0x40A3 inference.
    /// </summary>
    HostsInfo? Hosts => null;

    /// <summary>Raised when <see cref="Hosts"/> was read and differs from the previous value.</summary>
    event EventHandler? HostsChanged { add { } remove { } }

    /// <summary>Reads the Easy-Switch channels again (read only); null when offline or unsupported.</summary>
    Task<HostsInfo?> RefreshHostsAsync(CancellationToken ct) => Task.FromResult<HostsInfo?>(null);

    /// <summary>The running firmware, e.g. "MPK 10.00.0010" (0x0003 reads); null when disconnected or unsupported.</summary>
    Task<string?> GetFirmwareVersionAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    /// <summary>Battery state, or null when unavailable or disconnected.</summary>
    Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct);
}
