using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Where an app's thumb wheel second action (held on the top button) comes from.</summary>
public enum ThumbWheelSource
{
    /// <summary>Nothing is set: the app has no second role.</summary>
    None,

    /// <summary>The app's own profile sets it.</summary>
    Own,

    /// <summary>The app follows All apps' setting.</summary>
    AllApps,
}

/// <summary>The second role an app uses (null when none) and where it comes from. On means set and enabled.</summary>
public sealed record ThumbWheelView(ThumbWheelSecond? Second, ThumbWheelSource Source)
{
    public bool On => Second is { Enabled: true };

    public bool Inherited => Source is ThumbWheelSource.AllApps;
}

/// <summary>
/// Pure edits of Profile.ThumbWheelAlt, the thumb wheel's second action (run while the top button is held). A null
/// ThumbWheelAlt follows All apps. Every edit returns a new config and creates the app's profile when it has none.
/// </summary>
public static class ThumbWheelOps
{
    private static readonly ThumbWheelSecond Blank = new(true, new ActionSpec.None(), new ActionSpec.None());

    public static ThumbWheelView View(SlenderConfig config, string? app)
    {
        if (ProfileOps.Find(config, app)?.ThumbWheelAlt is { } own)
        {
            return new ThumbWheelView(own, ThumbWheelSource.Own);
        }

        if (app is null)
        {
            return new ThumbWheelView(null, ThumbWheelSource.None);
        }

        return ProfileOps.Find(config, null)?.ThumbWheelAlt is { } all
            ? new ThumbWheelView(all, ThumbWheelSource.AllApps)
            : new ThumbWheelView(null, ThumbWheelSource.None);
    }

    /// <summary>Muted text for an inherited setting, null when the app has its own or nothing.</summary>
    public static string? InheritedText(ThumbWheelView view) => view.Source switch
    {
        ThumbWheelSource.AllApps => "Same as All apps",
        _ => null,
    };

    /// <summary>Turns it on. An inherited role is kept as it is; an app with nothing starts with both notches unassigned.</summary>
    public static SlenderConfig SetEnabled(SlenderConfig config, string? app, bool enabled)
    {
        ThumbWheelView view = View(config, app);
        if (enabled && view.On)
        {
            return config;
        }

        return Store(config, app, (view.Second ?? Blank) with { Enabled = enabled });
    }

    /// <summary>"Turn off for app": stores Enabled = false in the app's own profile, so it ignores All apps.</summary>
    public static SlenderConfig TurnOffFor(SlenderConfig config, string? app) => SetEnabled(config, app, false);

    public static SlenderConfig SetForward(SlenderConfig config, string? app, ActionSpec action) =>
        Store(config, app, (View(config, app).Second ?? Blank) with { Enabled = true, Forward = action });

    public static SlenderConfig SetBack(SlenderConfig config, string? app, ActionSpec action) =>
        Store(config, app, (View(config, app).Second ?? Blank) with { Enabled = true, Back = action });

    /// <summary>Copies the inherited role into the app's own profile so it can be changed. No-op when the app already has its own.</summary>
    public static SlenderConfig Customise(SlenderConfig config, string? app)
    {
        ThumbWheelView view = View(config, app);
        return view.Source == ThumbWheelSource.Own ? config : Store(config, app, view.Second ?? Blank);
    }

    /// <summary>Drops the app's own setting so it follows All apps again. No-op for All apps.</summary>
    public static SlenderConfig FollowInherited(SlenderConfig config, string? app)
    {
        if (app is null || ProfileOps.Find(config, app) is not { ThumbWheelAlt: not null } profile)
        {
            return config;
        }

        return Replace(config, profile, profile with { ThumbWheelAlt = null });
    }

    public static string Text(ActionSpec action) => action is ActionSpec.None ? "Not assigned" : ActionCatalog.Plain(action);

    /// <summary>Writes <paramref name="second"/> as the app's own second action, creating its profile when it has none.</summary>
    public static SlenderConfig Store(SlenderConfig config, string? app, ThumbWheelSecond second)
    {
        Profile? profile = ProfileOps.Find(config, app);
        if (profile is null)
        {
            var created = new Profile(app, new Dictionary<ushort, ActionSpec>()) { DisplayName = app is null ? "All apps" : null, ThumbWheelAlt = second };
            var profiles = config.Profiles.ToList();
            profiles.Insert(app is null ? 0 : profiles.Count, created);
            return config with { Profiles = profiles };
        }

        return Replace(config, profile, profile with { ThumbWheelAlt = second });
    }

    private static SlenderConfig Replace(SlenderConfig config, Profile old, Profile updated) =>
        config with { Profiles = config.Profiles.Select(p => ReferenceEquals(p, old) ? updated : p).ToList() };
}
