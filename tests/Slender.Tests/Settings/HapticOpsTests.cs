using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class HapticOpsTests
{
    [Fact]
    public void Level_choices_are_real_strengths()
    {
        var cue = new HapticCue();
        Assert.Equal(1, HapticOps.LevelIndex(cue)); // no strength plays at Normal
        Assert.Equal(0, HapticOps.LevelIndex(cue with { Strength = 33 }));
        Assert.Equal(2, HapticOps.LevelIndex(cue with { Strength = 100 }));
        Assert.Equal(-1, HapticOps.LevelIndex(cue with { Strength = 50 }));
        Assert.Equal(33, HapticOps.WithLevel(cue, 0).Strength);
        Assert.Equal(66, HapticOps.WithLevel(cue, 1).Strength);
        Assert.Equal(100, HapticOps.WithLevel(cue, 2).Strength);
        Assert.Equal(["Light", "Normal", "Strong"], HapticOps.LevelChoices(cue));
        Assert.Equal(["Light", "Normal", "Strong", "Custom (50)"], HapticOps.LevelChoices(cue with { Strength = 50 }));
        Assert.Equal(1, HapticOps.WithLevel(cue, 2).Pulses); // the level no longer touches the pulses
    }

    [Fact]
    public void A_custom_strength_is_kept_until_a_level_is_picked()
    {
        var s = HapticOps.WithCue(new HapticSettings(), HapticEvent.TouchTap, new HapticCue(true, 1, Strength: 50));
        Assert.Equal(50, s.Cues[HapticEvent.TouchTap].Strength);
        var picked = HapticOps.WithCue(s, HapticEvent.TouchTap, HapticOps.WithLevel(s.Cues[HapticEvent.TouchTap], 1));
        Assert.Equal(66, picked.Cues[HapticEvent.TouchTap].Strength);
    }

    [Fact]
    public void Cycles_are_the_pulse_count_with_an_extra_entry_for_more_than_three()
    {
        var cue = new HapticCue();
        Assert.Equal(0, HapticOps.CyclesIndex(cue));
        Assert.Equal(2, HapticOps.CyclesIndex(cue with { Pulses = 3 }));
        Assert.Equal(3, HapticOps.CyclesIndex(cue with { Pulses = 5 }));
        Assert.Equal(["1x", "2x", "3x"], HapticOps.CyclesChoicesFor(cue));
        Assert.Equal(["1x", "2x", "3x", "5x"], HapticOps.CyclesChoicesFor(cue with { Pulses = 5 }));
        Assert.Equal(3, HapticOps.WithCycles(cue, 2).Pulses);
        Assert.Equal(2, HapticOps.WithCycles(cue, 1).Pulses);
        Assert.Equal(150, HapticOps.WithCycles(cue, 1).SpacingMs);
    }

    private static HapticSettings WithPatterns(params string[] names) =>
        names.Aggregate(new HapticSettings(), (s, n) => HapticOps.WithPattern(s, n, new HapticPattern(12, 6, 80, 3, 1500)));

    [Fact]
    public void Patterns_can_be_added_overwritten_and_listed_after_the_built_ins()
    {
        var s = WithPatterns("Ringer", "alarm");
        s = HapticOps.WithPattern(s, "RINGER", new HapticPattern(2, 2, 100, 1, 1000));
        Assert.Equal(2, s.Patterns.Count);
        Assert.Equal("RINGER", s.Patterns.Keys.Single(k => k.Equals("ringer", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, s.Patterns["ringer"].Pattern);

        var choices = HapticOps.PatternChoices(s);
        Assert.Equal(HapticCues.PatternNames.Count + 2, choices.Count);
        Assert.Equal(["alarm *", "RINGER *"], choices.Skip(HapticCues.PatternNames.Count));
    }

    [Fact]
    public void Picking_a_pattern_sets_the_reference_and_a_built_in_clears_it()
    {
        var s = WithPatterns("Ringer");
        var cue = new HapticCue(true, 4);
        var bound = HapticOps.WithPatternChoice(s, cue, HapticCues.PatternNames.Count);
        Assert.Equal("Ringer", bound.Custom);
        Assert.Equal(HapticCues.PatternNames.Count, HapticOps.PatternChoiceIndex(s, bound));
        Assert.True(HapticOps.UsesPattern(s, bound));
        var back = HapticOps.WithPatternChoice(s, bound, 7);
        Assert.Null(back.Custom);
        Assert.Equal(7, back.Pattern);
        Assert.Equal(4, HapticOps.PatternChoiceIndex(s, cue));
        Assert.False(HapticOps.UsesPattern(s, cue with { Custom = "gone" }));
    }

    [Fact]
    public void Pattern_names_are_validated()
    {
        var s = WithPatterns("Ringer");
        Assert.Null(HapticOps.PatternNameProblem(s, "my pattern"));
        Assert.Null(HapticOps.PatternNameProblem(s, "RINGER")); // saving over a name replaces it
        Assert.NotNull(HapticOps.PatternNameProblem(s, ""));
        Assert.NotNull(HapticOps.PatternNameProblem(s, "bad!"));
        Assert.NotNull(HapticOps.PatternNameProblem(s, "knock"));
        Assert.NotNull(HapticOps.PatternNameProblem(s, new string('a', 33)));
    }

    [Fact]
    public void Deleting_a_pattern_keeps_what_every_cue_played()
    {
        var s = WithPatterns("Ringer") with
        {
            Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.TouchTap] = new HapticCue(false, 1, Strength: 100, Custom: "ringer") },
            Alerts = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["call"] = new HapticCue(true, 1, Custom: "Ringer") },
            Notify = new NotifyHapticSettings
            {
                Default = new HapticCue(true, 5, Custom: "Ringer"),
                Apps = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["Teams"] = new HapticCue(true, 3, 2, 90, 33, Custom: "Ringer"), ["Mail"] = new HapticCue(true, 3) },
            },
        };
        var before = HapticCues.Expand(s, s.Cues[HapticEvent.TouchTap]);

        var gone = HapticOps.WithoutPattern(s, "RINGER");

        Assert.Empty(gone.Patterns);
        Assert.Equal(before, gone.Cues[HapticEvent.TouchTap]);
        Assert.Null(gone.Cues[HapticEvent.TouchTap].Custom);
        Assert.False(gone.Cues[HapticEvent.TouchTap].Enabled);
        Assert.Equal(new HapticCue(true, 12, 6, 80, null, 3, 1500), gone.Alerts["call"]);
        Assert.Equal(new HapticCue(true, 12, 6, 80, null, 3, 1500), gone.Notify.Default);
        Assert.Equal(new HapticCue(true, 12, 6, 80, 33, 3, 1500), gone.Notify.Apps["teams"]);
        Assert.Equal(new HapticCue(true, 3), gone.Notify.Apps["Mail"]);
        Assert.Same(gone, HapticOps.WithoutPattern(gone, "ringer")); // nothing to delete
    }

    [Fact]
    public void Pattern_summary_reads_like_the_list_row()
    {
        Assert.Equal("6 x Knock, 3 repeats", new HapticPattern(12, 6, 80, 3, 1500).Summary);
        Assert.Equal("1 x Damp state change", new HapticPattern(1).Summary);
    }

    [Fact]
    public void A_cue_equal_to_the_default_drops_its_override()
    {
        var s = new HapticSettings();
        var changed = HapticOps.WithCue(s, HapticEvent.TouchTap, new HapticCue(true, 1));
        Assert.True(changed.Cues[HapticEvent.TouchTap].Enabled);
        var back = HapticOps.WithCue(changed, HapticEvent.TouchTap, HapticCues.Default(HapticEvent.TouchTap));
        Assert.Empty(back.Cues);
    }

    [Fact]
    public void Editing_a_legacy_driven_cue_keeps_the_legacy_default_shown()
    {
        var s = new HapticSettings(OnButtonPress: true);
        Assert.True(HapticCues.Resolve(s, HapticEvent.ButtonPress).Enabled);
        var off = HapticOps.WithCue(s, HapticEvent.ButtonPress, HapticCues.Resolve(s, HapticEvent.ButtonPress) with { Enabled = false });
        Assert.Null(HapticCues.Effective(off, HapticEvent.ButtonPress));
    }

    [Fact]
    public void Alert_names_are_validated_and_unique_ignoring_case()
    {
        var s = HapticOps.WithNewAlert(new HapticSettings(), "Build");
        Assert.Null(HapticOps.AlertNameProblem(new HapticSettings(), "build"));
        Assert.NotNull(HapticOps.AlertNameProblem(s, "BUILD"));
        Assert.NotNull(HapticOps.AlertNameProblem(s, ""));
        Assert.NotNull(HapticOps.AlertNameProblem(s, "two words"));
        Assert.True(s.Alerts["build"].Enabled);
    }

    [Fact]
    public void Alerts_can_be_edited_and_deleted()
    {
        var s = HapticOps.WithNewAlert(new HapticSettings(), "build");
        s = HapticOps.WithAlertCue(s, "BUILD", new HapticCue(true, 7, 2, 90));
        Assert.Equal(new HapticCue(true, 7, 2, 90), s.Alerts["build"]);
        s = HapticOps.WithoutAlert(s, "Build");
        Assert.Empty(s.Alerts);
    }

    [Fact]
    public void The_command_shows_the_name()
    {
        Assert.Equal("Slender.Buzz.exe build", HapticOps.Command("build"));
        Assert.Equal("Slender.Buzz.exe", HapticOps.Command(null));
    }
}
