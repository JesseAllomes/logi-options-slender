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
        new(ControlId.Forward, "Forward", "Forward"),
        new(ControlId.Back, "Back", "Back"),
        new(ControlId.Gesture, "Gesture button", "Not assigned"),
        new(ControlId.SensePanel, "Sense Panel", "Not assigned"),
    ];

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
    ];

    public static IReadOnlyList<ControlInfo> For(DeviceKind kind) => kind == DeviceKind.Mouse ? MouseControls : KeyboardControls;

    public static ControlInfo? Find(DeviceKind kind, ushort id) => For(kind).FirstOrDefault(c => c.Id == id);

    public static string DeviceName(DeviceKind kind) => kind == DeviceKind.Mouse ? "MX Master 4" : "ERGO K860";

    public static string DeviceId(DeviceKind kind) => kind == DeviceKind.Mouse ? DeviceIds.Mouse : DeviceIds.Keyboard;
}
