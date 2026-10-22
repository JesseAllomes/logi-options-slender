using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Settings.Logic;

public enum DeviceKind
{
    Mouse,
    Keyboard,
}

/// <summary>Well-known device identifiers. Ids match SlenderStatus.Devices[].Id.</summary>
public static class DeviceIds
{
    public const string Mouse = "mx-master-4";
    public const string Keyboard = "ergo-k860";
    public const string Headset = "yealink-wh64";

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

/// <summary>One assignable control: a mouse button, the thumb wheel or a keyboard key. Description = where it is or what it is, for the hover text.</summary>
public sealed record ControlInfo(ushort Id, string Name, string NativeText, string? Description = null)
{
    /// <summary>Hover text: the name, then the description when there is one.</summary>
    public string Tip => Description is null ? Name : Name + Environment.NewLine + Description;
}

/// <summary>A copper hotspot on a device render. X and Y are percent of the image; LabelSide -1 = left, 1 = right.</summary>
public sealed record Hotspot(ushort ControlId, string Name, string Slot, double X, double Y, int LabelSide);

/// <summary>Image layout for one device picture: its pixel size and hotspots.</summary>
public sealed record DeviceLayout(string ImageFile, string ThumbFile, int Width, int Height, IReadOnlyList<Hotspot> Hotspots);

/// <summary>A device picture that ships with Slender, with its hotspots.</summary>
public sealed record CachedDevice(string DeviceId, string ImagePath, string ThumbPath, DeviceLayout Layout, int ImageWidth, int ImageHeight);

/// <summary>Static knowledge about the two supported devices.</summary>
public static class ControlCatalog
{
    public static IReadOnlyList<ControlInfo> MouseControls { get; } =
    [
        new(ControlId.Middle, "Middle click", "Middle click", "Pressing the main scroll wheel down until it clicks."),
        new(ControlId.ModeShift, "Mode shift", "Ratchet / free-spin", "The small button just behind the scroll wheel. Out of the box it switches the wheel between clicky (ratchet) and free-spin."),
        new(DeviceIds.ThumbWheelPseudoId, "Thumb wheel", "Scroll sideways", "The small wheel on the side, under your thumb. Turning it scrolls sideways unless you change it."),
        new(DeviceIds.ThumbWheelTouchPseudoId, "Thumb wheel touch", "Not assigned", "Resting your thumb on the thumb wheel without turning it."),
        new(DeviceIds.ThumbWheelTapPseudoId, "Thumb wheel tap", "Not assigned", "One quick tap on the thumb wheel without turning it."),
        new(DeviceIds.ThumbWheelDoubleTapPseudoId, "Thumb wheel double tap", "Not assigned", "Two quick taps on the thumb wheel."),
        new(ControlId.Forward, "Forward", "Forward", "The front one of the two side buttons by your thumb."),
        new(ControlId.Back, "Back", "Back", "The rear one of the two side buttons by your thumb."),
        new(ControlId.Gesture, "Gesture click", "Not assigned", "Pressing and letting go of the gesture button without moving the mouse."),
        new(DeviceIds.SwipeId(GestureDirection.Up), "Gesture swipe up", "Not assigned", "Hold the gesture button and move the mouse up."),
        new(DeviceIds.SwipeId(GestureDirection.Down), "Gesture swipe down", "Not assigned", "Hold the gesture button and move the mouse down."),
        new(DeviceIds.SwipeId(GestureDirection.Left), "Gesture swipe left", "Not assigned", "Hold the gesture button and move the mouse left."),
        new(DeviceIds.SwipeId(GestureDirection.Right), "Gesture swipe right", "Not assigned", "Hold the gesture button and move the mouse right."),
        new(ControlId.SensePanel, "Sense Panel", "Not assigned", "The pressure-sensitive pad on the thumb rest. It buzzes back when you press it."),
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
        // Spare divertable controls found by the 2026-10-15 inventory. No printed key is known for them, so they are numbered; the
        // hover text keeps Logitech's internal task name (Solaar, SpareKeys) and points at Identify.
        new(KeyboardId.Spare00EB, "Spare key 1", "Not assigned", SpareTip(KeyboardId.Spare00EB)),
        new(KeyboardId.Spare00EC, "Spare key 2", "Not assigned", SpareTip(KeyboardId.Spare00EC)),
        new(KeyboardId.Spare0101, "Spare key 3", "Not assigned", SpareTip(KeyboardId.Spare0101)),
    ];

    private static string SpareTip(ushort cid) =>
        $"A key the K860 reports but has no printed name for{(SpareKeys.All.First(k => k.Cid == cid).SolaarTask is { } task ? $" (Logitech's internal name: {task})" : "")}. "
        + "It may not exist on your keyboard. Press Identify, then press keys, to find out which one it is.";

    /// <summary>The three spare keys, bindable like the rest but with no spot on the keyboard picture.</summary>
    public static IReadOnlyList<ControlInfo> SpareKeyControls => KeyboardControls.Where(c => SpareKeys.IsSpare(c.Id)).ToList();

    public static IReadOnlyList<ControlInfo> For(DeviceKind kind) => kind == DeviceKind.Mouse ? MouseControls : KeyboardControls;

    public static ControlInfo? Find(DeviceKind kind, ushort id) => For(kind).FirstOrDefault(c => c.Id == id);

    public static string DeviceName(DeviceKind kind) => kind == DeviceKind.Mouse ? "MX Master 4" : "ERGO K860";

    public static string DeviceId(DeviceKind kind) => kind == DeviceKind.Mouse ? DeviceIds.Mouse : DeviceIds.Keyboard;
}
