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

    /// <summary>The cue configured for an event whether or not it is on: the override, else the default (what the Settings page shows).</summary>
    public static HapticCue Resolve(HapticSettings settings, HapticEvent e) =>
        (settings.Cues.TryGetValue(e, out var own) ? own : Default(e, settings)).Clamped();

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
