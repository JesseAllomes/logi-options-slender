namespace Slender.Engine;

/// <summary>
/// The thumb wheel's second action, used while the top button is held. Nothing by default; a profile's ThumbWheelAlt sets it
/// for that app, and the default profile's ThumbWheelAlt applies to apps that set none.
/// </summary>
public static class ThumbWheelModes
{
    /// <summary>Next tab per notch forward, previous tab per notch back (the Left + Right chord's fallback).</summary>
    public static ThumbWheelSecond Tabs { get; } = new(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Keys("Ctrl+Shift+Tab"));

    /// <summary>
    /// The second action in effect for the app: its own setting, else the default profile's. Null when it is off or neither
    /// direction does anything (holding the top button then changes nothing).
    /// </summary>
    public static ThumbWheelSecond? SecondFor(SlenderConfig config, string? process)
    {
        Profile? own = process is null ? null : config.Profiles.FirstOrDefault(p =>
            p.AppProcess is not null && string.Equals(Path.GetFileNameWithoutExtension(p.AppProcess), Path.GetFileNameWithoutExtension(process), StringComparison.OrdinalIgnoreCase));
        ThumbWheelSecond? second = own?.ThumbWheelAlt
            ?? config.Profiles.FirstOrDefault(p => p.AppProcess is null)?.ThumbWheelAlt;
        return second is { Enabled: true } && (second.Forward is not ActionSpec.None || second.Back is not ActionSpec.None) ? second : null;
    }
}
