using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Tests.Ringer;

public sealed class RingerConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private JsonConfigStore Store() => new(_dir);
    private string FilePath => Path.Combine(_dir, "config.json");

    [Fact]
    public void The_default_ringer_is_a_strong_ringing_buzz_that_repeats_through_the_session()
    {
        var cue = new HapticSettings().Ringer;
        Assert.Equal(HapticCues.DefaultRinger, cue);
        Assert.True(cue.Enabled);
        Assert.Equal(HapticCues.StrongStrength, cue.Strength);
        Assert.Equal(14, cue.Pattern); // "Ringing"
        Assert.Equal(cue, cue.Clamped());
        Assert.Null(new HapticSettings().RingerOrigin);
    }

    [Fact]
    public void A_changed_ringer_and_the_address_round_trip()
    {
        var haptics = new HapticSettings
        {
            Ringer = new HapticCue(true, 12, 3, 120, 33, 1, 2000),
            RingerOrigin = "https://pbx.example.com:5001",
        };
        Store().Save(SlenderConfig.Default with { Haptics = haptics });
        HapticSettings loaded = Store().Load().Haptics;

        Assert.Equal(new HapticCue(true, 12, 3, 120, 33, 1, 2000), loaded.Ringer);
        Assert.Equal("https://pbx.example.com:5001", loaded.RingerOrigin);
        Assert.Equal(haptics, loaded);
    }

    [Fact]
    public void A_ringer_that_uses_a_saved_pattern_round_trips()
    {
        var haptics = new HapticSettings
        {
            Patterns = new Dictionary<string, HapticPattern>(StringComparer.OrdinalIgnoreCase) { ["Desk phone"] = new HapticPattern(14, 2, 200, 2, 1500) },
            Ringer = new HapticCue(true, 14, Strength: 100, Custom: "Desk phone"),
        };
        Store().Save(SlenderConfig.Default with { Haptics = haptics });
        Assert.Equal(haptics, Store().Load().Haptics);
    }

    [Fact]
    public void An_unchanged_ringer_and_no_address_add_nothing_to_the_file()
    {
        Store().Save(SlenderConfig.Default);
        string text = File.ReadAllText(FilePath);
        Assert.DoesNotContain("\"ringer\": {", text);
        Assert.Contains("\"ringer\": null", text);
        Assert.Contains("\"ringerOrigin\": null", text);
        Assert.Equal(HapticCues.DefaultRinger, Store().Load().Haptics.Ringer);
    }

    [Fact]
    public void An_old_config_file_loads_unchanged_with_the_default_ringer()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"version":1,"profiles":[],"excel":{},"haptics":{"enabled":false,"level":1,"cues":{"touchTap":{"pattern":8,"pulses":2,"spacingMs":90,"strength":40}}}}""");

        HapticSettings loaded = Store().Load().Haptics;

        Assert.False(loaded.Enabled);
        Assert.Equal(new HapticCue(true, 8, 2, 90, 40), loaded.Cues[HapticEvent.TouchTap]);
        Assert.Equal(HapticCues.DefaultRinger, loaded.Ringer);
        Assert.Null(loaded.RingerOrigin);
    }

    [Fact]
    public void A_bad_address_in_the_file_is_dropped_and_a_wild_ringer_is_clamped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"version":1,"profiles":[],"excel":{},"haptics":{"ringerOrigin":"http://pbx.example.com/x","ringer":{"pattern":99,"pulses":50,"spacingMs":1,"repeatGapMs":1}}}""");

        HapticSettings loaded = Store().Load().Haptics;

        Assert.Null(loaded.RingerOrigin);
        Assert.Equal(new HapticCue(true, 14, 6, 40, null, 1, 200), loaded.Ringer);
    }

    [Fact]
    public void Ring_buttons_default_to_off_and_round_trip()
    {
        Assert.Equal(0, Store().Load().Haptics.RingAnswerButton);
        Store().Save(SlenderConfig.Default);
        Assert.DoesNotContain("RingAnswerButton", File.ReadAllText(FilePath)); // off is not written

        Store().Save(SlenderConfig.Default with { Haptics = new HapticSettings { RingAnswerButton = Slender.Hidpp.ControlId.Forward, RingRejectButton = Slender.Hidpp.ControlId.Back } });
        var loaded = Store().Load().Haptics;
        Assert.Equal(Slender.Hidpp.ControlId.Forward, loaded.RingAnswerButton);
        Assert.Equal(Slender.Hidpp.ControlId.Back, loaded.RingRejectButton);
    }

    [Fact]
    public void A_ring_button_that_is_not_a_choice_loads_as_off()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"version":2,"haptics":{"ringAnswerButton":195,"ringRejectButton":99999}}""");
        var loaded = Store().Load().Haptics;
        Assert.Equal(0, loaded.RingAnswerButton);
        Assert.Equal(0, loaded.RingRejectButton);
    }
}
