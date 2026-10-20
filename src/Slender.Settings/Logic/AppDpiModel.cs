using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Pure logic for the per-app pointer speed rows: Profile.Dpi null = follow All apps, a value overrides it in that app.</summary>
public static class AppDpiModel
{
    /// <summary>The apps that can carry their own speed: every profile except All apps.</summary>
    public static IReadOnlyList<Profile> Apps(SlenderConfig config) => config.Profiles.Where(p => p.AppProcess is not null).ToList();

    /// <summary>The app's own speed, or null while it follows All apps.</summary>
    public static int? Own(SlenderConfig config, string app) => ProfileOps.Find(config, app)?.Dpi;

    /// <summary>What an app without its own speed gets: the All apps profile's, else the global pointer speed; null = the mouse's own.</summary>
    public static int? Inherited(SlenderConfig config) => ProfileOps.Find(config, null)?.Dpi ?? config.Pointer.Dpi;

    /// <summary>Sets (or with null clears) the app's own speed, snapped to the sensor's real steps (<paramref name="scale"/>, else the fixed 50-step range) and clamped to its range. Never creates a profile just to clear.</summary>
    public static SlenderConfig Set(SlenderConfig config, string app, int? dpi, DpiScale? scale = null)
    {
        DpiScale s = scale ?? DpiScale.Fallback;
        Profile? profile = ProfileOps.Find(config, app);
        if (profile is null)
        {
            if (dpi is null) return config;
            var created = new Profile(app, new Dictionary<ushort, ActionSpec>()) { Dpi = s.Snap(dpi.Value) };
            return config with { Profiles = [.. config.Profiles, created] };
        }

        int? value = dpi is { } d ? s.Snap(d) : null;
        return config with { Profiles = config.Profiles.Select(p => ReferenceEquals(p, profile) ? p with { Dpi = value } : p).ToList() };
    }
}
