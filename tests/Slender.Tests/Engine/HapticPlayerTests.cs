using Slender.Engine;

namespace Slender.Tests.Engine;

public class HapticPlayerTests
{
    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHoldTimer Timer = new();
        public HapticSettings Settings = new(Strength: 60);
        public HapticPlayer Player;

        public Rig() => Player = new HapticPlayer(Mouse, Timer, new InlineQueue(), () => Settings, new ListLog(), CancellationToken.None);
    }

    private sealed class DeferredQueue : IWorkQueue
    {
        public List<Action> Items { get; } = [];
        public void Post(Action work) => Items.Add(work);
        public void Dispose() { }

        public void Drain()
        {
            while (Items.Count > 0)
            {
                var a = Items[0];
                Items.RemoveAt(0);
                a();
            }
        }
    }

    [Fact]
    public void A_single_pulse_plays_at_once_without_a_timer()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 3, 1, 200));
        Assert.Equal([(byte)3], r.Mouse.Haptics);
        Assert.Equal([HapticPlayer.RestoreDelayMs], r.Timer.Delays); // only the idle restore, no pulse timer
        Assert.Equal([HapticCues.NormalStrength], r.Mouse.StrengthWrites); // no strength set plays at Normal
    }

    [Fact]
    public void Pulses_are_spaced_by_the_cue_spacing()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 3, 120));
        Assert.Equal([(byte)5], r.Mouse.Haptics);
        Assert.Equal([120], r.Timer.Delays);
        r.Timer.Elapse();
        Assert.Equal([(byte)5, (byte)5], r.Mouse.Haptics);
        r.Timer.Elapse();
        Assert.Equal([(byte)5, (byte)5, (byte)5], r.Mouse.Haptics);
        Assert.Equal([120, 120, HapticPlayer.RestoreDelayMs], r.Timer.Delays);
        r.Timer.Elapse();
        Assert.Equal(3, r.Mouse.Haptics.Count); // nothing left but the restore
    }

    [Fact]
    public void A_repeated_group_pauses_between_groups_like_a_ringer()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 3, 100, Repeats: 2, RepeatGapMs: 1000));
        for (int i = 0; i < 10; i++) r.Timer.Elapse();
        Assert.Equal(6, r.Mouse.Haptics.Count); // 3 taps, pause, 3 taps, then nothing
        Assert.Equal([100, 100, 1000, 100, 100, HapticPlayer.RestoreDelayMs], r.Timer.Delays);
    }

    [Fact]
    public void Repeats_are_cut_so_a_cue_never_lasts_over_fifteen_seconds()
    {
        var ringer = new HapticCue(true, 5, 6, 200, Repeats: 10, RepeatGapMs: 1000).Clamped();
        Assert.Equal(8, ringer.Repeats); // 8 x 1 s of taps + 7 x 1 s pauses = 15 s; a 9th group would go over
        Assert.True(ringer.TotalMs <= HapticCue.MaxTotalMs);
        Assert.Equal(1, new HapticCue(Repeats: 0).Clamped().Repeats);
        Assert.Equal(HapticCue.RepeatGapMin, new HapticCue(Repeats: 2, RepeatGapMs: 1).Clamped().RepeatGapMs);
    }

    [Fact]
    public void A_new_cue_cancels_the_rest_of_the_old_one()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 4, 100));
        r.Player.Play(new HapticCue(true, 9, 1, 100));
        r.Timer.Elapse(); // the old cue's timer was cancelled: nothing more plays
        Assert.Equal([(byte)5, (byte)9], r.Mouse.Haptics);
    }

    [Fact]
    public void A_timer_callback_already_queued_when_a_new_cue_starts_is_dropped()
    {
        var mouse = new FakeMouse();
        var timer = new FakeHoldTimer();
        var queue = new DeferredQueue();
        var player = new HapticPlayer(mouse, timer, queue, () => new HapticSettings(), new ListLog(), CancellationToken.None);
        player.Play(new HapticCue(true, 1, 3, 100));
        queue.Drain();
        Assert.Equal([(byte)1], mouse.Haptics);
        player.Play(new HapticCue(true, 2, 1, 100)); // the new cue is queued ...
        timer.Elapse(); // ... and the old timer fires too: its pulse 2 is queued behind the new cue
        queue.Drain();
        Assert.Equal([(byte)1, (byte)2], mouse.Haptics);
    }

    [Fact]
    public void Exact_strength_is_set_before_the_first_pulse_and_restored_after_the_last()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 2, 100, 30));
        Assert.Equal(["strength 30", "play 5"], r.Mouse.Calls);
        r.Timer.Elapse();
        Assert.Equal(["strength 30", "play 5", "play 5"], r.Mouse.Calls);
        Assert.Equal([100, HapticPlayer.RestoreDelayMs], r.Timer.Delays);
        r.Timer.Elapse();
        Assert.Equal(["strength 30", "play 5", "play 5", "strength 60"], r.Mouse.Calls); // the global strength
    }

    [Fact]
    public void A_burst_of_exact_cues_writes_twice()
    {
        var r = new Rig();
        for (int i = 0; i < 5; i++) r.Player.Play(new HapticCue(true, 5, 1, 100, 30));
        r.Timer.Elapse();
        Assert.Equal(5, r.Mouse.Haptics.Count);
        Assert.Equal([30, 60], r.Mouse.StrengthWrites);
    }

    [Fact]
    public void A_cue_without_strength_after_a_light_one_plays_at_normal()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 1, 100, 30));
        r.Player.Play(new HapticCue(true, 2, 1, 100));
        Assert.Equal(["strength 30", "play 5", "strength 66", "play 2"], r.Mouse.Calls);
    }

    [Fact]
    public void Back_to_back_buzzes_at_one_level_write_the_strength_once()
    {
        var r = new Rig();
        for (int i = 0; i < 4; i++) r.Player.Play(new HapticCue(true, 1, 2, 100)); // all Normal
        r.Timer.Elapse();
        r.Player.Play(new HapticCue(true, 3, 1, 100, HapticCues.NormalStrength));
        Assert.Equal([66], r.Mouse.StrengthWrites);
    }

    [Fact]
    public void A_different_level_writes_once_more()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 1, Strength: 33));
        r.Player.Play(new HapticCue(true, 1, Strength: 33));
        r.Player.Play(new HapticCue(true, 1, Strength: 100));
        r.Player.Play(new HapticCue(true, 1, Strength: 100));
        Assert.Equal([33, 100], r.Mouse.StrengthWrites);
    }

    [Fact]
    public void A_buzz_at_the_global_strength_writes_nothing()
    {
        var r = new Rig();
        r.Settings = new HapticSettings(Strength: 66);
        r.Player.Play(new HapticCue(true, 1, 2, 100));
        r.Timer.Elapse();
        Assert.Empty(r.Mouse.StrengthWrites);
        Assert.Equal([(byte)1, (byte)1], r.Mouse.Haptics);
        Assert.Equal([100], r.Timer.Delays); // and nothing to restore later
    }

    [Fact]
    public void The_global_strength_comes_back_only_after_five_idle_seconds()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 1, Strength: 100));
        Assert.Equal([5000], r.Timer.Delays);
        Assert.Equal([100], r.Mouse.StrengthWrites);
        r.Timer.Elapse();
        Assert.Equal([100, 60], r.Mouse.StrengthWrites);
    }

    [Fact]
    public void A_buzz_within_five_seconds_postpones_the_restore()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 1, Strength: 100));
        r.Player.Play(new HapticCue(true, 2, Strength: 100));
        Assert.Equal([5000, 5000], r.Timer.Delays); // the first restore was cancelled, a new one scheduled
        r.Timer.Elapse(); // only the live timer fires
        Assert.Equal([100, 60], r.Mouse.StrengthWrites);
        Assert.Single(r.Mouse.StrengthWrites, 60);
    }

    [Fact]
    public void A_cue_naming_a_pattern_is_expanded_before_it_plays()
    {
        var r = new Rig();
        r.Settings = new HapticSettings(Strength: 60) { Patterns = new Dictionary<string, HapticPattern>(StringComparer.OrdinalIgnoreCase) { ["Ringer"] = new HapticPattern(12, 2, 90, 2, 1000) } };
        r.Player.Play(new HapticCue(true, 1, Strength: 100, Custom: "ringer"));
        for (int i = 0; i < 5; i++) r.Timer.Elapse();
        Assert.Equal(4, r.Mouse.Haptics.Count);
        Assert.All(r.Mouse.Haptics, h => Assert.Equal((byte)12, h));
        Assert.Equal([100, 60], r.Mouse.StrengthWrites);
    }

    [Fact]
    public void A_mouse_failure_is_logged_not_thrown()
    {
        var r = new Rig();
        r.Mouse.ThrowOnHaptic = true;
        r.Player.Play(new HapticCue(true, 5, 1, 100));
        Assert.Single(r.Mouse.Haptics);
    }

    [Fact]
    public void Dispose_puts_a_buzz_strength_back()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 1, 100, 30));
        r.Player.Dispose();
        Assert.Equal([30, 60], r.Mouse.StrengthWrites);
        r.Player.Play(new HapticCue(true, 5, 1, 100));
        Assert.Single(r.Mouse.Haptics); // nothing after dispose
    }

    private static SlenderEngine Engine(FakeMouse mouse, SlenderConfig cfg) =>
        new(mouse, new FakeHook(), new FakeForeground(), new FakeExcel(), new FakeExecutor(), cfg, new ListLog(), new InlineQueue(), () => 0, new FakeHoldTimer());

    [Fact]
    public void Engine_routes_events_alerts_and_test_cues_through_the_player()
    {
        var mouse = new FakeMouse();
        var cfg = SlenderConfig.Default with
        {
            Haptics = new HapticSettings
            {
                Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.TouchTap] = new HapticCue(true, 8, 1, 100) },
                Alerts = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["build"] = new HapticCue(true, 7, 1, 100) },
            },
        };
        using var engine = Engine(mouse, cfg);
        engine.PlayHaptic(HapticEvent.TouchTap);
        engine.PlayHaptic(HapticEvent.TouchRest); // off by default
        engine.PlayAlert("Build");
        engine.PlayAlert("unknown");
        engine.PlayAlert(null);
        engine.PlayCue(new HapticCue(false, 12, 1, 100)); // a test plays whatever the switches say
        Assert.Equal([(byte)8, (byte)7, (byte)5, (byte)5, (byte)12], mouse.Haptics);
    }

    [Fact]
    public void Engine_plays_nothing_when_haptics_are_off()
    {
        var mouse = new FakeMouse();
        using var engine = Engine(mouse, SlenderConfig.Default with { Haptics = new HapticSettings(Enabled: false) });
        engine.PlayHaptic(HapticEvent.HoldStart);
        engine.PlayAlert("x");
        Assert.Empty(mouse.Haptics);
    }
}
