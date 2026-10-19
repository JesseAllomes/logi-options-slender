using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Settings.Logic;

public enum DeviceKind
{
    Mouse,
    Keyboard,
}

/// <summary>Well-known device identifiers. Ids match SlenderStatus.Devices[].Id; model ids are Options+ model ids.</summary>
public static class DeviceIds
{
    public const string Mouse = "mx-master-4";
    public const string Keyboard = "ergo-k860";
    public const string MouseModel = "2b042";
    public const string KeyboardModel = "6b359";

    /// <summary>The thumb wheel is not a HID++ control id; Slender stores its binding under this pseudo id.</summary>
    public const ushort ThumbWheelPseudoId = 0xF150;

    /// <summary>Thumb wheel touch (finger resting on the wheel; press on touch, release on lift). Needs the wheel diverted in that app.</summary>
    public const ushort ThumbWheelTouchPseudoId = 0xF151;

    /// <summary>Thumb wheel tap (touch then lift within 250 ms and under a quarter notch of rotation). Needs the wheel diverted in that app.</summary>
    public const ushort ThumbWheelTapPseudoId = 0xF152;

    /// <summary>Thumb wheel double tap (a second tap within 300 ms). With it set, a single tap waits 300 ms first.</summary>
    public const ushort ThumbWheelDoubleTapPseudoId = 0xF153;

    /// <summary>Gesture swipes are not controls either: pseudo ids 0xF1C0 + (int)GestureDirection, stored in Profile.GestureSwipes.</summary>
    public const ushort SwipePseudoIdBase = 0xF1C0;

    public static ushort SwipeId(GestureDirection direction) => (ushort)(SwipePseudoIdBase + (int)direction);

    public static bool TryGetSwipe(ushort id, out GestureDirection direction)
    {
        direction = (GestureDirection)(id - SwipePseudoIdBase);
        return id >= SwipePseudoIdBase && id < SwipePseudoIdBase + 4;
    }
}

/// <summary>One assignable control: a mouse button, the thumb wheel or a keyboard key.</summary>
public sealed record ControlInfo(ushort Id, string Name, string NativeText);

/// <summary>A copper hotspot on a device render. X and Y are percent of the image; LabelSide -1 = left, 1 = right.</summary>
public sealed record Hotspot(ushort ControlId, string Name, string Slot, double X, double Y, int LabelSide);

/// <summary>Parsed Options+ image layout for one device image.</summary>
public sealed record DeviceLayout(string ImageFile, string ThumbFile, int Width, int Height, IReadOnlyList<Hotspot> Hotspots);

/// <summary>Static knowledge about the two supported devices. Used with or without Options+ assets.</summary>
public static class ControlCatalog
{
    public static IReadOnlyList<ControlInfo> MouseControls { get; } =
    [
        new(ControlId.Middle, "Middle click", "Middle click"),
        new(ControlId.ModeShift, "Mode shift", "Ratchet / free-spin"),
        new(DeviceIds.ThumbWheelPseudoId, "Thumb wheel", "Scroll sideways"),
        new(DeviceIds.ThumbWheelTouchPseudoId, "Thumb wheel touch", "Not assigned"),
        new(DeviceIds.ThumbWheelTapPseudoId, "Thumb wheel tap", "Not assigned"),
        new(DeviceIds.ThumbWheelDoubleTapPseudoId, "Thumb wheel double tap", "Not assigned"),
        new(ControlId.Forward, "Forward", "Forward"),
        new(ControlId.Back, "Back", "Back"),
        new(ControlId.Gesture, "Gesture click", "Not assigned"),
        new(DeviceIds.SwipeId(GestureDirection.Up), "Gesture swipe up", "Not assigned"),
        new(DeviceIds.SwipeId(GestureDirection.Down), "Gesture swipe down", "Not assigned"),
        new(DeviceIds.SwipeId(GestureDirection.Left), "Gesture swipe left", "Not assigned"),
        new(DeviceIds.SwipeId(GestureDirection.Right), "Gesture swipe right", "Not assigned"),
        new(ControlId.SensePanel, "Sense Panel", "Not assigned"),
    ];

    /// <summary>The four swipe rows, in Up, Down, Left, Right order.</summary>
    /// <summary>The thumb wheel touch and tap rows, shown next to the thumb wheel.</summary>
    public static IReadOnlyList<ControlInfo> ThumbTouchControls =>
        MouseControls.Where(c => c.Id is DeviceIds.ThumbWheelTouchPseudoId or DeviceIds.ThumbWheelTapPseudoId or DeviceIds.ThumbWheelDoubleTapPseudoId).ToList();

    public static IReadOnlyList<ControlInfo> SwipeControls => MouseControls.Where(c => DeviceIds.TryGetSwipe(c.Id, out _)).ToList();

    public static IReadOnlyList<ControlInfo> KeyboardControls { get; } =
    [
        new(199, "Brightness down", "Brightness down"),
        new(200, "Brightness up", "Brightness up"),
        new(224, "Task view", "Task view"),
        new(256, "Switch apps", "Switch apps"),
        new(110, "Show desktop", "Show desktop"),
        new(212, "Search", "Search"),
        new(228, "Previous track", "Previous track"),
        new(229, "Play / pause", "Play / pause"),
        new(230, "Next track", "Next track"),
        new(231, "Mute", "Mute"),
        new(232, "Volume down", "Volume down"),
        new(233, "Volume up", "Volume up"),
        new(191, "Screen capture", "Screen capture"),
        new(10, "Calculator", "Calculator"),
        new(234, "Look up", "Look up"),
        new(111, "Lock", "Lock"),
        // Spare divertable controls found by the 2026-10-15 inventory; labels from Solaar's task names (SpareKeys).
        new(KeyboardId.Spare00EB, SpareKeys.Label(KeyboardId.Spare00EB), "Not assigned"),
        new(KeyboardId.Spare00EC, SpareKeys.Label(KeyboardId.Spare00EC), "Not assigned"),
        new(KeyboardId.Spare0101, SpareKeys.Label(KeyboardId.Spare0101), "Not assigned"),
    ];

    /// <summary>The three spare keys, bindable like the rest but with no spot on the keyboard picture.</summary>
    public static IReadOnlyList<ControlInfo> SpareKeyControls => KeyboardControls.Where(c => SpareKeys.IsSpare(c.Id)).ToList();

    public static IReadOnlyList<ControlInfo> For(DeviceKind kind) => kind == DeviceKind.Mouse ? MouseControls : KeyboardControls;

    public static ControlInfo? Find(DeviceKind kind, ushort id) => For(kind).FirstOrDefault(c => c.Id == id);

    public static string DeviceName(DeviceKind kind) => kind == DeviceKind.Mouse ? "MX Master 4" : "ERGO K860";

    public static string DeviceId(DeviceKind kind) => kind == DeviceKind.Mouse ? DeviceIds.Mouse : DeviceIds.Keyboard;
}
