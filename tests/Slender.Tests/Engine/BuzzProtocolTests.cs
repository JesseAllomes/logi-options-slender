using Slender.Engine;
using Slender.Engine.Config;
using Slender.Tray;

namespace Slender.Tests.Engine;

public class BuzzProtocolTests
{
    [Fact]
    public void An_empty_line_is_the_no_name_alert()
    {
        Assert.Equal(new BuzzRequest(null, null), BuzzProtocol.Parse(""));
        Assert.Equal(new BuzzRequest(null, null), BuzzProtocol.Parse("  \r"));
        Assert.Equal(new BuzzRequest(null, null), BuzzProtocol.Parse(null));
    }

    [Fact]
    public void A_test_line_carries_repeats_only_when_the_cue_repeats()
    {
        var once = new HapticCue(true, 5, 6, 80);
        var ringer = once with { Repeats = 3, RepeatGapMs = 1000 };
        Assert.Equal("!test 5 6 80 -", BuzzProtocol.TestLine(once));
        Assert.Equal("!test 5 6 80 - 3 1000", BuzzProtocol.TestLine(ringer));
        Assert.Equal(ringer, BuzzProtocol.Parse(BuzzProtocol.TestLine(ringer))!.TestCue);
        Assert.Null(BuzzProtocol.Parse("!test 5 6 80 - 3")); // repeats without its pause
    }

    [Fact]
    public void A_resolved_test_cue_carries_strength_and_repeats_to_the_tray()
    {
        var s = new HapticSettings { Patterns = new Dictionary<string, HapticPattern>(StringComparer.OrdinalIgnoreCase) { ["Ringer"] = new HapticPattern(12, 6, 80, 3, 1500) } };
        var cue = HapticCues.ForTest(s, new HapticCue(true, 1, Strength: 33, Custom: "ringer"));
        Assert.Equal("!test 12 6 80 33 3 1500", BuzzProtocol.TestLine(cue));
        Assert.Equal(cue, BuzzProtocol.Parse(BuzzProtocol.TestLine(cue))!.TestCue);
    }

    [Fact]
    public void A_name_is_trimmed()
    {
        Assert.Equal(new BuzzRequest("claude", null), BuzzProtocol.Parse(" claude \r"));
    }

    [Fact]
    public void Lines_over_64_characters_are_ignored()
    {
        Assert.NotNull(BuzzProtocol.Parse(new string('a', 64)));
        Assert.Null(BuzzProtocol.Parse(new string('a', 65)));
    }

    [Fact]
    public void A_test_line_carries_a_clamped_cue()
    {
        var cue = new HapticCue(true, 9, 3, 250, 40);
        Assert.Equal(cue, BuzzProtocol.Parse(BuzzProtocol.TestLine(cue))!.TestCue);
        Assert.Equal(new HapticCue(true, 7, 2, 150, null), BuzzProtocol.Parse(BuzzProtocol.TestLine(new HapticCue(true, 7, 2, 150)))!.TestCue);
        Assert.Equal(new HapticCue(true, 14, 6, 1000, 100), BuzzProtocol.Parse("!test 99 99 99999 999")!.TestCue);
    }

    [Theory]
    [InlineData("!test")]
    [InlineData("!test 1 2 3")]
    [InlineData("!test a b c d")]
    [InlineData("!test 1 -2 100 -")]
    [InlineData("!other")]
    public void Malformed_bang_lines_are_ignored(string line) => Assert.Null(BuzzProtocol.Parse(line));

    [Fact]
    public void The_limiter_allows_five_per_second_and_drops_the_rest()
    {
        long now = 0;
        var limiter = new BuzzRateLimiter(5, () => now);
        Assert.Equal(5, Enumerable.Range(0, 10).Count(_ => limiter.TryAcquire()));
        now = 999;
        Assert.False(limiter.TryAcquire());
        now = 1000;
        Assert.True(limiter.TryAcquire()); // the first stamp has aged out
    }

    [Fact]
    public void The_server_plays_names_and_tests_and_drops_extras()
    {
        var mouse = new FakeMouse();
        var cfg = SlenderConfig.Default with
        {
            Haptics = new HapticSettings { Alerts = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["build"] = new HapticCue(true, 7, 1, 100) } },
        };
        using var engine = new SlenderEngine(mouse, new FakeHook(), new FakeForeground(), new FakeExcel(), new FakeExecutor(), cfg, new ListLog(), new InlineQueue(), () => 0, new FakeHoldTimer());
        using var server = new BuzzServer(engine);
        server.Handle("build");
        server.Handle(new string('x', 65)); // ignored, and does not use up the budget
        server.Handle("!test 12 1 100 -");
        server.Handle("");
        Assert.Equal([(byte)7, (byte)12, (byte)5], mouse.Haptics);
        for (int i = 0; i < 20; i++) server.Handle("build");
        Assert.Equal(5, mouse.Haptics.Count); // five per second in all
    }

    [Theory]
    [InlineData("build", true)]
    [InlineData("Claude_2-x", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a.b", false)]
    [InlineData("1234567890123456789012345678901234", false)]
    public void Alert_name_validation(string name, bool valid) => Assert.Equal(valid, JsonConfigStore.IsAlertName(name));
}
