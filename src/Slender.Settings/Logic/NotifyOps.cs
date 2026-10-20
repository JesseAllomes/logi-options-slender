using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Pure edits and listing for the Haptics page's Notifications menu. No UI types, so the tests compile it directly.</summary>
public static class NotifyOps
{
    /// <summary>One row of the menu: an app name, when the tray last saw it (null when only configured) and how often.</summary>
    public sealed record AppRow(string Name, DateTimeOffset? LastSeen, int Count);

    /// <summary>
    /// Every app that has notified plus every app with its own cue, one row each, most recently seen first, then the
    /// configured-only apps by name.
    /// </summary>
    public static List<AppRow> Rows(IEnumerable<SeenApp> seen, NotifyHapticSettings settings)
    {
        var rows = new Dictionary<string, AppRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in seen)
            rows[a.Name] = new AppRow(a.Name, new DateTimeOffset(Math.Clamp(a.LastTicks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), TimeSpan.Zero), a.Count);
        foreach (string name in settings.Apps.Keys)
            rows.TryAdd(name, new AppRow(name, null, 0));
        return rows.Values.OrderByDescending(r => r.LastSeen ?? DateTimeOffset.MinValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Stores an app's cue. A cue equal to the default drops the override so config.json only holds what the user changed.</summary>
    public static HapticSettings WithAppCue(HapticSettings settings, string app, HapticCue cue)
    {
        cue = cue.Clamped();
        var apps = new Dictionary<string, HapticCue>(settings.Notify.Apps, StringComparer.OrdinalIgnoreCase);
        if (cue == settings.Notify.Default.Clamped()) apps.Remove(app);
        else apps[app] = cue;
        return settings with { Notify = settings.Notify with { Apps = apps } };
    }

    public static HapticSettings WithEnabled(HapticSettings settings, bool on) => settings with { Notify = settings.Notify with { Enabled = on } };

    public static HapticSettings WithDefault(HapticSettings settings, HapticCue cue) => settings with { Notify = settings.Notify with { Default = cue.Clamped() } };

    /// <summary>"3 times, last 14:05" style hint for a row.</summary>
    public static string SeenText(AppRow row, DateTimeOffset now)
    {
        if (row.LastSeen is not { } at) return "not seen yet";
        var local = at.ToLocalTime();
        string when = local.Date == now.ToLocalTime().Date ? local.ToString("HH:mm") : local.ToString("d MMM");
        return $"{row.Count} time{(row.Count == 1 ? "" : "s")}, last {when}";
    }
}
