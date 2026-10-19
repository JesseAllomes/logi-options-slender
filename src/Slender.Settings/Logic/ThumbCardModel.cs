using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>What the card's result preview draws under a wheel.</summary>
public enum ThumbPreviewKind
{
    /// <summary>A sheet of lettered columns sliding sideways (the normal side scroll).</summary>
    Sheet,

    /// <summary>A strip of tabs whose highlight moves (tab and sheet pairs).</summary>
    Tabs,

    /// <summary>The chip flashes and counts the steps (anything else).</summary>
    Count,
}

/// <summary>One mode's speed: the value in effect and whether it comes from All apps (or the global default).</summary>
public sealed record ThumbSpeedView(int Value, bool Inherited);

/// <summary>The left and right chip texts for a mode, and the muted caption under each (empty for none).</summary>
public sealed record ThumbChips(string Left, string Right, string LeftCaption, string RightCaption, bool Mono);

/// <summary>
/// Pure logic behind the thumb wheel card: the 7 speed notches, per-app speed edits (Profile.SideScrollSpeed and
/// ThumbWheelSecond.Speed), the chip texts for both modes, and which result preview fits.
/// </summary>
public static class ThumbCardModel
{
    /// <summary>Speed notches, percent, like Haptic feel (user 2026-10-19).</summary>
    public static IReadOnlyList<int> Notches { get; } = [25, 50, 75, 100, 150, 200, 400];

    /// <summary>The notch nearest <paramref name="speed"/> (ties go to the slower one).</summary>
    public static int NotchIndex(int speed)
    {
        int best = 0;
        for (int i = 1; i < Notches.Count; i++)
        {
            if (Math.Abs(Notches[i] - speed) < Math.Abs(Notches[best] - speed))
            {
                best = i;
            }
        }

        return best;
    }

    public static ThumbSpeedView SideSpeed(SlenderConfig config, string? app)
    {
        if (ProfileOps.Find(config, app)?.SideScrollSpeed is { } own)
        {
            return new ThumbSpeedView(own, false);
        }

        return new ThumbSpeedView(ThumbWheelModes.SideScrollSpeedFor(config, app), app is not null);
    }

    /// <summary>Sets the app's side scroll speed (All apps: the default profile's). Creates the app's profile when it has none.</summary>
    public static SlenderConfig SetSideSpeed(SlenderConfig config, string? app, int speed)
    {
        int value = Math.Clamp(speed, PointerSettings.SpeedMin, PointerSettings.SpeedMax);
        Profile? profile = ProfileOps.Find(config, app);
        if (profile is null)
        {
            var created = new Profile(app, new Dictionary<ushort, ActionSpec>()) { DisplayName = app is null ? "All apps" : null, SideScrollSpeed = value };
            var profiles = config.Profiles.ToList();
            profiles.Insert(app is null ? 0 : profiles.Count, created);
            return config with { Profiles = profiles };
        }

        return config with { Profiles = config.Profiles.Select(p => ReferenceEquals(p, profile) ? p with { SideScrollSpeed = value } : p).ToList() };
    }

    public static ThumbSpeedView SecondSpeed(SlenderConfig config, string? app)
    {
        ThumbWheelView view = ThumbWheelOps.View(config, app);
        int? own = view.Second?.Speed;
        return new ThumbSpeedView(own ?? config.Pointer.SecondActionSpeed, view.Inherited || own is null && app is not null);
    }

    /// <summary>Sets the speed of the app's second action, copying an inherited one into the app first (like a notch edit).</summary>
    public static SlenderConfig SetSecondSpeed(SlenderConfig config, string? app, int speed)
    {
        int value = Math.Clamp(speed, PointerSettings.SpeedMin, PointerSettings.SpeedMax);
        SlenderConfig owned = ThumbWheelOps.Customise(config, app);
        ThumbWheelSecond second = ThumbWheelOps.View(owned, app).Second ?? new ThumbWheelSecond(true, new ActionSpec.None(), new ActionSpec.None());
        return ThumbWheelOps.Store(owned, app, second with { Speed = value });
    }

    /// <summary>Chips for a normal turn, from the thumb wheel's binding (null / None = the mouse's own side scroll).</summary>
    public static ThumbChips TurnChips(ActionSpec? binding) => binding switch
    {
        null or ActionSpec.None => new ThumbChips("Scroll left", "Scroll right", string.Empty, string.Empty, false),
        ActionSpec.Disabled => new ThumbChips("Off", "Off", string.Empty, string.Empty, false),
        ActionSpec.Keys keys => KeysChips(keys.Chord),
        _ => new ThumbChips(ActionCatalog.Plain(binding), ActionCatalog.Plain(binding), string.Empty, string.Empty, false),
    };

    /// <summary>Chips for the held turn: Back on the left, Forward on the right.</summary>
    public static ThumbChips HoldChips(ThumbWheelSecond? second)
    {
        if (second is null)
        {
            return new ThumbChips("Nothing", "Nothing", string.Empty, string.Empty, false);
        }

        (string left, string leftCaption, bool leftMono) = Chip(second.Back);
        (string right, string rightCaption, bool rightMono) = Chip(second.Forward);
        return new ThumbChips(left, right, leftCaption, rightCaption, leftMono || rightMono);
    }

    public static ThumbPreviewKind TurnPreview(ActionSpec? binding) => binding switch
    {
        null or ActionSpec.None => ThumbPreviewKind.Sheet,
        ActionSpec.Keys keys when IsTabPair(SlenderEngine.SplitWheelChord(keys.Chord)) => ThumbPreviewKind.Tabs,
        _ => ThumbPreviewKind.Count,
    };

    public static ThumbPreviewKind HoldPreview(ThumbWheelSecond? second)
    {
        if (second is null)
        {
            return ThumbPreviewKind.Count;
        }

        return IsTabPair((StepChord(second.Forward), StepChord(second.Back))) ? ThumbPreviewKind.Tabs : ThumbPreviewKind.Count;
    }

    private static ThumbChips KeysChips(string chord)
    {
        (string right, string left) = SlenderEngine.SplitWheelChord(chord);
        return new ThumbChips(left, right, Caption(left), Caption(right), true);
    }

    private static (string Text, string Caption, bool Mono) Chip(ActionSpec action) => action switch
    {
        ActionSpec.None => ("Nothing", string.Empty, false),
        ActionSpec.Keys keys => (keys.Chord, Caption(keys.Chord), true),
        _ => (ActionCatalog.Plain(action), string.Empty, false),
    };

    private static string Caption(string chord)
    {
        string name = ActionCatalog.Plain(new ActionSpec.Keys(chord));
        return string.Equals(name, chord, StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
    }

    /// <summary>The chord one step sends, for the tab test (Excel sheet commands become their keys).</summary>
    private static string StepChord(ActionSpec action) => action switch
    {
        ActionSpec.Keys keys => keys.Chord,
        ActionSpec.Excel { Command: ExcelCommand.NextSheet } => "Ctrl+PageDown",
        ActionSpec.Excel { Command: ExcelCommand.PreviousSheet } => "Ctrl+PageUp",
        _ => string.Empty,
    };

    private static readonly HashSet<string> TabChords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+PageDown", "Ctrl+PageUp",
    };

    private static bool IsTabPair((string Right, string Left) pair) => TabChords.Contains(pair.Right) && TabChords.Contains(pair.Left);
}
