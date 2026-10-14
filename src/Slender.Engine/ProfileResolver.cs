namespace Slender.Engine;

/// <summary>Foreground process name -> matching Profile, else the default profile (AppProcess null).</summary>
public static class ProfileResolver
{
    private static readonly Profile Empty = new(null, new Dictionary<ushort, ActionSpec>());

    public static Profile Resolve(SlenderConfig config, string? processName)
    {
        if (!string.IsNullOrEmpty(processName))
        {
            var name = Normalize(processName);
            foreach (var p in config.Profiles)
                if (p.AppProcess is not null && string.Equals(Normalize(p.AppProcess), name, StringComparison.OrdinalIgnoreCase))
                    return p;
        }
        return Default(config);
    }

    public static Profile Default(SlenderConfig config)
    {
        foreach (var p in config.Profiles)
            if (p.AppProcess is null) return p;
        return Empty;
    }

    // Tolerate "EXCEL.EXE" in config.
    private static string Normalize(string s) =>
        s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
}
