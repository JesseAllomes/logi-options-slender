namespace Slender.Engine.Macros;

/// <summary>
/// Lets the pure label code (the ring, the settings labels) show a macro by its name without holding the config: the tray and
/// Settings each point <see cref="Resolver"/> at their live config.
/// </summary>
public static class MacroLabels
{
    public static volatile Func<string, string?>? Resolver;

    /// <summary>The macro's name; "Macro (deleted)" when the config has no such macro; "Macro" when there is no resolver to ask.</summary>
    public static string NameOf(string id)
    {
        var resolver = Resolver;
        if (resolver is null) return "Macro";
        try { return resolver(id) is { Length: > 0 } name ? name : "Macro (deleted)"; }
        catch { return "Macro"; }
    }
}
