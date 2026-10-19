using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>One of the user's shortcuts with its position in SlenderConfig.MyShortcuts (edits and deletes use it).</summary>
public sealed record MyShortcutEntry(int Index, CustomShortcut Shortcut);

/// <summary>
/// Pure edits of SlenderConfig.MyShortcuts, the user's own named shortcuts in the picker's Shortcuts tab (user 2026-10-19).
/// An entry with no app shows for every app; one with an app shows only for that app.
/// </summary>
public static class MyShortcutOps
{
    public const int MaxNameLength = 60;

    /// <summary>The entries the Shortcuts tab shows for <paramref name="app"/> (null = All apps: only the everywhere ones).</summary>
    public static IReadOnlyList<MyShortcutEntry> For(SlenderConfig config, string? app) =>
        config.MyShortcuts
            .Select((s, i) => new MyShortcutEntry(i, s))
            .Where(e => e.Shortcut.App is null || (app is not null && SameApp(e.Shortcut.App, app)))
            .ToList();

    /// <summary>Why the entry cannot be saved, or null when it can.</summary>
    public static string? Problem(string? name, string? chord)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give it a name.";
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return $"Keep the name under {MaxNameLength} characters.";
        }

        return string.IsNullOrWhiteSpace(chord) || !ChordParser.TryParse(chord, out _) ? "Press the keys for it." : null;
    }

    public static SlenderConfig Add(SlenderConfig config, string name, string chord, string? app) =>
        config with { MyShortcuts = [.. config.MyShortcuts, Make(name, chord, app)] };

    /// <summary>Replaces the entry at <paramref name="index"/>; an index that no longer exists changes nothing.</summary>
    public static SlenderConfig Update(SlenderConfig config, int index, string name, string chord, string? app)
    {
        if (index < 0 || index >= config.MyShortcuts.Count)
        {
            return config;
        }

        var list = config.MyShortcuts.ToList();
        list[index] = Make(name, chord, app);
        return config with { MyShortcuts = list };
    }

    public static SlenderConfig Remove(SlenderConfig config, int index)
    {
        if (index < 0 || index >= config.MyShortcuts.Count)
        {
            return config;
        }

        var list = config.MyShortcuts.ToList();
        list.RemoveAt(index);
        return config with { MyShortcuts = list };
    }

    private static CustomShortcut Make(string name, string chord, string? app) =>
        new(name.Trim(), chord.Trim(), string.IsNullOrWhiteSpace(app) ? null : Path.GetFileNameWithoutExtension(app.Trim()));

    private static bool SameApp(string a, string b) =>
        string.Equals(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b), StringComparison.OrdinalIgnoreCase);
}
