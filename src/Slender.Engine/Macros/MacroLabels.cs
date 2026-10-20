namespace Slender.Engine.Macros;

/// <summary>
/// Lets the pure label code (the ring, the settings labels) show a macro by its name without holding the config: the tray and
/// Settings each point <see cref="Resolver"/> at their live config.
/// </summary>
public static class MacroLabels
{
    public static volatile Func<string, string?>? Resolver;

    /// <summary>The macro's name, or "Macro" when it is unknown (deleted, or the resolver is not set).</summary>
    public static string NameOf(string id)
    {
        try { if (Resolver?.Invoke(id) is { Length: > 0 } name) return name; }
        catch { }
        return "Macro";
    }
}
