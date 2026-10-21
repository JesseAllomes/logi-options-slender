namespace Slender.Engine;

/// <summary>
/// Turns the accessible name Windows gives a toast popup into the app's name. A toast's name is its text lines, app name
/// first (as the toast shows it), so the first non-empty line is the app. Pure string work; the hook that reads the name
/// lives in the tray. Anything unreadable becomes <see cref="Unknown"/> so the default cue still plays.
/// </summary>
public static class ToastNames
{
    public const string Unknown = "Unknown app";

    /// <summary>Window titles the toast host uses for itself; not an app name.</summary>
    private static readonly string[] HostTitles = ["New notification", "Notification", "Windows.UI.Core.CoreWindow"];

    /// <summary>MSAA role of a text element (ROLE_SYSTEM_STATICTEXT).</summary>
    public const int StaticTextRole = 0x29;

    /// <summary>
    /// The app from the toast's accessible tree, read breadth first (Windows 11 build 26300, checked 2026-10-22): client
    /// "New notification" > pane > window "New notification from {app}, {title}, ..." > image, text "{app}", buttons, text lines.
    /// The first text element is the app name in any display language; the window's own name is the fallback.
    /// </summary>
    public static string AppFromParts(string? clientName, IEnumerable<(int Role, string? Name)> parts)
    {
        foreach (var (role, name) in parts)
        {
            if (role != StaticTextRole || string.IsNullOrWhiteSpace(name)) continue;
            string app = AppFromAccessibleName(name);
            if (app != Unknown) return app;
        }

        return AppFromAccessibleName(clientName);
    }

    public static string AppFromAccessibleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Unknown;
        foreach (string raw in name.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = Clean(raw);
            if (line.Length == 0 || Array.Exists(HostTitles, h => string.Equals(h, line, StringComparison.OrdinalIgnoreCase))) continue;
            return line;
        }

        return Unknown;
    }

    private static string Clean(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.Trim()) sb.Append(char.IsControl(c) ? ' ' : c);
        string t = sb.ToString().Trim();
        return t.Length > NotifyHapticSettings.MaxAppNameLength ? t[..NotifyHapticSettings.MaxAppNameLength].TrimEnd() : t;
    }
}
