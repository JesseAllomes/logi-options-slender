namespace Slender.Engine.Ringer;

/// <summary>
/// Finds the 3CX web address from installed web-app shortcuts. An app installed from Edge (or Chrome) is a shortcut to the browser's
/// proxy with "--app-url=https://host/" in its arguments, so the address can be read without asking the user (user 2026-10-22:
/// "it's a web app, not in the browser"). Pure string work; Settings reads the shortcuts.
/// </summary>
public static class WebAppShortcuts
{
    private const string AppUrlSwitch = "--app-url=";

    /// <summary>The value of --app-url in a shortcut's arguments (quoted or not); null when there is none.</summary>
    public static string? AppUrl(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments)) return null;
        int at = arguments.IndexOf(AppUrlSwitch, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        int start = at + AppUrlSwitch.Length;
        if (start >= arguments.Length) return null;
        if (arguments[start] == '"')
        {
            int close = arguments.IndexOf('"', start + 1);
            return close > start + 1 ? arguments[(start + 1)..close] : null;
        }

        int end = arguments.IndexOf(' ', start);
        string value = end < 0 ? arguments[start..] : arguments[start..end];
        return value.Length > 0 ? value : null;
    }

    /// <summary>
    /// The 3CX origin from the first shortcut whose name or web address mentions 3CX and whose address is usable
    /// (<see cref="RingerOrigin.Normalize"/>); null when none qualifies.
    /// </summary>
    public static string? Find3cx(IEnumerable<(string Name, string? Arguments)> shortcuts)
    {
        foreach (var (name, arguments) in shortcuts)
        {
            string? url = AppUrl(arguments);
            if (url is null) continue;
            if (!name.Contains("3cx", StringComparison.OrdinalIgnoreCase) && !url.Contains("3cx", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && RingerOrigin.Normalize(uri.GetLeftPart(UriPartial.Authority)) is { } origin) return origin;
        }

        return null;
    }
}
