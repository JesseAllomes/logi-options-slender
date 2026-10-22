using Slender.Engine;
using Slender.Engine.Ringer;
using Slender.Tests.Engine;

namespace Slender.Tests.Ringer;

public class RingerSessionTests
{
    /// <summary>A timer whose pending entries are fired one by one, by the delay they were scheduled with.</summary>
    private sealed class StepTimer : IHoldTimer
    {
        private sealed class Handle(StepTimer owner, int delay, Action fire) : IDisposable
        {
            public int Delay { get; } = delay;
            public Action Fire { get; } = fire;
            public void Dispose() => owner.Pending.Remove(this);
        }

        private List<Handle> Pending { get; } = [];
        public int PendingCount => Pending.Count;

        public IDisposable Schedule(int delayMs, Action fire)
        {
            var h = new Handle(this, delayMs, fire);
            Pending.Add(h);
            return h;
        }

        public void FireCap() => Fire(RingerSession.MaxRingMs);

        /// <summary>Fires the pending timer that is not the cap (the next ring round).</summary>
        public void FireRound() => Pending.First(h => h.Delay != RingerSession.MaxRingMs).Fire();

        private void Fire(int delay) => Pending.First(h => h.Delay == delay).Fire();
    }

    private sealed class Rig
    {
        public StepTimer Timer = new();
        public List<HapticCue> Played = [];
        public int Cancels;
        public List<bool> Watch = [];
        public HapticCue? Cue = new HapticCue(true, 14, 2, 250, 100, 1, 1500);
        public RingerSession Session;

        public Rig() => Session = new RingerSession(Timer, new InlineQueue(), () => Cue, Played.Add, () => Cancels++, Watch.Add, new ListLog());
    }

    [Fact]
    public void Start_plays_the_cue_and_repeats_it_after_its_length_and_pause_until_stopped()
    {
        var r = new Rig();
        r.Session.Signal(start: true);
        Assert.True(r.Session.IsRinging);
        Assert.Single(r.Played);
        Assert.Equal([true], r.Watch); // the click watch is on only while ringing

        r.Timer.FireRound();
        r.Timer.FireRound();
        Assert.Equal(3, r.Played.Count);
        Assert.All(r.Played, c => Assert.Equal(r.Cue, c));

        r.Session.Signal(start: false);
        Assert.False(r.Session.IsRinging);
        Assert.Equal(1, r.Cancels);
        Assert.Equal([true, false], r.Watch);
        Assert.Equal(0, r.Timer.PendingCount); // nothing left running
    }

    [Fact]
    public void The_pause_between_rings_is_the_cue_length_plus_its_pause()
    {
        var r = new Rig { Cue = new HapticCue(true, 14, 3, 200, 100, 2, 1000) }; // 2 groups of 3 taps 200 ms apart, 1 s between groups: the cue lasts 1800 ms, then its 1 s pause
        var timer = new List<int>();
        r.Session = new RingerSession(new RecordingTimer(timer), new InlineQueue(), () => r.Cue, r.Played.Add, () => { }, _ => { }, new ListLog());
        r.Session.Signal(true);
        Assert.Equal([RingerSession.MaxRingMs, r.Cue!.TotalMs + 1000], timer);
        Assert.Equal(2800, timer[1]);
    }

    private sealed class RecordingTimer(List<int> delays) : IHoldTimer
    {
        public IDisposable Schedule(int delayMs, Action fire)
        {
            delays.Add(delayMs);
            return new Nothing();
        }

        private sealed class Nothing : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void A_mouse_click_ends_the_ring()
    {
        var r = new Rig();
        r.Session.Signal(true);
        r.Session.OnClick();
        Assert.False(r.Session.IsRinging);
        Assert.Equal(1, r.Cancels);
        Assert.Equal([true, false], r.Watch);
        Assert.Equal(0, r.Timer.PendingCount);
    }

    [Fact]
    public void The_safety_cap_ends_a_ring_that_never_got_its_stop()
    {
        var r = new Rig();
        r.Session.Signal(true);
        Assert.Equal(90_000, RingerSession.MaxRingMs);
        r.Timer.FireCap();
        Assert.False(r.Session.IsRinging);
        Assert.Equal([true, false], r.Watch);
        Assert.Equal(0, r.Timer.PendingCount);
    }

    [Fact]
    public void A_repeated_start_keeps_one_ring()
    {
        var r = new Rig();
        r.Session.Signal(true);
        r.Session.Signal(true);
        r.Session.Signal(true);
        Assert.Single(r.Played);
        Assert.Equal([true], r.Watch);
        Assert.Equal(2, r.Timer.PendingCount); // one cap and one next round, never more
        r.Session.Signal(false);
        Assert.Equal(1, r.Cancels);
    }

    [Fact]
    public void A_stop_or_click_with_no_ring_does_nothing_and_a_new_ring_can_follow_an_old_one()
    {
        var r = new Rig();
        r.Session.Signal(false);
        r.Session.OnClick();
        Assert.Empty(r.Played);
        Assert.Empty(r.Watch);
        Assert.Equal(0, r.Cancels);

        r.Session.Signal(true);
        r.Session.OnClick();
        r.Session.Signal(true);
        Assert.Equal(2, r.Played.Count);
        Assert.True(r.Session.IsRinging);
    }

    [Fact]
    public void A_cue_that_is_off_does_not_ring_and_a_cue_switched_off_mid_ring_stops_it()
    {
        var r = new Rig { Cue = null };
        r.Session.Signal(true);
        Assert.False(r.Session.IsRinging);
        Assert.Empty(r.Played);
        Assert.Empty(r.Watch);

        r.Cue = new HapticCue(true, 14, 2, 250, 100, 1, 1500);
        r.Session.Signal(true);
        r.Cue = null;
        r.Timer.FireRound();
        Assert.False(r.Session.IsRinging);
    }

    [Fact]
    public void Dispose_turns_the_click_watch_off_and_ignores_later_signals()
    {
        var r = new Rig();
        r.Session.Signal(true);
        r.Session.Dispose();
        Assert.Equal([true, false], r.Watch);
        r.Session.Signal(true);
        Assert.Single(r.Played);
    }

    // ---- through the engine: ordinary buzzes are dropped while ringing ----

    private static SlenderEngine Engine(FakeMouse mouse, FakeHook hook, FakeHoldTimer timer, HapticSettings haptics) =>
        new(mouse, hook, new FakeForeground(), new FakeExcel(), new FakeExecutor(), SlenderConfig.Default with { Haptics = haptics },
            new ListLog(), new InlineQueue(), () => 0, timer);

    [Fact]
    public void The_engine_drops_ordinary_buzzes_while_ringing_and_plays_them_again_after()
    {
        var mouse = new FakeMouse();
        var hook = new FakeHook();
        using var engine = Engine(mouse, hook, new FakeHoldTimer(), new HapticSettings { Ringer = new HapticCue(true, 9, 1, 150, 100, 1, 1500) });
        engine.PlayAlert(null);
        Assert.Equal([(byte)5], mouse.Haptics);

        engine.SignalRing(new RingSignal(true, RingerSignalSource.Headset));
        Assert.Equal([(byte)5, (byte)9], mouse.Haptics); // the ringer cue, at Strong
        Assert.True(hook.ClickWatch);
        engine.PlayAlert("x");
        engine.PlayHaptic(HapticEvent.HoldStart);
        engine.PlayNotification("app");
        engine.PlayCue(new HapticCue(true, 3));
        Assert.Equal([(byte)5, (byte)9], mouse.Haptics);

        hook.Click(); // any button ends it
        Assert.False(hook.ClickWatch);
        engine.PlayAlert(null);
        Assert.Equal([(byte)5, (byte)9, (byte)5], mouse.Haptics);
    }

    [Fact]
    public void The_engine_reports_each_ring_start_once_and_nothing_exists_before_the_first_signal()
    {
        var hook = new FakeHook();
        using var engine = Engine(new FakeMouse(), hook, new FakeHoldTimer(), new HapticSettings());
        Assert.Equal(0, hook.ClickSubscribers);
        var seen = new List<RingSignalEventArgs>();
        engine.RingSignalReceived += (_, e) => seen.Add(e);

        engine.SignalRing(new RingSignal(true, RingerSignalSource.Notification));
        engine.SignalRing(new RingSignal(false, RingerSignalSource.Notification));
        Assert.Equal(1, hook.ClickSubscribers);
        var only = Assert.Single(seen);
        Assert.Equal(RingerSignalSource.Notification, only.Source);
    }

    [Fact]
    public void The_engine_does_not_ring_when_haptics_are_off()
    {
        var mouse = new FakeMouse();
        using var engine = Engine(mouse, new FakeHook(), new FakeHoldTimer(), new HapticSettings(Enabled: false));
        engine.SignalRing(new RingSignal(true, RingerSignalSource.Headset));
        Assert.Empty(mouse.Haptics);
    }
}
