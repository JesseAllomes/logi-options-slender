using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Settings.Logic;

/// <summary>Pure edits for the Haptics page: per-event cues and named alerts. No UI types, so the tests compile it directly.</summary>
public static class HapticOps
{
    public static readonly string[] IntensityNames = ["Light", "Normal", "Strong"];

    /// <summary>The events in page order.</summary>
    public static readonly HapticEvent[] EventOrder =
    [
        HapticEvent.ButtonPress, HapticEvent.RingOpen, HapticEvent.RingHover, HapticEvent.RingRun, HapticEvent.HoldStart,
        HapticEvent.TopHoldEngage, HapticEvent.TouchRest, HapticEvent.TouchTap, HapticEvent.TouchDoubleTap,
        HapticEvent.WheelModeChange, HapticEvent.BatteryLow, HapticEvent.Alert,
    ];

    /// <summary>Light / Normal / Strong = 1 / 2 / 3 pulses; anything else (set by hand in config.json) is -1, "Custom".</summary>
    public static int IntensityIndex(HapticCue cue) => cue.Pulses is >= 1 and <= 3 ? cue.Pulses - 1 : -1;

    public static HapticCue WithIntensity(HapticCue cue, int index) => cue with { Pulses = Math.Clamp(index, 0, 2) + 1 };

    /// <summary>
    /// Stores the cue for an event. A cue identical to the built-in one drops the override, so config.json only holds what
    /// the user changed.
    /// </summary>
    public static HapticSettings WithCue(HapticSettings settings, HapticEvent e, HapticCue cue)
    {
        cue = cue.Clamped();
        var cues = new Dictionary<HapticEvent, HapticCue>(settings.Cues);
        if (cue == HapticCues.Default(e, settings).Clamped()) cues.Remove(e);
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

    /// <summary>The command another program runs to play an alert.</summary>
    public static string Command(string? name) => string.IsNullOrEmpty(name) ? "Slender.Buzz.exe" : $"Slender.Buzz.exe {name}";
}
