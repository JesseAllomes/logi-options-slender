using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>How an action reads on a label: plain text plus optional key-cap chips.</summary>
public sealed record ActionLabel(string Text, IReadOnlyList<string> Chips);

/// <summary>One choice in a picker tab.</summary>
public sealed record ActionOption(string Title, ActionSpec Action);

/// <summary>Human text and picker options for ActionSpec values. Pure.</summary>
public static class ActionCatalog
{
    private static readonly Dictionary<string, string> ChordNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Win+Shift+S"] = "Snip & copy",
        ["Win+Tab"] = "Task view",
        ["Win+D"] = "Show desktop",
        ["Win+L"] = "Lock",
        ["Win+S"] = "Search",
        ["Alt+Tab"] = "Switch apps",
        ["Alt+Left"] = "Back",
        ["Alt+Right"] = "Forward",
        ["Ctrl+PageDown"] = "Next tab",
        ["Ctrl+PageUp"] = "Previous tab",
        ["Ctrl+Z"] = "Undo",
        ["Ctrl+Y"] = "Redo",
        ["Ctrl+C"] = "Copy",
        ["Ctrl+V"] = "Paste",
    };

    private static readonly Dictionary<string, string> ChordWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VolumeUp"] = "Volume up",
        ["VolumeDown"] = "Volume down",
        ["MediaNext"] = "Next track",
        ["MediaPrevious"] = "Previous track",
        ["MediaPlayPause"] = "Play / pause",
        ["MediaStop"] = "Stop",
        ["VolumeMute"] = "Mute",
        ["Ctrl+Plus"] = "Zoom in",
        ["Ctrl+Minus"] = "Zoom out",
        ["Ctrl+PageDown"] = "Next tab",
        ["Ctrl+PageUp"] = "Previous tab",
        ["Right"] = "Right",
        ["Left"] = "Left",
    };

    /// <summary>Two-direction thumb wheel encoding: "Right|Left" sends the first chord per notch right and the second per notch left.</summary>
    public static string EncodePair(string right, string left) => right + "|" + left;

    public static bool TryDecodePair(string chord, out string right, out string left)
    {
        right = left = string.Empty;
        int bar = chord.IndexOf('|');
        if (bar <= 0 || bar == chord.Length - 1 || chord.IndexOf('|', bar + 1) >= 0)
        {
            return false;
        }

        right = chord[..bar].Trim();
        left = chord[(bar + 1)..].Trim();
        return right.Length > 0 && left.Length > 0;
    }

    /// <summary>"Volume up / down" for VolumeUp|VolumeDown; falls back to "A / B" with readable names.</summary>
    public static string PairName(string right, string left)
    {
        string a = ChordWords.GetValueOrDefault(right, right);
        string b = ChordWords.GetValueOrDefault(left, left);
        string[] wa = a.Split(' ');
        string[] wb = b.Split(' ');
        if (wa.Length == 2 && wb.Length == 2 && wa[0].Equals(wb[0], StringComparison.OrdinalIgnoreCase))
        {
            return wa[0] + " " + wa[1].ToLowerInvariant() + " / " + wb[1].ToLowerInvariant();
        }

        if (wa.Length == 2 && wb.Length == 2 && wa[1].Equals(wb[1], StringComparison.OrdinalIgnoreCase))
        {
            return wa[0] + " / " + wb[0].ToLowerInvariant() + " " + wa[1];
        }

        return a + " / " + b;
    }

    /// <summary>Keyboard chord for an Excel command when Excel has a plain shortcut for it, else null.</summary>
    public static string? ExcelChord(ExcelCommand command) => command switch
    {
        ExcelCommand.NextSheet => "Ctrl+PageDown",
        ExcelCommand.PreviousSheet => "Ctrl+PageUp",
        _ => null,
    };

    public static string ExcelName(ExcelCommand command) => command switch
    {
        ExcelCommand.NextSheet => "Next sheet",
        ExcelCommand.PreviousSheet => "Previous sheet",
        ExcelCommand.ZoomIn => "Zoom in",
        ExcelCommand.ZoomOut => "Zoom out",
        ExcelCommand.ZoomReset => "Zoom to 100%",
        ExcelCommand.ScrollToFirstColumn => "First column",
        ExcelCommand.ScrollToLastUsedColumn => "Last used column",
        ExcelCommand.CycleTableStyle => "Next table style",
        ExcelCommand.CycleChartType => "Next chart type",
        _ => command.ToString(),
    };

    /// <summary>Key chord parts for key caps: "Ctrl+PageDown" gives ["Ctrl", "PgDn"].</summary>
    public static IReadOnlyList<string> ChordParts(string chord) =>
        chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p switch { "PageDown" => "PgDn", "PageUp" => "PgUp", "Backspace" => "Bksp", _ => p })
            .ToArray();

    /// <summary>The label text for an action.</summary>
    public static ActionLabel Describe(ActionSpec action) => action switch
    {
        ActionSpec.Keys keys when TryDecodePair(keys.Chord, out string r, out string l) => new ActionLabel(PairName(r, l), []),
        ActionSpec.Keys keys => new ActionLabel(
            ChordNames.GetValueOrDefault(keys.Chord) ?? ShortcutLibrary.WindowsName(keys.Chord) ?? string.Empty, ChordParts(keys.Chord)),
        ActionSpec.Excel excel => new ActionLabel(ExcelName(excel.Command), ExcelChord(excel.Command) is { } c ? ChordParts(c) : []),
        ActionSpec.ExcelMacro macro => new ActionLabel("Macro " + macro.Name, []),
        ActionSpec.MouseButton mouse => new ActionLabel(MouseName(mouse.Button), []),
        ActionSpec.ActionsRing => new ActionLabel("Actions Ring", []),
        ActionSpec.Launch launch => new ActionLabel(LaunchName(launch.Target), []),
        ActionSpec.Disabled => new ActionLabel("Off", []),
        ActionSpec.Brightness b => new ActionLabel(b.StepPercent < 0 ? "Brightness down" : "Brightness up", []),
        ActionSpec.Text t => new ActionLabel(TextName(t.Value), []),
        ActionSpec.Macro m => new ActionLabel("Macro: " + Slender.Engine.Macros.MacroLabels.NameOf(m.Id), []),
        ActionSpec.TapHold th => new ActionLabel(TapHoldName(th), []),
        _ => new ActionLabel("Not assigned", []),
    };

    /// <summary>"Type 'Kind regards,…'": the first line of the text, cut to 20 characters; the ellipsis shows when something was cut.</summary>
    public static string TextName(string value)
    {
        string[] lines = value.Replace("\r", string.Empty).Split('\n');
        string first = lines[0].Trim();
        bool cut = lines.Skip(1).Any(l => l.Trim().Length > 0);
        if (first.Length > 20)
        {
            first = first[..20].TrimEnd();
            cut = true;
        }

        return first.Length == 0 && !cut ? "Type text" : $"Type '{first}{(cut ? "…" : string.Empty)}'";
    }

    /// <summary>"Open Excel" for an .exe or file, "Open example.com" for a URL, "Open Documents" for a folder, "Open app" for a Store app.</summary>
    public static string LaunchName(string target)
    {
        string t = target.Trim();
        if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return "Open app";
        }

        if (Uri.TryCreate(t, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0)
        {
            return "Open " + (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host);
        }

        string name = Path.GetFileName(t.TrimEnd('\\', '/'));
        if (name.Length == 0)
        {
            return t.Length == 0 ? "Open" : "Open " + t;
        }

        string ext = Path.GetExtension(name);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return "Open " + name;
    }

    public static string MouseName(string button) => button switch
    {
        "Left" => "Left click",
        "Right" => "Right click",
        "Middle" => "Middle click",
        "Back" => "Back",
        "Forward" => "Forward",
        _ => button,
    };

    /// <summary>"Press: Back · Hold: Actions Ring". An unset half reads "Nothing".</summary>
    public static string TapHoldName(ActionSpec.TapHold th) => $"Press: {TapHoldPart(th.Tap)} · Hold: {TapHoldPart(th.Hold)}";

    private static string TapHoldPart(ActionSpec action) => action is ActionSpec.None ? "Nothing" : Plain(action);

    /// <summary>
    /// Press / Hold is offered on buttons and keys, not on the gesture button (it already uses hold for swipes), the thumb wheel
    /// or a swipe direction.
    /// </summary>
    public static bool CanTapHold(DeviceKind kind, ushort controlId) =>
        kind == DeviceKind.Keyboard || (controlId != Slender.Hidpp.ControlId.Gesture
            && controlId != DeviceIds.ThumbWheelPseudoId && controlId != DeviceIds.ThumbWheelTouchPseudoId
            && controlId != DeviceIds.ThumbWheelTapPseudoId && controlId != DeviceIds.ThumbWheelDoubleTapPseudoId
            && !DeviceIds.TryGetSwipe(controlId, out _));

    /// <summary>Builds a Press / Hold action; false when either half is itself a Press / Hold (not allowed) or null.</summary>
    public static bool TryBuildTapHold(ActionSpec? tap, ActionSpec? hold, out ActionSpec.TapHold result)
    {
        result = null!;
        if (tap is null || hold is null || tap is ActionSpec.TapHold || hold is ActionSpec.TapHold)
        {
            return false;
        }

        result = new ActionSpec.TapHold(tap, hold);
        return true;
    }

    /// <summary>The preset for a control such as the Sense Panel: the chosen quick press, and a hold that opens the Actions Ring.</summary>
    public static ActionSpec.TapHold HoldOpensRing(ActionSpec tap) => new(tap is ActionSpec.TapHold ? new ActionSpec.None() : tap, new ActionSpec.ActionsRing());

    /// <summary>A typed shortcut as a Keys action; false when the engine cannot send it.</summary>
    public static bool TryParseShortcut(string? text, out ActionSpec.Keys keys)
    {
        keys = null!;
        string chord = (text ?? string.Empty).Trim();
        if (chord.Length == 0 || !ChordParser.TryParse(chord, out _))
        {
            return false;
        }

        keys = new ActionSpec.Keys(chord);
        return true;
    }

    /// <summary>
    /// What either half of a Press / Hold can be: nothing, the ring, mouse buttons, Excel, brightness, the thumb wheel switch and,
    /// on the keyboard, the app and media shortcuts. Typed shortcuts are added by the picker. Never a Press / Hold.
    /// </summary>
    public static IReadOnlyList<ActionOption> TapHoldChoices(DeviceKind kind)
    {
        var list = new List<ActionOption> { new("Nothing", new ActionSpec.None()) };
        list.AddRange(RingOptions);
        list.AddRange(MouseOptions);
        if (kind == DeviceKind.Keyboard)
        {
            list.AddRange(KeyboardAppOptions);
            list.AddRange(KeyboardMediaOptions);
        }
        else
        {
            list.AddRange(ExcelOptions);
            list.AddRange(BrightnessOptions);
        }

        return list;
    }

    /// <summary>Single-line text, for tooltips and automation names.</summary>
    public static string Plain(ActionSpec action)
    {
        ActionLabel l = Describe(action);
        return l.Chips.Count == 0 ? l.Text : (l.Text.Length == 0 ? string.Join('+', l.Chips) : l.Text + " " + string.Join('+', l.Chips));
    }

    public static IReadOnlyList<ActionOption> MouseOptions { get; } =
    [
        new("Left click", new ActionSpec.MouseButton("Left")),
        new("Right click", new ActionSpec.MouseButton("Right")),
        new("Middle click", new ActionSpec.MouseButton("Middle")),
        new("Back", new ActionSpec.MouseButton("Back")),
        new("Forward", new ActionSpec.MouseButton("Forward")),
    ];

    public static IReadOnlyList<ActionOption> ExcelOptions { get; } =
        Enum.GetValues<ExcelCommand>().Select(c => new ActionOption(ExcelName(c), new ActionSpec.Excel(c))).ToArray();

    public static IReadOnlyList<ActionOption> OffOptions { get; } =
    [
        new("Turn off", new ActionSpec.Disabled()),
    ];

    /// <summary>Changes the brightness of the monitor under the cursor (laptop panel or DDC/CI external) by 10 points per press.</summary>
    public static IReadOnlyList<ActionOption> BrightnessOptions { get; } =
    [
        new("Brightness up", new ActionSpec.Brightness(10)),
        new("Brightness down", new ActionSpec.Brightness(-10)),
    ];

    public static IReadOnlyList<ActionOption> RingOptions { get; } =
    [
        new("Show Actions Ring", new ActionSpec.ActionsRing()),
    ];

    /// <summary>Thumb wheel "Two-way" tab: one pick covers both directions.</summary>
    public static IReadOnlyList<ActionOption> ThumbPairOptions { get; } =
    [
        new("Volume up / down", new ActionSpec.Keys(EncodePair("VolumeUp", "VolumeDown"))),
        new("Next / previous track", new ActionSpec.Keys(EncodePair("MediaNext", "MediaPrevious"))),
        new("Zoom in / out", new ActionSpec.Keys(EncodePair("Ctrl+Plus", "Ctrl+Minus"))),
        new("Next / previous tab", new ActionSpec.Keys(EncodePair("Ctrl+PageDown", "Ctrl+PageUp"))),
    ];

    /// <summary>Keyboard "App" tab: Windows shortcuts that open or switch apps.</summary>
    public static IReadOnlyList<ActionOption> KeyboardAppOptions { get; } =
    [
        new("Task view", new ActionSpec.Keys("Win+Tab")),
        new("Switch apps", new ActionSpec.Keys("Alt+Tab")),
        new("Show desktop", new ActionSpec.Keys("Win+D")),
        new("Search", new ActionSpec.Keys("Win+S")),
        new("Snip & copy", new ActionSpec.Keys("Win+Shift+S")),
        new("Lock", new ActionSpec.Keys("Win+L")),
    ];

    /// <summary>Keyboard "Media" tab. These chord names need engine support for media keys (docs/settings.md).</summary>
    public static IReadOnlyList<ActionOption> KeyboardMediaOptions { get; } =
    [
        new("Play / pause", new ActionSpec.Keys("MediaPlayPause")),
        new("Stop", new ActionSpec.Keys("MediaStop")),
        new("Next track", new ActionSpec.Keys("MediaNext")),
        new("Previous track", new ActionSpec.Keys("MediaPrevious")),
        new("Mute", new ActionSpec.Keys("VolumeMute")),
        new("Volume down", new ActionSpec.Keys("VolumeDown")),
        new("Volume up", new ActionSpec.Keys("VolumeUp")),
        new("Brightness down", new ActionSpec.Brightness(-10)),
        new("Brightness up", new ActionSpec.Brightness(10)),
    ];
}

/// <summary>Turns key presses into chord strings the engine's ChordParser accepts.</summary>
public static class ChordRecorder
{
    public const ushort VkShift = 0x10;
    public const ushort VkControl = 0x11;
    public const ushort VkMenu = 0x12;
    public const ushort VkLeftWin = 0x5B;
    public const ushort VkRightWin = 0x5C;

    public static bool IsModifier(ushort vk) => vk is VkShift or VkControl or VkMenu or VkLeftWin or VkRightWin or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>The ChordParser name for a virtual key, or null when the engine has no name for it.</summary>
    public static string? KeyName(ushort vk)
    {
        if (vk is >= 0x41 and <= 0x5A)
        {
            return ((char)vk).ToString();
        }

        if (vk is >= 0x30 and <= 0x39)
        {
            return ((char)vk).ToString();
        }

        if (vk is >= 0x70 and <= 0x87)
        {
            return "F" + (vk - 0x6F);
        }

        return vk switch
        {
            0x0D => "Enter",
            0x1B => "Esc",
            0x09 => "Tab",
            0x20 => "Space",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0xBC => "Comma",
            0xBE => "Period",
            0xBD => "Minus",
            0xBB => "Plus",
            0xBA => "Semicolon",
            0xBF => "Slash",
            0xDC => "Backslash",
            0xC0 => "Backtick",
            _ => null,
        };
    }

    /// <summary>Builds "Win+Ctrl+Alt+Shift+Key". Returns null when the key has no name.</summary>
    public static string? Compose(bool win, bool ctrl, bool alt, bool shift, ushort vk)
    {
        string? key = KeyName(vk);
        if (key is null)
        {
            return null;
        }

        var parts = new List<string>(5);
        if (win)
        {
            parts.Add("Win");
        }

        if (ctrl)
        {
            parts.Add("Ctrl");
        }

        if (alt)
        {
            parts.Add("Alt");
        }

        if (shift)
        {
            parts.Add("Shift");
        }

        parts.Add(key);
        return string.Join('+', parts);
    }
}
