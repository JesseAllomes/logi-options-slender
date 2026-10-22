namespace Slender.Settings.Logic;

/// <summary>
/// The device pictures that ship with Slender (user 2026-10-23): photoreal pictures generated with Codex from the real devices and
/// approved by the user, in Assets/Devices beside the app. Nothing is copied from Logi Options+ or Yealink USB Connect. Hotspots
/// are plain percentages of the picture, measured on these files.
/// </summary>
public static class BuiltInPictures
{
    public const string Folder = "Devices";

    private static Hotspot H(ushort id, string name, string slot, double x, double y, int side) => new(id, name, slot, x, y, side);

    public static readonly CachedDevice Mouse = Make(DeviceIds.Mouse, "mx-master-4.webp", 992, 1536,
    [
        H(0x0052, "Middle click", "SLOT_NAME_MIDDLE_BUTTON", 74.6, 19.0, 1),
        H(0x00C4, "Mode shift", "SLOT_NAME_MODESHIFT_BUTTON", 79.5, 39.5, 1),
        H(0xF150, "Thumb wheel", "SLOT_NAME_THUMBWHEEL", 56.3, 56.4, 1),
        H(0x0056, "Forward", "SLOT_NAME_FORWARD_BUTTON", 42.7, 47.0, 1),
        H(0x0053, "Back", "SLOT_NAME_BACK_BUTTON", 47.4, 60.0, 1),
        H(0x00C3, "Gesture button", "SLOT_NAME_GESTURE_BUTTON", 38.9, 36.1, -1),
        H(0x01A0, "Sense Panel", "ASSIGNMENT_NAME_SHOW_RADIAL_MENU", 31.0, 48.9, -1),
    ]);

    public static readonly CachedDevice Keyboard = Make(DeviceIds.Keyboard, "ergo-k860.webp", 1502, 833,
    [
        H(0x00C7, "Brightness down", "SLOT_NAME_BRIGHTNESS_DOWN", 9.2, 7.2, -1),
        H(0x00C8, "Brightness up", "SLOT_NAME_BRIGHTNESS_UP", 13.1, 7.6, -1),
        H(0x00E0, "Task view", "SLOT_NAME_MISSION_CONTROL", 17.0, 8.3, -1),
        H(0x0100, "Switch apps", "SLOT_NAME_SWITCH_APPLICATIONS", 20.9, 9.2, -1),
        H(0x006E, "Show desktop", "SLOT_NAME_SHOW_DESKTOP", 24.7, 10.3, -1),
        H(0x00D4, "Search", "SLOT_NAME_SEARCH", 29.0, 11.7, -1),
        H(0x00E4, "Previous track", "SLOT_NAME_PREVIOUS", 36.0, 11.7, -1),
        H(0x00E5, "Play / pause", "SLOT_NAME_PLAY_PAUSE", 41.2, 10.4, -1),
        H(0x00E6, "Next track", "SLOT_NAME_NEXT", 45.2, 9.6, -1),
        H(0x00E7, "Mute", "SLOT_NAME_MUTE", 49.4, 8.8, -1),
        H(0x00E8, "Volume down", "SLOT_NAME_VOLUME_DOWN", 53.4, 8.2, -1),
        H(0x00E9, "Volume up", "SLOT_NAME_VOLUME_UP", 57.5, 7.6, -1),
        H(0x00BF, "Screen capture", "SLOT_NAME_SCREEN_CAPTURE", 65.4, 7.5, -1),
        H(0x000A, "Calculator", "SLOT_NAME_CALCULATOR", 84.4, 7.9, -1),
        H(0x00EA, "Look up", "SLOT_NAME_LOOK_UP", 92.5, 7.9, -1),
        H(0x006F, "Lock", "SLOT_NAME_LOCK", 96.5, 7.9, -1),
    ]);

    /// <summary>The WH64 Teams mono headset (the user's build).</summary>
    public static readonly CachedDevice Headset = Make(DeviceIds.Headset, "wh64-headset.webp", 917, 1153,
    [
        H(0xF302, "Teams button", YealinkSlots.TeamsButton, 23.3, 68.7, 1),
        H(0xF303, "Busylight", YealinkSlots.Busylight, 17.8, 84.3, -1),
        H(0xF304, "Boom arm mute", YealinkSlots.BoomMute, 35.1, 88.1, 1),
        H(0xF305, "Battery", YealinkSlots.Battery, 10.8, 74.4, -1),
    ]);

    public static readonly CachedDevice Base = Make(DeviceIds.Headset + "-base", "wh64-base.webp", 1048, 1116,
    [
        H(0xF311, "Desk phone key", YealinkSlots.DeskKey, 69.0, 82.7, -1),
        H(0xF312, "PC key", YealinkSlots.PcKey, 77.8, 76.6, 1),
        H(0xF313, "Teams key", YealinkSlots.BaseTeamsKey, 86.3, 70.9, 1),
        H(0xF314, "Mute light", YealinkSlots.MuteLight, 64.4, 71.5, -1),
        H(0xF315, "Charging light", YealinkSlots.ChargeLight, 69.0, 68.8, -1),
        H(0xF316, "Call light", YealinkSlots.CallLight, 73.6, 65.8, 1),
    ]);

    public static CachedDevice For(DeviceKind kind) => kind == DeviceKind.Mouse ? Mouse : Keyboard;

    public static IReadOnlyList<CachedDevice> All => [Mouse, Keyboard, Headset, Base];

    private static CachedDevice Make(string id, string file, int width, int height, IReadOnlyList<Hotspot> hotspots)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", Folder, file);
        return new CachedDevice(id, path, path, new DeviceLayout(file, file, width, height, hotspots), width, height);
    }
}
