namespace Slender.Settings.Logic;

/// <summary>One suggested shortcut: what it does and the chord, in ChordRecorder order (Win, Ctrl, Alt, Shift, key).</summary>
public sealed record Shortcut(string Name, string Chord);

/// <summary>A titled list of suggested shortcuts. Id is stable (hide keys use it); Title is what the picker shows.</summary>
public sealed record ShortcutGroup(string Id, string Title, IReadOnlyList<Shortcut> Items);

/// <summary>
/// Suggested shortcuts for the picker's Shortcuts tab: the app's own list (by process name, when known), then common
/// editing shortcuts, then Windows. Pure data; every chord parses with ChordParser (tests check it).
/// </summary>
public static class ShortcutLibrary
{
    public const string CommonId = "common";
    public const string WindowsId = "windows";

    private static Shortcut S(string name, string chord) => new(name, chord);

    public static readonly IReadOnlyList<Shortcut> Common =
    [
        S("Copy", "Ctrl+C"), S("Cut", "Ctrl+X"), S("Paste", "Ctrl+V"), S("Paste as plain text", "Ctrl+Shift+V"),
        S("Undo", "Ctrl+Z"), S("Redo", "Ctrl+Y"), S("Select all", "Ctrl+A"), S("Save", "Ctrl+S"),
        S("Find", "Ctrl+F"), S("Print", "Ctrl+P"), S("New", "Ctrl+N"), S("Open", "Ctrl+O"),
        S("Close tab or document", "Ctrl+W"), S("Next tab", "Ctrl+Tab"), S("Previous tab", "Ctrl+Shift+Tab"),
        S("Zoom in", "Ctrl+Plus"), S("Zoom out", "Ctrl+Minus"), S("Back", "Alt+Left"), S("Forward", "Alt+Right"),
        S("Refresh", "F5"), S("Rename", "F2"), S("Context menu", "Shift+F10"), S("Close window", "Alt+F4"),
    ];

    public static readonly IReadOnlyList<Shortcut> Windows =
    [
        S("Show desktop", "Win+D"), S("Task view", "Win+Tab"), S("Switch apps", "Alt+Tab"), S("Lock", "Win+L"),
        S("Clipboard history", "Win+V"), S("Snip & copy", "Win+Shift+S"), S("Screenshot to file", "Win+PrintScreen"),
        S("Search", "Win+S"), S("File Explorer", "Win+E"), S("Settings", "Win+I"), S("Run", "Win+R"),
        S("Quick Link menu", "Win+X"), S("Emoji panel", "Win+Period"), S("Voice typing", "Win+H"),
        S("Notifications", "Win+N"), S("Quick settings", "Win+A"), S("Snap layouts", "Win+Z"),
        S("Snap left", "Win+Left"), S("Snap right", "Win+Right"), S("Maximise", "Win+Up"), S("Minimise", "Win+Down"),
        S("Minimise all", "Win+M"), S("Move window to next monitor", "Win+Shift+Right"),
        S("Move window to previous monitor", "Win+Shift+Left"), S("Next desktop", "Win+Ctrl+Right"),
        S("Previous desktop", "Win+Ctrl+Left"), S("New desktop", "Win+Ctrl+D"), S("Close desktop", "Win+Ctrl+F4"),
        S("Project / displays", "Win+P"), S("Magnifier zoom in", "Win+Plus"), S("Magnifier zoom out", "Win+Minus"),
        S("Task Manager", "Ctrl+Shift+Esc"),
    ];

    private static readonly ShortcutGroup Excel = new("excel", "Excel",
    [
        S("Next sheet", "Ctrl+PageDown"), S("Previous sheet", "Ctrl+PageUp"), S("Edit cell", "F2"),
        S("Repeat / lock reference", "F4"), S("Format cells", "Ctrl+1"), S("Insert cells", "Ctrl+Shift+Plus"),
        S("Delete cells", "Ctrl+Minus"), S("Fill down", "Ctrl+D"), S("Fill right", "Ctrl+R"), S("Paste special", "Ctrl+Alt+V"),
        S("AutoSum", "Alt+Plus"), S("Filter on / off", "Ctrl+Shift+L"), S("Select column", "Ctrl+Space"),
        S("Select row", "Shift+Space"), S("Today's date", "Ctrl+Semicolon"), S("Flash fill", "Ctrl+E"),
        S("Show formulas", "Ctrl+Backtick"), S("New sheet", "Shift+F11"), S("Recalculate", "F9"),
        S("Group", "Alt+Shift+Right"), S("Ungroup", "Alt+Shift+Left"), S("Make a table", "Ctrl+T"),
    ]);

    private static readonly ShortcutGroup PowerPoint = new("powerpoint", "PowerPoint",
    [
        S("Start slide show", "F5"), S("Show from this slide", "Shift+F5"), S("New slide", "Ctrl+M"),
        S("Duplicate", "Ctrl+D"), S("Group", "Ctrl+G"), S("Ungroup", "Ctrl+Shift+G"), S("Bigger font", "Ctrl+Shift+Period"),
        S("Smaller font", "Ctrl+Shift+Comma"), S("Centre text", "Ctrl+E"), S("Black screen (in a show)", "B"),
        S("End show", "Esc"),
    ]);

    private static readonly ShortcutGroup Word = new("word", "Word",
    [
        S("Heading 1", "Ctrl+Alt+1"), S("Heading 2", "Ctrl+Alt+2"), S("Heading 3", "Ctrl+Alt+3"),
        S("Normal style", "Ctrl+Shift+N"), S("Page break", "Ctrl+Enter"), S("Track changes", "Ctrl+Shift+E"),
        S("Word count", "Ctrl+Shift+G"), S("Bullets", "Ctrl+Shift+L"), S("Bold", "Ctrl+B"), S("Italic", "Ctrl+I"),
        S("Underline", "Ctrl+U"), S("Insert link", "Ctrl+K"), S("Centre", "Ctrl+E"), S("Spelling", "F7"),
    ]);

    private static readonly ShortcutGroup Outlook = new("outlook", "Outlook",
    [
        S("New email", "Ctrl+Shift+M"), S("Reply", "Ctrl+R"), S("Reply all", "Ctrl+Shift+R"), S("Forward", "Ctrl+F"),
        S("Send", "Ctrl+Enter"), S("Mark as read", "Ctrl+Q"), S("Mark as unread", "Ctrl+U"), S("Mail", "Ctrl+1"),
        S("Calendar", "Ctrl+2"), S("Search", "Ctrl+E"),
    ]);

    private static readonly ShortcutGroup Browser = new("browser", "Browser",
    [
        S("Next tab", "Ctrl+Tab"), S("Previous tab", "Ctrl+Shift+Tab"), S("New tab", "Ctrl+T"), S("Close tab", "Ctrl+W"),
        S("Reopen closed tab", "Ctrl+Shift+T"), S("Back", "Alt+Left"), S("Forward", "Alt+Right"), S("Refresh", "F5"),
        S("Hard refresh", "Ctrl+F5"), S("Address bar", "Ctrl+L"), S("Find on page", "Ctrl+F"), S("Zoom in", "Ctrl+Plus"),
        S("Zoom out", "Ctrl+Minus"), S("Reset zoom", "Ctrl+0"), S("Downloads", "Ctrl+J"), S("History", "Ctrl+H"),
        S("New window", "Ctrl+N"), S("Bookmark page", "Ctrl+D"), S("First tab", "Ctrl+1"), S("Last tab", "Ctrl+9"),
        S("Full screen", "F11"), S("Developer tools", "F12"),
    ]);

    private static readonly ShortcutGroup Teams = new("teams", "Teams",
    [
        S("Mute / unmute", "Ctrl+Shift+M"), S("Camera on / off", "Ctrl+Shift+O"), S("Leave call", "Ctrl+Shift+H"),
        S("Raise / lower hand", "Ctrl+Shift+K"), S("Share screen", "Ctrl+Shift+E"), S("Blur background", "Ctrl+Shift+P"),
        S("Accept video call", "Ctrl+Shift+A"), S("Accept audio call", "Ctrl+Shift+S"), S("Decline call", "Ctrl+Shift+D"),
        S("Search", "Ctrl+E"), S("New chat", "Ctrl+N"),
    ]);

    private static readonly ShortcutGroup Acrobat = new("acrobat", "Acrobat",
    [
        S("Next page", "Right"), S("Previous page", "Left"), S("Go to page", "Ctrl+Shift+N"), S("Zoom in", "Ctrl+Plus"),
        S("Zoom out", "Ctrl+Minus"), S("Fit page", "Ctrl+0"), S("Actual size", "Ctrl+1"), S("Fit width", "Ctrl+2"),
        S("Full screen", "Ctrl+L"), S("Rotate clockwise", "Ctrl+Shift+Plus"), S("Rotate anticlockwise", "Ctrl+Shift+Minus"),
        S("Find", "Ctrl+F"), S("Advanced search", "Ctrl+Shift+F"), S("Hand tool", "H"), S("Select tool", "V"),
    ]);

    private static readonly ShortcutGroup Explorer = new("explorer", "File Explorer",
    [
        S("New folder", "Ctrl+Shift+N"), S("Rename", "F2"), S("Address bar", "Alt+D"), S("Up a folder", "Alt+Up"),
        S("Back", "Alt+Left"), S("Forward", "Alt+Right"), S("Properties", "Alt+Enter"), S("Preview pane", "Alt+P"),
        S("Details pane", "Alt+Shift+P"), S("Copy as path", "Ctrl+Shift+C"), S("New tab", "Ctrl+T"),
        S("Close tab", "Ctrl+W"), S("Next tab", "Ctrl+Tab"), S("New window", "Ctrl+N"), S("Search", "Ctrl+F"),
    ]);

    private static readonly ShortcutGroup VsCode = new("vscode", "VS Code",
    [
        S("Command palette", "Ctrl+Shift+P"), S("Quick open", "Ctrl+P"), S("Terminal", "Ctrl+Backtick"),
        S("Comment line", "Ctrl+Slash"), S("Go to definition", "F12"), S("Rename symbol", "F2"),
        S("Format document", "Alt+Shift+F"), S("Find in files", "Ctrl+Shift+F"), S("Side bar", "Ctrl+B"),
        S("Split editor", "Ctrl+Backslash"), S("Next editor", "Ctrl+PageDown"), S("Previous editor", "Ctrl+PageUp"),
    ]);

    private static readonly Dictionary<string, ShortcutGroup> ByProcess = new(StringComparer.OrdinalIgnoreCase)
    {
        ["excel"] = Excel,
        ["powerpnt"] = PowerPoint,
        ["winword"] = Word,
        ["outlook"] = Outlook, ["olk"] = Outlook,
        ["msedge"] = Browser, ["chrome"] = Browser, ["firefox"] = Browser, ["island"] = Browser, ["brave"] = Browser,
        ["opera"] = Browser, ["vivaldi"] = Browser, ["arc"] = Browser,
        ["ms-teams"] = Teams, ["teams"] = Teams,
        ["acrobat"] = Acrobat, ["acrord32"] = Acrobat,
        ["explorer"] = Explorer,
        ["code"] = VsCode,
    };

    /// <summary>Every group, for tests.</summary>
    public static IEnumerable<ShortcutGroup> AllGroups =>
        ByProcess.Values.Distinct().Append(new ShortcutGroup(CommonId, "Common", Common)).Append(new ShortcutGroup(WindowsId, "Windows", Windows));

    /// <summary>
    /// Groups for one app profile (null = All apps): the app's own list titled with its name, then common shortcuts the app
    /// list does not already have, then Windows.
    /// </summary>
    public static IReadOnlyList<ShortcutGroup> For(string? appProcess, string? appName = null)
    {
        var groups = new List<ShortcutGroup>(3);
        HashSet<string> inApp = new(StringComparer.OrdinalIgnoreCase);
        if (appProcess is not null && ByProcess.TryGetValue(appProcess, out ShortcutGroup? app))
        {
            groups.Add(app with { Title = string.IsNullOrWhiteSpace(appName) ? app.Title : appName });
            inApp.UnionWith(app.Items.Select(i => i.Chord));
        }

        groups.Add(new ShortcutGroup(CommonId, "Common", [.. Common.Where(s => !inApp.Contains(s.Chord))]));
        groups.Add(new ShortcutGroup(WindowsId, "Windows", Windows));
        return groups;
    }

    /// <summary>The key that hides one entry of one group (stored in settings-ui.json).</summary>
    public static string HideKey(string groupId, string chord) => groupId + "|" + chord;

    /// <summary>The Windows name for a chord ("Clipboard history" for Win+V), else null. Windows chords mean the same everywhere.</summary>
    public static string? WindowsName(string chord) =>
        Windows.FirstOrDefault(s => string.Equals(s.Chord, chord, StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>True when the chord is one of the suggestions shown for this app.</summary>
    public static bool Contains(string? appProcess, string chord) =>
        For(appProcess).Any(g => g.Items.Any(s => string.Equals(s.Chord, chord, StringComparison.OrdinalIgnoreCase)));
}
