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
        Assert.Empty(r.Timer.Delays);
        Assert.Empty(r.Mouse.StrengthWrites);
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
        Assert.Equal([120, 120], r.Timer.Delays);
        r.Timer.Elapse();
        Assert.Equal(3, r.Mouse.Haptics.Count); // nothing left
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
    public void A_cue_without_strength_after_an_exact_one_puts_the_global_strength_back_first()
    {
        var r = new Rig();
        r.Player.Play(new HapticCue(true, 5, 1, 100, 30));
        r.Player.Play(new HapticCue(true, 2, 1, 100));
        Assert.Equal(["strength 30", "play 5", "strength 60", "play 2"], r.Mouse.Calls);
        r.Timer.Elapse(); // the cancelled restore does not write again
        Assert.Equal([30, 60], r.Mouse.StrengthWrites);
    }

    [Fact]
    public void Plain_cues_never_write_the_strength()
    {
        var r = new Rig();
        for (int i = 0; i < 4; i++) r.Player.Play(new HapticCue(true, 1, 2, 100));
        r.Timer.Elapse();
        Assert.Empty(r.Mouse.StrengthWrites);
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
    public void Dispose_puts_an_exact_strength_back()
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
