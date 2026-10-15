namespace Slender.Engine;

/// <summary>
/// Dual thumb wheel. Browsers have it on by default (next tab forward, previous tab back); a profile's ThumbWheelAlt
/// overrides that, and the default profile's ThumbWheelAlt applies to apps that set none.
/// </summary>
public static class ThumbWheelModes
{
    /// <summary>Next tab per notch forward, previous tab per notch back.</summary>
    public static ThumbWheelSecond Tabs { get; } = new(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Keys("Ctrl+Shift+Tab"));

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge", "chrome", "island", "firefox", "brave", "opera", "vivaldi", "arc",
    };

    /// <summary>The built-in second role for <paramref name="process"/> (no ".exe" needed), or null when there is none.</summary>
    public static ThumbWheelSecond? BuiltIn(string? process) =>
        process is not null && Browsers.Contains(Path.GetFileNameWithoutExtension(process)) ? Tabs : null;

    /// <summary>
    /// The second role in effect for the app: its own setting, else the default profile's, else the built-in preset.
    /// Null when dual mode is off or nothing is set (the toggle then does nothing).
    /// </summary>
    public static ThumbWheelSecond? SecondFor(SlenderConfig config, string? process)
    {
        Profile? own = process is null ? null : config.Profiles.FirstOrDefault(p =>
            p.AppProcess is not null && string.Equals(Path.GetFileNameWithoutExtension(p.AppProcess), Path.GetFileNameWithoutExtension(process), StringComparison.OrdinalIgnoreCase));
        ThumbWheelSecond? second = own?.ThumbWheelAlt
            ?? config.Profiles.FirstOrDefault(p => p.AppProcess is null)?.ThumbWheelAlt
            ?? BuiltIn(process);
        return second is { Enabled: true } ? second : null;
    }
}
