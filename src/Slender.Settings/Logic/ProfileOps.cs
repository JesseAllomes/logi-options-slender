using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Pure edits of SlenderConfig profiles. The default profile ("All apps") has AppProcess == null.</summary>
public static class ProfileOps
{
    public static IReadOnlyDictionary<ushort, ActionSpec> Map(Profile profile, DeviceKind kind) =>
        kind == DeviceKind.Mouse ? profile.Bindings : profile.KeyBindings;

    public static Profile? Find(SlenderConfig config, string? appProcess) =>
        config.Profiles.FirstOrDefault(p => string.Equals(p.AppProcess, appProcess, StringComparison.OrdinalIgnoreCase));

    /// <summary>The binding this profile sets itself, or null.</summary>
    public static ActionSpec? Own(SlenderConfig config, string? appProcess, DeviceKind kind, ushort id)
    {
        Profile? p = Find(config, appProcess);
        return p is not null && Map(p, kind).TryGetValue(id, out ActionSpec? a) && a is not ActionSpec.None ? a : null;
    }

    /// <summary>
    /// True when an app profile holds an explicit None for the control: the button's own function applies there and the
    /// All apps binding is ignored. (The settings UI never writes it; it comes from an Options+ import.)
    /// </summary>
    public static bool IsPassThrough(SlenderConfig config, string? appProcess, DeviceKind kind, ushort id)
    {
        if (appProcess is null)
        {
            return false;
        }

        Profile? p = Find(config, appProcess);
        return p is not null && Map(p, kind).TryGetValue(id, out ActionSpec? a) && a is ActionSpec.None;
    }

    /// <summary>What the control does in this app. Inherited is true when the app has no binding of its own. Explicit pass-through gives no action.</summary>
    public static (ActionSpec? Action, bool Inherited) Effective(SlenderConfig config, string? appProcess, DeviceKind kind, ushort id)
    {
        if (IsPassThrough(config, appProcess, kind, id))
        {
            return (null, false);
        }

        ActionSpec? own = Own(config, appProcess, kind, id);
        if (own is not null)
        {
            return (own, false);
        }

        if (appProcess is null)
        {
            return (null, false);
        }

        return (Own(config, null, kind, id), true);
    }

    /// <summary>Number of controls this app changes from All apps. For All apps, the number of controls it assigns.</summary>
    public static int ChangedCount(SlenderConfig config, string? appProcess, DeviceKind kind)
    {
        Profile? p = Find(config, appProcess);
        if (p is null)
        {
            return 0;
        }

        HashSet<ushort> known = ControlCatalog.For(kind).Select(c => c.Id).ToHashSet();
        // In an app profile an explicit None is a change from All apps (pass through); in All apps it is just unassigned.
        return Map(p, kind).Count(kv => known.Contains(kv.Key) && (kv.Value is not ActionSpec.None || appProcess is not null));
    }

    /// <summary>Sets (or, with a null action, clears) one binding. Creates the profile if it does not exist.</summary>
    public static SlenderConfig SetBinding(SlenderConfig config, string? appProcess, DeviceKind kind, ushort id, ActionSpec? action)
    {
        var profiles = config.Profiles.ToList();
        int index = profiles.FindIndex(p => string.Equals(p.AppProcess, appProcess, StringComparison.OrdinalIgnoreCase));
        Profile profile;
        if (index < 0)
        {
            profile = new Profile(appProcess, new Dictionary<ushort, ActionSpec>()) { DisplayName = appProcess is null ? "All apps" : null };
            profiles.Insert(appProcess is null ? 0 : profiles.Count, profile);
            index = profiles.IndexOf(profile);
        }
        else
        {
            profile = profiles[index];
        }

        var map = new Dictionary<ushort, ActionSpec>(Map(profile, kind));
        if (action is null)
        {
            map.Remove(id);
        }
        else
        {
            map[id] = action;
        }

        profiles[index] = kind == DeviceKind.Mouse ? profile with { Bindings = map } : profile with { KeyBindings = map };
        return config with { Profiles = profiles };
    }

    /// <summary>Adds an app profile; no-op when the process already has one.</summary>
    public static SlenderConfig AddApp(SlenderConfig config, string process, string displayName, string? exePath)
    {
        string key = process.Trim().ToLowerInvariant();
        if (key.Length == 0 || Find(config, key) is not null)
        {
            return config;
        }

        var profile = new Profile(key, new Dictionary<ushort, ActionSpec>()) { DisplayName = displayName, ExePath = exePath };
        return config with { Profiles = [.. config.Profiles, profile] };
    }

    public static SlenderConfig RemoveApp(SlenderConfig config, string process) =>
        config with { Profiles = config.Profiles.Where(p => p.AppProcess is null || !string.Equals(p.AppProcess, process, StringComparison.OrdinalIgnoreCase)).ToList() };

    /// <summary>Name for the app strip: DisplayName, else the process name with a capital.</summary>
    public static string AppName(Profile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return profile.DisplayName;
        }

        string p = profile.AppProcess ?? "All apps";
        return p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..];
    }

    /// <summary>"4 buttons changed · others follow All apps" (or the keyboard/All apps variants).</summary>
    public static string Subtitle(int changed, DeviceKind kind, bool isAllApps)
    {
        string noun = kind == DeviceKind.Mouse ? "button" : "key";
        if (isAllApps)
        {
            return kind == DeviceKind.Mouse ? "Default for every app" : "Default for every app";
        }

        string count = changed == 1 ? $"1 {noun} changed" : $"{changed} {noun}s changed";
        return count + " · others follow All apps";
    }
}
