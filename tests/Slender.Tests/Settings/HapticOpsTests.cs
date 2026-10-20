using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class HapticOpsTests
{
    [Fact]
    public void Strength_choices_are_one_two_three_pulses()
    {
        var cue = new HapticCue();
        Assert.Equal(0, HapticOps.IntensityIndex(cue with { Pulses = 1 }));
        Assert.Equal(2, HapticOps.IntensityIndex(cue with { Pulses = 3 }));
        Assert.Equal(-1, HapticOps.IntensityIndex(cue with { Pulses = 5 }));
        Assert.Equal(3, HapticOps.WithIntensity(cue, 2).Pulses);
        Assert.Equal(1, HapticOps.WithIntensity(cue, 0).Pulses);
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
