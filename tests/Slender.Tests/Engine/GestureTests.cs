using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class GestureTrackerTests
{
    [Theory]
    [InlineData(0, -60, GestureDirection.Up)]
    [InlineData(5, 60, GestureDirection.Down)]
    [InlineData(-70, 20, GestureDirection.Left)]
    [InlineData(70, -20, GestureDirection.Right)]
    [InlineData(50, 0, GestureDirection.Right)]
    [InlineData(30, -55, GestureDirection.Up)]
    public void Dominant_axis_past_threshold_wins(int dx, int dy, GestureDirection expected) =>
        Assert.Equal(expected, GestureTracker.Resolve(dx, dy));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49, 0)]
    [InlineData(-30, 49)]
    public void Below_threshold_is_a_click(int dx, int dy) => Assert.Null(GestureTracker.Resolve(dx, dy));

    [Fact]
    public void Threshold_is_fifty_pixels() => Assert.Equal(50, GestureTracker.SwipeThresholdPx);

    [Fact]
    public void Movement_accumulates_across_events()
    {
        var t = new GestureTracker();
        t.Begin(0);
        for (var i = 0; i < 6; i++) Assert.True(t.Add(0, -10, i));
        Assert.Equal((true, (GestureDirection?)GestureDirection.Up), t.End(10));
    }

    [Fact]
    public void Add_without_a_gesture_is_not_swallowed()
    {
        var t = new GestureTracker();
        Assert.False(t.Add(100, 0, 0));
        t.Begin(0);
        t.End(1);
        Assert.False(t.Add(100, 0, 2));
    }

    [Fact]
    public void Timeout_abandons_the_gesture()
    {
        var t = new GestureTracker();
        t.Begin(0);
        Assert.False(t.Add(100, 0, GestureTracker.TimeoutMs + 1));
        Assert.False(t.Active);
        Assert.Equal((false, (GestureDirection?)null), t.End(GestureTracker.TimeoutMs + 2));
    }

    [Fact]
    public void Cancel_ends_it()
    {
        var t = new GestureTracker();
        t.Begin(0);
        t.Cancel();
        Assert.False(t.Add(10, 10, 1));
    }
}

public class GestureEngineTests
{
    private static readonly ActionSpec Click = new ActionSpec.Keys("Ctrl+Click");
    private static readonly ActionSpec Up = new ActionSpec.Keys("Win+Tab");
    private static readonly ActionSpec Left = new ActionSpec.Keys("Ctrl+Win+Left");
    private static readonly ActionSpec TeamsUp = new ActionSpec.Keys("Win");

    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExecutor Exec = new();
        public long Now;
        public SlenderEngine Engine;
        public Rig(SlenderConfig cfg)
        {
            Engine = new SlenderEngine(Mouse, Hook, Fg, new FakeExcel(), Exec, cfg, new ListLog(), new InlineQueue(), () => Now);
            Engine.Start();
        }
        public void Press() => Mouse.Press(ControlId.Gesture, true);
        public void Release() => Mouse.Press(ControlId.Gesture, false);
        public ActionSpec[] Ran => Exec.Calls.Where(c => c.Item2).Select(c => c.Item1).ToArray();
    }

    private static Profile Def(Dictionary<GestureDirection, ActionSpec>? swipes = null, ActionSpec? click = null) =>
        new(null, click is null ? new Dictionary<ushort, ActionSpec>() : new Dictionary<ushort, ActionSpec> { [ControlId.Gesture] = click })
        { GestureSwipes = swipes ?? [] };

    private static SlenderConfig Cfg(params Profile[] p) => SlenderConfig.Default with { Profiles = p };

    private static Rig Standard() => new(Cfg(Def(new() { [GestureDirection.Up] = Up, [GestureDirection.Left] = Left }, Click)));

    [Fact]
    public void Swipe_runs_direction_action_and_swallows_moves()
    {
        var r = Standard();
        r.Press();
        Assert.True(r.Hook.Capturing);
        Assert.True(r.Hook.Move(0, -30));
        Assert.True(r.Hook.Move(2, -30));
        r.Release();
        Assert.False(r.Hook.Capturing);
        Assert.Equal([Up], r.Ran);
    }

    [Fact]
    public void Release_without_enough_movement_runs_click()
    {
        var r = Standard();
        r.Press();
        r.Hook.Move(10, -10);
        r.Release();
        Assert.Equal([Click], r.Ran);
    }

    [Fact]
    public void Unbound_direction_does_nothing()
    {
        var r = Standard();
        r.Press();
        r.Hook.Move(0, 80);
        r.Release();
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Moves_are_never_swallowed_without_a_gesture()
    {
        var r = Standard();
        Assert.False(r.Hook.Move(100, 0)); // not capturing
        r.Press();
        r.Release();
        Assert.False(r.Hook.Move(100, 0));
    }

    [Fact]
    public void Disconnect_releases_the_cursor()
    {
        var r = Standard();
        r.Press();
        r.Mouse.Connect(false);
        Assert.False(r.Hook.Capturing);
        Assert.False(r.Hook.Move(100, 0));
        r.Release(); // the late button-up from a vanished device does nothing
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Safety_timeout_stops_swallowing()
    {
        var r = Standard();
        r.Press();
        r.Now = GestureTracker.TimeoutMs + 1;
        Assert.False(r.Hook.Move(0, -80)); // handler declines and capture is turned off
        Assert.False(r.Hook.Capturing);
        r.Release();
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Dispose_releases_the_cursor()
    {
        var r = Standard();
        r.Press();
        r.Engine.Dispose();
        Assert.False(r.Hook.Capturing);
    }

    [Fact]
    public void Click_only_config_keeps_the_plain_press_release_path()
    {
        var r = new Rig(Cfg(Def(click: Click)));
        r.Press();
        Assert.False(r.Hook.Capturing);
        r.Release();
        Assert.Equal([(Click, true), (Click, false)], r.Exec.Calls.ToArray());
    }

    [Fact]
    public void Swipe_only_config_diverts_the_gesture_button()
    {
        var cfg = Cfg(Def(new() { [GestureDirection.Up] = Up }));
        Assert.Equal([ControlId.Gesture], SlenderEngine.ComputeDivertSet(cfg));
        Assert.Empty(SlenderEngine.ComputeDivertSet(Cfg(Def(new() { [GestureDirection.Up] = new ActionSpec.None() }))));
    }

    [Fact]
    public void App_profile_overrides_and_falls_back_per_direction()
    {
        var app = new Profile("teams", new Dictionary<ushort, ActionSpec>()) { GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = TeamsUp } };
        var r = new Rig(Cfg(Def(new() { [GestureDirection.Up] = Up, [GestureDirection.Left] = Left }, Click), app));
        r.Fg.ProcessName = "teams";
        r.Press(); r.Hook.Move(0, -60); r.Release();
        r.Press(); r.Hook.Move(-60, 0); r.Release();
        Assert.Equal([TeamsUp, Left], r.Ran);
    }

    [Fact]
    public void Explicit_none_in_app_profile_suppresses_the_default_swipe()
    {
        var app = new Profile("teams", new Dictionary<ushort, ActionSpec>()) { GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = new ActionSpec.None() } };
        var r = new Rig(Cfg(Def(new() { [GestureDirection.Up] = Up, [GestureDirection.Left] = Left }), app));
        r.Fg.ProcessName = "teams";
        r.Press(); r.Hook.Move(0, -60); r.Release();
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Swipe_plays_haptic_only_when_button_press_haptics_are_on()
    {
        var on = Cfg(Def(new() { [GestureDirection.Up] = Up }));
        var r = new Rig(on with { Haptics = on.Haptics with { Enabled = true, OnButtonPress = true } });
        r.Press(); r.Hook.Move(0, -60); r.Release();
        Assert.Single(r.Mouse.Haptics);

        var off = new Rig(on with { Haptics = on.Haptics with { Enabled = true, OnButtonPress = false } });
        off.Press(); off.Hook.Move(0, -60); off.Release();
        Assert.Empty(off.Mouse.Haptics);
    }
}
