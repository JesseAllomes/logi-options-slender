using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Settings.Logic;

/// <summary>Pure edits for the Haptics page: per-event cues, named alerts and My patterns. No UI types, so the tests compile it directly.</summary>
public static class HapticOps
{
    /// <summary>The most Cycles entries that are plain choices (1x, 2x, 3x); a cue with more shows its own count as an extra entry.</summary>
    public const int CyclesChoices = 3;

    /// <summary>The marker after a saved pattern's name in a Pattern list.</summary>
    public const string PatternMark = " *";

    /// <summary>The events in page order.</summary>
    public static readonly HapticEvent[] EventOrder =
    [
        HapticEvent.ButtonPress, HapticEvent.RingOpen, HapticEvent.RingHover, HapticEvent.RingRun, HapticEvent.HoldStart,
        HapticEvent.TopHoldEngage, HapticEvent.TouchRest, HapticEvent.TouchTap, HapticEvent.TouchDoubleTap,
        HapticEvent.WheelModeChange, HapticEvent.BatteryLow, HapticEvent.Alert,
    ];

    /// <summary>0 / 1 / 2 for Light / Normal / Strong (no strength counts as Normal); -1 for a strength set by hand, "Custom (N)".</summary>
    public static int LevelIndex(HapticCue cue) => HapticCues.LevelOf(cue.Strength);

    public static HapticCue WithLevel(HapticCue cue, int index) => cue with { Strength = HapticCues.StrengthOf(index) };

    /// <summary>The entries of a Strength box: the three levels, plus "Custom (N)" for a hand-edited strength.</summary>
    public static List<string> LevelChoices(HapticCue cue)
    {
        var items = new List<string>(HapticCues.LevelNames);
        if (LevelIndex(cue) < 0) items.Add($"Custom ({cue.Strength})");
        return items;
    }

    /// <summary>0 / 1 / 2 for 1x / 2x / 3x; 3 (the extra entry) for more cycles.</summary>
    public static int CyclesIndex(HapticCue cue) => cue.Pulses <= CyclesChoices ? Math.Max(cue.Pulses, 1) - 1 : CyclesChoices;

    public static HapticCue WithCycles(HapticCue cue, int index) => cue with { Pulses = Math.Clamp(index, 0, CyclesChoices - 1) + 1 };

    /// <summary>The entries of a Cycles box: 1x, 2x, 3x, plus "Nx" when the cue has more.</summary>
    public static List<string> CyclesChoicesFor(HapticCue cue)
    {
        var items = new List<string> { "1x", "2x", "3x" };
        if (cue.Pulses > CyclesChoices) items.Add($"{cue.Pulses}x");
        return items;
    }

    /// <summary>True when the cue plays a saved pattern that still exists (its own length is then ignored).</summary>
    public static bool UsesPattern(HapticSettings settings, HapticCue cue) => cue.Custom is { } n && settings.Patterns.ContainsKey(n);

    /// <summary>The saved pattern names in list order.</summary>
    public static List<string> PatternNamesSorted(HapticSettings settings) => [.. settings.Patterns.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The entries of a Pattern box: the 15 built-ins, then each saved pattern with a trailing " *".</summary>
    public static List<string> PatternChoices(HapticSettings settings) =>
        [.. HapticCues.PatternNames, .. PatternNamesSorted(settings).Select(n => n + PatternMark)];

    /// <summary>The Pattern box entry a cue shows.</summary>
    public static int PatternChoiceIndex(HapticSettings settings, HapticCue cue)
    {
        if (cue.Custom is { } n && settings.Patterns.ContainsKey(n))
        {
            int i = PatternNamesSorted(settings).FindIndex(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return HapticCues.PatternNames.Count + i;
        }
        return Math.Clamp(cue.Pattern, 0, HapticCue.PatternMax);
    }

    /// <summary>The cue after picking an entry of the Pattern box: a built-in waveform (drops any reference) or a saved pattern.</summary>
    public static HapticCue WithPatternChoice(HapticSettings settings, HapticCue cue, int index)
    {
        int builtIn = HapticCues.PatternNames.Count;
        if (index < builtIn) return cue with { Pattern = Math.Max(index, 0), Custom = null };
        var names = PatternNamesSorted(settings);
        return index - builtIn < names.Count ? cue with { Custom = names[index - builtIn] } : cue;
    }

    /// <summary>
    /// Stores the cue for an event. A cue identical to the built-in one drops the override, so config.json only holds what
    /// the user changed.
    /// </summary>
    public static HapticSettings WithCue(HapticSettings settings, HapticEvent e, HapticCue cue)
    {
        cue = cue.Clamped();
        var cues = new Dictionary<HapticEvent, HapticCue>(settings.Cues);
        // Picking Normal writes strength 66, which plays the same as the default's none: still no override.
        var plain = cue.Strength == HapticCues.NormalStrength ? cue with { Strength = null } : cue;
        if (plain == HapticCues.Default(e, settings).Clamped()) cues.Remove(e);
        else cues[e] = cue;
        return settings with { Cues = cues };
    }

    public static HapticSettings WithAlertCue(HapticSettings settings, string name, HapticCue cue)
    {
        var alerts = new Dictionary<string, HapticCue>(settings.Alerts, StringComparer.OrdinalIgnoreCase);
        if (alerts.ContainsKey(name)) alerts[name] = cue.Clamped();
        return settings with { Alerts = alerts };
    }

    public static HapticSettings WithoutAlert(HapticSettings settings, string name)
    {
        var alerts = new Dictionary<string, HapticCue>(settings.Alerts, StringComparer.OrdinalIgnoreCase);
        alerts.Remove(name);
        return settings with { Alerts = alerts };
    }

    /// <summary>Why a new alert name cannot be used; null when it is fine.</summary>
    public static string? AlertNameProblem(HapticSettings settings, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return "Type a name first.";
        if (!JsonConfigStore.IsAlertName(name)) return "Use letters, digits, - or _ only, up to 32 characters.";
        if (settings.Alerts.ContainsKey(name)) return "That name is already in the list.";
        return null;
    }

    /// <summary>Adds an alert with the default Alert cue; the caller checks <see cref="AlertNameProblem"/> first.</summary>
    public static HapticSettings WithNewAlert(HapticSettings settings, string name)
    {
        var alerts = new Dictionary<string, HapticCue>(settings.Alerts, StringComparer.OrdinalIgnoreCase)
        {
            [name.Trim()] = HapticCues.Default(HapticEvent.Alert, settings).Clamped() with { Enabled = true },
        };
        return settings with { Alerts = alerts };
    }

    /// <summary>Why a pattern cannot be saved under this name; null when it is fine. Saving over an existing name replaces it.</summary>
    public static string? PatternNameProblem(HapticSettings settings, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return "Type a name first.";
        if (HapticCues.PatternNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return "That is the name of a built-in pattern.";
        if (!HapticCues.IsPatternName(name)) return "Use letters, digits, space, - or _ only, up to 32 characters.";
        if (!settings.Patterns.ContainsKey(name) && settings.Patterns.Count >= HapticCues.MaxPatterns) return $"You can keep up to {HapticCues.MaxPatterns} patterns.";
        return null;
    }

    /// <summary>
    /// Saves a pattern; the same name (any case) is overwritten and takes the new spelling, and cues that use it follow. The
    /// caller checks <see cref="PatternNameProblem"/> first.
    /// </summary>
    public static HapticSettings WithPattern(HapticSettings settings, string name, HapticPattern pattern)
    {
        var patterns = new Dictionary<string, HapticPattern>(settings.Patterns, StringComparer.OrdinalIgnoreCase);
        name = name.Trim();
        patterns.Remove(name);
        patterns[name] = pattern.Clamped();
        return settings with { Patterns = patterns };
    }

    /// <summary>
    /// Deletes a pattern. Every cue that used it (events, alerts, notification apps) keeps what it played: it copies the
    /// pattern's waveform, pulses, spacing and repeats and drops the reference.
    /// </summary>
    public static HapticSettings WithoutPattern(HapticSettings settings, string name)
    {
        if (!settings.Patterns.TryGetValue(name, out var p)) return settings;
        HapticCue Scrub(HapticCue c) =>
            string.Equals(c.Custom, name, StringComparison.OrdinalIgnoreCase) ? p.ToCue() with { Enabled = c.Enabled, Strength = c.Strength } : c;
        var patterns = new Dictionary<string, HapticPattern>(settings.Patterns, StringComparer.OrdinalIgnoreCase);
        patterns.Remove(name);
        return settings with
        {
            Patterns = patterns,
            Cues = settings.Cues.ToDictionary(kv => kv.Key, kv => Scrub(kv.Value)),
            Alerts = new Dictionary<string, HapticCue>(settings.Alerts.ToDictionary(kv => kv.Key, kv => Scrub(kv.Value)), StringComparer.OrdinalIgnoreCase),
            Notify = settings.Notify with
            {
                Default = Scrub(settings.Notify.Default),
                Apps = new Dictionary<string, HapticCue>(settings.Notify.Apps.ToDictionary(kv => kv.Key, kv => Scrub(kv.Value)), StringComparer.OrdinalIgnoreCase),
            },
        };
    }

    /// <summary>The command another program runs to play an alert.</summary>
    public static string Command(string? name) => string.IsNullOrEmpty(name) ? "Slender.Buzz.exe" : $"Slender.Buzz.exe {name}";
}
