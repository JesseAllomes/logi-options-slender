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
}
