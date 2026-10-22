using Slender.Engine.Ring;

namespace Slender.Engine;

/// <summary>
/// What each <see cref="HapticEvent"/> buzzes: the user's override when there is one, else the built-in default, which
/// reproduces what Slender did before cues became configurable.
/// </summary>
public static class HapticCues
{
    /// <summary>
    /// The 15 built-in waveforms of the MX Master 4 (0x19B0 fn4, index 0-14). The names come from the Solaar project and are
    /// not verified on this device: they are a hint, not a promise.
    /// </summary>
    public static readonly IReadOnlyList<string> PatternNames =
    [
        "Sharp state change", "Damp state change", "Sharp collision", "Damp collision", "Subtle collision",
        "Happy alert", "Angry alert", "Completed", "Square", "Wave", "Firework", "Mad", "Knock", "Jingle", "Ringing",
    ];

    /// <summary>Light / Normal / Strong buzz strengths (0-100 on the mouse; Strong is the device maximum). Settings and the player share these.</summary>
    public const int LightStrength = 33, NormalStrength = 66, StrongStrength = 100;

    public static readonly IReadOnlyList<string> LevelNames = ["Light", "Normal", "Strong"];

    /// <summary>0 / 1 / 2 for Light / Normal / Strong; null (no strength set) counts as Normal; -1 for any other number (set by hand in config.json).</summary>
    public static int LevelOf(int? strength) => strength switch
    {
        null or NormalStrength => 1,
        LightStrength => 0,
        StrongStrength => 2,
        _ => -1,
    };

    /// <summary>The strength of a level index (clamped into 0-2).</summary>
    public static int StrengthOf(int level) => Math.Clamp(level, 0, 2) switch { 0 => LightStrength, 1 => NormalStrength, _ => StrongStrength };

    /// <summary>The most patterns a config keeps, and the longest name.</summary>
    public const int MaxPatterns = 50, MaxPatternNameLength = 32;

    /// <summary>
    /// A usable name for a saved pattern: letters, digits, space, '-' or '_', up to 32 characters, no space at either end, and
    /// not the name of a built-in waveform (those would be ambiguous in the Pattern list).
    /// </summary>
    public static bool IsPatternName(string? name) =>
        name is { Length: > 0 and <= MaxPatternNameLength }
        && name == name.Trim()
        && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-' or '_')
        && !PatternNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Waveform name for a pattern index (clamped into 0-14).</summary>
    public static string PatternName(int pattern) => PatternNames[Math.Clamp(pattern, 0, HapticCue.PatternMax)];

    /// <summary>
    /// The built-in cue for an event. The legacy switches decide which are on: button press follows OnButtonPress, the ring's
    /// open, hover and run follow OnActionsRing (null = a fresh HapticSettings, i.e. the defaults).
    /// </summary>
    public static HapticCue Default(HapticEvent e, HapticSettings? legacy = null)
    {
        legacy ??= new HapticSettings();
        return e switch
        {
            HapticEvent.ButtonPress => new HapticCue(legacy.OnButtonPress, 1),
            HapticEvent.RingOpen => new HapticCue(legacy.OnActionsRing, 1),
            HapticEvent.RingHover => new HapticCue(legacy.OnActionsRing, RingHaptics.HoverWaveform),
            HapticEvent.RingRun => new HapticCue(legacy.OnActionsRing, RingHaptics.RunWaveform),
            HapticEvent.HoldStart => new HapticCue(true, 1),
            HapticEvent.BatteryLow => new HapticCue(false, 6),
            HapticEvent.Alert => new HapticCue(true, 5),
            _ => new HapticCue(false, 1), // TopHoldEngage, TouchRest, TouchTap, TouchDoubleTap, WheelModeChange
        };
    }

    /// <summary>The cue configured for an event whether or not it is on: the override, else the default, with any pattern reference still in place (what the Settings rows edit).</summary>
    public static HapticCue Configured(HapticSettings settings, HapticEvent e) =>
        (settings.Cues.TryGetValue(e, out var own) ? own : Default(e, settings)).Clamped();

    /// <summary>The same as <see cref="Configured"/> with a pattern reference expanded (what the mouse will play).</summary>
    public static HapticCue Resolve(HapticSettings settings, HapticEvent e) => Expand(settings, Configured(settings, e));

    /// <summary>
    /// Replaces a cue's waveform, pulses, spacing and repeats with those of the pattern it names; Enabled and Strength stay
    /// the cue's and the reference is dropped. A cue with no reference, or one naming a pattern that is gone, comes back as it is.
    /// </summary>
    public static HapticCue Expand(HapticSettings settings, HapticCue cue)
    {
        if (cue.Custom is not { } name || !settings.Patterns.TryGetValue(name, out var p)) return cue;
        return p.ToCue() with { Enabled = cue.Enabled, Strength = cue.Strength };
    }

    /// <summary>What a Test button sends: expanded, on, and with the strength filled in so the tray needs no lookup.</summary>
    public static HapticCue ForTest(HapticSettings settings, HapticCue cue) =>
        (Expand(settings, cue) with { Enabled = true, Custom = null }).WithStrength().Clamped();

    /// <summary>The cue with its strength set (null becomes Normal).</summary>
    public static HapticCue WithStrength(this HapticCue cue) => cue with { Strength = cue.Strength ?? NormalStrength };

    /// <summary>What to play for an event now; null when haptics are off or this event's cue is off.</summary>
    public static HapticCue? Effective(HapticSettings settings, HapticEvent e)
    {
        if (!settings.Enabled) return null;
        var cue = Resolve(settings, e);
        return cue.Enabled ? cue : null;
    }

    /// <summary>
    /// What to play for a named alert: the user's alert of that name, else the Alert event's cue (an unknown or empty name);
    /// null when haptics are off or that cue is off.
    /// </summary>
    public static HapticCue? EffectiveAlert(HapticSettings settings, string? name)
    {
        if (!settings.Enabled) return null;
        if (!string.IsNullOrEmpty(name) && settings.Alerts.TryGetValue(name, out var own))
        {
            var cue = own.Clamped();
            return cue.Enabled ? cue : null;
        }
        return Effective(settings, HapticEvent.Alert);
    }

    /// <summary>
    /// The built-in ringer: the "Ringing" waveform twice, Strong, then a pause, like a desk phone. Repeats stay 1 because the ringer session
    /// does the repeating; the cue's pause is the quiet time between rings.
    /// </summary>
    public static readonly HapticCue DefaultRinger = new(true, 14, 2, 250, StrongStrength, 1, 1500);

    /// <summary>Plain-language names for the Settings page.</summary>
    public static string EventLabel(HapticEvent e) => e switch
    {
        HapticEvent.ButtonPress => "Button press",
        HapticEvent.RingOpen => "Actions Ring opens",
        HapticEvent.RingHover => "Ring highlight moves",
        HapticEvent.RingRun => "Ring action runs",
        HapticEvent.HoldStart => "Press-and-hold reached",
        HapticEvent.TopHoldEngage => "Top button hold starts",
        HapticEvent.TouchRest => "Thumb rests on wheel",
        HapticEvent.TouchTap => "Thumb wheel tap",
        HapticEvent.TouchDoubleTap => "Thumb wheel double tap",
        HapticEvent.WheelModeChange => "Scroll wheel ratchet/free-spin changes",
        HapticEvent.BatteryLow => "Battery low",
        _ => "Alert (no name)",
    };
}
