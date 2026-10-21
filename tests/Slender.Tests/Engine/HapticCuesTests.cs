using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tests.Engine;

public class HapticCuesTests
{
    [Fact]
    public void Defaults_reproduce_todays_behaviour()
    {
        var s = new HapticSettings();
        Assert.False(HapticCues.Default(HapticEvent.ButtonPress, s).Enabled);
        Assert.True(HapticCues.Default(HapticEvent.ButtonPress, s with { OnButtonPress = true }).Enabled);
        Assert.Equal(1, HapticCues.Default(HapticEvent.ButtonPress).Pattern);

        Assert.True(HapticCues.Default(HapticEvent.RingOpen, s).Enabled);
        Assert.False(HapticCues.Default(HapticEvent.RingOpen, s with { OnActionsRing = false }).Enabled);
        Assert.Equal(RingHaptics.HoverWaveform, HapticCues.Default(HapticEvent.RingHover, s).Pattern);
        Assert.Equal(RingHaptics.RunWaveform, HapticCues.Default(HapticEvent.RingRun, s).Pattern);
        Assert.False(HapticCues.Default(HapticEvent.RingHover, s with { OnActionsRing = false }).Enabled);

        var hold = HapticCues.Default(HapticEvent.HoldStart);
        Assert.True(hold.Enabled);
        Assert.Equal(1, hold.Pattern);
        Assert.Equal(1, hold.Pulses);
    }

    [Theory]
    [InlineData(HapticEvent.TopHoldEngage)]
    [InlineData(HapticEvent.TouchRest)]
    [InlineData(HapticEvent.TouchTap)]
    [InlineData(HapticEvent.TouchDoubleTap)]
    [InlineData(HapticEvent.WheelModeChange)]
    public void New_events_are_off_by_default(HapticEvent e) => Assert.False(HapticCues.Default(e).Enabled);

    [Fact]
    public void Battery_low_is_off_and_alert_is_on_with_their_patterns()
    {
        var battery = HapticCues.Default(HapticEvent.BatteryLow);
        Assert.False(battery.Enabled);
        Assert.Equal(6, battery.Pattern);
        var alert = HapticCues.Default(HapticEvent.Alert);
        Assert.True(alert.Enabled);
        Assert.Equal(5, alert.Pattern);
    }

    [Fact]
    public void Every_event_has_a_default_inside_the_limits_and_a_label()
    {
        foreach (var e in Enum.GetValues<HapticEvent>())
        {
            var cue = HapticCues.Default(e);
            Assert.Equal(cue, cue.Clamped());
            Assert.False(string.IsNullOrWhiteSpace(HapticCues.EventLabel(e)));
        }
    }

    [Fact]
    public void Effective_prefers_the_override_and_falls_back_to_the_default()
    {
        var s = new HapticSettings { Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.TouchTap] = new HapticCue(true, 7, 2, 90) } };
        Assert.Equal(new HapticCue(true, 7, 2, 90), HapticCues.Effective(s, HapticEvent.TouchTap));
        Assert.Equal(HapticCues.Default(HapticEvent.HoldStart), HapticCues.Effective(s, HapticEvent.HoldStart));
        Assert.Null(HapticCues.Effective(s, HapticEvent.TouchRest)); // default off
    }

    [Fact]
    public void Effective_is_nothing_when_haptics_or_the_cue_are_off()
    {
        var off = new HapticSettings(Enabled: false);
        Assert.All(Enum.GetValues<HapticEvent>(), e => Assert.Null(HapticCues.Effective(off, e)));
        var s = new HapticSettings { Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.HoldStart] = new HapticCue(false) } };
        Assert.Null(HapticCues.Effective(s, HapticEvent.HoldStart));
    }

    [Fact]
    public void Effective_clamps_a_hand_edited_cue()
    {
        var s = new HapticSettings { Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.HoldStart] = new HapticCue(true, 99, 50, 1, 500) } };
        var cue = HapticCues.Effective(s, HapticEvent.HoldStart)!;
        Assert.Equal(new HapticCue(true, 14, 6, 40, 100), cue);
    }

    [Fact]
    public void Alerts_use_their_own_cue_else_the_alert_event()
    {
        var s = new HapticSettings { Alerts = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["build"] = new HapticCue(true, 7, 3, 100) } };
        Assert.Equal(new HapticCue(true, 7, 3, 100), HapticCues.EffectiveAlert(s, "BUILD"));
        Assert.Equal(HapticCues.Default(HapticEvent.Alert), HapticCues.EffectiveAlert(s, "nope"));
        Assert.Equal(HapticCues.Default(HapticEvent.Alert), HapticCues.EffectiveAlert(s, null));
        Assert.Null(HapticCues.EffectiveAlert(s with { Enabled = false }, "build"));
    }

    [Fact]
    public void Pattern_names_cover_all_fifteen_waveforms()
    {
        Assert.Equal(HapticCue.PatternMax + 1, HapticCues.PatternNames.Count);
        Assert.Equal("Happy alert", HapticCues.PatternName(5));
        Assert.Equal("Ringing", HapticCues.PatternName(99));
    }

    private static HapticSettings WithRinger() => new()
    {
        Patterns = new Dictionary<string, HapticPattern>(StringComparer.OrdinalIgnoreCase) { ["Ringer"] = new HapticPattern(12, 6, 80, 3, 1500) },
    };

    [Fact]
    public void A_pattern_reference_expands_but_keeps_enabled_and_strength()
    {
        var s = WithRinger();
        var cue = new HapticCue(false, 2, 1, 150, 33, Custom: "ringer");
        var expanded = HapticCues.Expand(s, cue);
        Assert.Equal(new HapticCue(false, 12, 6, 80, 33, 3, 1500), expanded);
        Assert.Null(expanded.Custom);
    }

    [Fact]
    public void A_missing_pattern_falls_back_to_the_cues_own_fields()
    {
        var cue = new HapticCue(true, 2, 3, 90, Custom: "gone");
        Assert.Equal(cue, HapticCues.Expand(WithRinger(), cue));
        Assert.Equal(cue, HapticCues.Expand(new HapticSettings(), cue));
    }

    [Fact]
    public void Resolve_expands_while_Configured_keeps_the_reference()
    {
        var s = WithRinger() with { Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.TouchTap] = new HapticCue(true, 1, Custom: "Ringer") } };
        Assert.Equal("Ringer", HapticCues.Configured(s, HapticEvent.TouchTap).Custom);
        Assert.Equal(new HapticCue(true, 12, 6, 80, null, 3, 1500), HapticCues.Resolve(s, HapticEvent.TouchTap));
    }

    [Fact]
    public void ForTest_sends_a_finished_cue()
    {
        var test = HapticCues.ForTest(WithRinger(), new HapticCue(false, 1, Custom: "ringer"));
        Assert.Equal(new HapticCue(true, 12, 6, 80, HapticCues.NormalStrength, 3, 1500), test);
        Assert.Equal(100, HapticCues.ForTest(new HapticSettings(), new HapticCue(true, 1, Strength: 100)).Strength);
    }

    [Fact]
    public void Levels_map_to_real_strengths_and_unknown_numbers_are_custom()
    {
        Assert.Equal([33, 66, 100], new[] { HapticCues.StrengthOf(0), HapticCues.StrengthOf(1), HapticCues.StrengthOf(2) });
        Assert.Equal(0, HapticCues.LevelOf(33));
        Assert.Equal(1, HapticCues.LevelOf(66));
        Assert.Equal(2, HapticCues.LevelOf(100));
        Assert.Equal(1, HapticCues.LevelOf(null));
        Assert.Equal(-1, HapticCues.LevelOf(50));
    }

    [Theory]
    [InlineData("Ringer", true)]
    [InlineData("my pattern_2-b", true)]
    [InlineData("", false)]
    [InlineData(" lead", false)]
    [InlineData("trail ", false)]
    [InlineData("bad!", false)]
    [InlineData("knock", false)] // a built-in waveform name, any case
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)] // 33 characters
    public void Pattern_names_follow_the_alert_rules_plus_spaces(string name, bool ok) => Assert.Equal(ok, HapticCues.IsPatternName(name));
}
