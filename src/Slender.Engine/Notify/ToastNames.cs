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
