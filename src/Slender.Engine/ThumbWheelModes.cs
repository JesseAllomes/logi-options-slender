namespace Slender.Engine;

/// <summary>
/// The thumb wheel's second action, used while the top button is held. Nothing by default; a profile's ThumbWheelAlt sets it
/// for that app, and the default profile's ThumbWheelAlt applies to apps that set none.
/// </summary>
public static class ThumbWheelModes
{
    /// <summary>Next tab per notch forward, previous tab per notch back.</summary>
    public static ThumbWheelSecond Tabs { get; } = new(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Keys("Ctrl+Shift+Tab"));

    /// <summary>
    /// The second action in effect for the app: its own setting, else the default profile's. Null when it is off or neither
    /// direction does anything (holding the top button then changes nothing).
    /// </summary>
    /// <summary>Second action speed for <paramref name="second"/>: its own, else PointerSettings.SecondActionSpeed.</summary>
    public static int SpeedOf(SlenderConfig config, ThumbWheelSecond second) => second.Speed ?? config.Pointer.SecondActionSpeed;

    /// <summary>Side scroll speed in the app: its own profile's, else the default profile's, else PointerSettings.SideScrollSpeed.</summary>
    public static int SideScrollSpeedFor(SlenderConfig config, string? process) =>
        Own(config, process)?.SideScrollSpeed
        ?? config.Profiles.FirstOrDefault(p => p.AppProcess is null)?.SideScrollSpeed
        ?? config.Pointer.SideScrollSpeed;

    private static Profile? Own(SlenderConfig config, string? process) => process is null ? null : config.Profiles.FirstOrDefault(p =>
        p.AppProcess is not null && string.Equals(Path.GetFileNameWithoutExtension(p.AppProcess), Path.GetFileNameWithoutExtension(process), StringComparison.OrdinalIgnoreCase));

    public static ThumbWheelSecond? SecondFor(SlenderConfig config, string? process)
    {
        Profile? own = process is null ? null : config.Profiles.FirstOrDefault(p =>
            p.AppProcess is not null && string.Equals(Path.GetFileNameWithoutExtension(p.AppProcess), Path.GetFileNameWithoutExtension(process), StringComparison.OrdinalIgnoreCase));
        ThumbWheelSecond? second = own?.ThumbWheelAlt
            ?? config.Profiles.FirstOrDefault(p => p.AppProcess is null)?.ThumbWheelAlt;
        return second is { Enabled: true } && (second.Forward is not ActionSpec.None || second.Back is not ActionSpec.None) ? second : null;
    }
}
