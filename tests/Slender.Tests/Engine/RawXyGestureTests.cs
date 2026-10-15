using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class RawXyGestureTests
{
    private static readonly ActionSpec Click = new ActionSpec.Keys("Ctrl+Click");
    private static readonly ActionSpec Up = new ActionSpec.Keys("Win+Tab");
    private static readonly ActionSpec Left = new ActionSpec.Keys("Ctrl+Win+Left");

    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExecutor Exec = new();
        public long Now;
        public SlenderEngine Engine;
        public Rig(SlenderConfig cfg, bool rawActive = true, int? deviceDpi = null)
        {
            Mouse.RawActive = rawActive;
            Mouse.Dpi = deviceDpi;
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

    private static SlenderConfig Standard() => Cfg(Def(new() { [GestureDirection.Up] = Up, [GestureDirection.Left] = Left }, Click));

    // ---- thresholds and direction resolution ----

    [Theory]
    [InlineData(1000, 50)]
    [InlineData(2000, 100)]
    [InlineData(500, 25)]
    [InlineData(200, 10)]
    [InlineData(4000, 200)]
    [InlineData(1600, 80)]
    public void Raw_threshold_scales_with_dpi(int dpi, int expected) => Assert.Equal(expected, GestureTracker.RawThresholdFor(dpi));

    [Fact]
    public void Raw_threshold_matches_the_pixel_threshold_at_the_reference_dpi() =>
        Assert.Equal(GestureTracker.SwipeThresholdPx, GestureTracker.RawThresholdFor(GestureTracker.ReferenceDpi));

    [Theory]
    [InlineData(0, -60, 1000, GestureDirection.Up)]
    [InlineData(0, 40, 1000, null)]
    [InlineData(0, 49, 1000, null)]
    [InlineData(0, 50, 1000, GestureDirection.Down)]
    [InlineData(-120, 10, 2000, GestureDirection.Left)]
    [InlineData(0, -90, 2000, null)]   // 90 counts at 2000 DPI is under the 100 count threshold
    [InlineData(40, 0, 800, GestureDirection.Right)] // 40 counts at 800 DPI hits the 40 count threshold
    public void Direction_resolves_from_raw_deltas_with_dpi_scaling(int dx, int dy, int dpi, GestureDirection? expected) =>
        Assert.Equal(expected, GestureTracker.Resolve(dx, dy, GestureTracker.RawThresholdFor(dpi)));

    [Fact]
    public void Tracker_uses_the_threshold_given_at_begin()
    {
        var t = new GestureTracker();
        t.Begin(0, GestureTracker.RawThresholdFor(2000));
        t.Add(0, -60, 1);
        Assert.Equal((true, (GestureDirection?)null), t.End(2));
        t.Begin(10, GestureTracker.RawThresholdFor(2000));
        t.Add(0, -60, 11); t.Add(0, -45, 12);
        Assert.Equal((true, (GestureDirection?)GestureDirection.Up), t.End(13));
    }

    // ---- engine: raw XY source ----

    [Fact]
    public void Raw_swipe_runs_the_direction_action_without_capturing_the_cursor()
    {
        var r = new Rig(Standard());
        r.Press();
        Assert.False(r.Hook.Capturing);
        r.Mouse.RawMove(0, -30);
        r.Mouse.RawMove(3, -30);
        Assert.False(r.Hook.Move(0, -30)); // the hook never swallows while raw XY is the source
        r.Release();
        Assert.False(r.Hook.Capturing);
        Assert.Equal([Up], r.Ran);
    }

    [Fact]
    public void Raw_release_without_enough_movement_runs_the_click()
    {
        var r = new Rig(Standard());
        r.Press();
        r.Mouse.RawMove(10, -10);
        r.Release();
        Assert.Equal([Click], r.Ran);
    }

    [Fact]
    public void Raw_threshold_follows_the_configured_dpi()
    {
        var cfg = Standard() with { Pointer = new PointerSettings(Dpi: 2000) };
        var r = new Rig(cfg);
        r.Press(); r.Mouse.RawMove(0, -90); r.Release();
        Assert.Equal([Click], r.Ran); // 90 < 100 counts at 2000 DPI
        r.Press(); r.Mouse.RawMove(0, -110); r.Release();
        Assert.Equal([Click, Up], r.Ran);
    }

    [Fact]
    public async Task Raw_threshold_falls_back_to_the_dpi_read_from_the_device()
    {
        var r = new Rig(Standard(), deviceDpi: 400);
        r.Mouse.Connect(true);
        for (var i = 0; i < 200 && !r.Ran.Contains(Up); i++)
        {
            // 25 counts is a click at the 1000 DPI default (50) and a swipe once the device's 400 DPI (20 counts) has been read
            r.Press(); r.Mouse.RawMove(0, -25); r.Release();
            await Task.Delay(10);
        }
        Assert.Contains(Up, r.Ran);
    }

    [Fact]
    public void Raw_moves_without_a_gesture_are_ignored()
    {
        var r = new Rig(Standard());
        r.Mouse.RawMove(0, -100);
        r.Press(); r.Release();
        Assert.Equal([Click], r.Ran);
    }

    // ---- fallback to the hook ----

    [Fact]
    public void Without_raw_xy_the_hook_path_is_unchanged()
    {
        var r = new Rig(Standard(), rawActive: false);
        r.Press();
        Assert.True(r.Hook.Capturing);
        r.Mouse.RawMove(0, -100); // stray raw events are not used on the hook path
        Assert.True(r.Hook.Move(0, -60));
        r.Release();
        Assert.False(r.Hook.Capturing);
        Assert.Equal([Up], r.Ran);
    }

    [Fact]
    public void Hook_path_keeps_the_pixel_threshold()
    {
        var r = new Rig(Standard() with { Pointer = new PointerSettings(Dpi: 2000) }, rawActive: false);
        r.Press(); r.Hook.Move(0, -60); r.Release();
        Assert.Equal([Up], r.Ran); // 60 px passes the 50 px threshold whatever the DPI
    }

    // ---- timeout and aborts ----

    [Fact]
    public void Raw_gesture_times_out_without_running_anything()
    {
        var r = new Rig(Standard());
        r.Press();
        r.Mouse.RawMove(0, -10);
        r.Now = GestureTracker.TimeoutMs + 1;
        r.Mouse.RawMove(0, -80);
        r.Release();
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Disconnect_aborts_a_raw_gesture()
    {
        var r = new Rig(Standard());
        r.Press();
        r.Mouse.RawMove(0, -80);
        r.Mouse.Connect(false);
        r.Release(); // the late button-up from a vanished device does nothing
        Assert.Empty(r.Exec.Calls);
        r.Mouse.RawMove(0, -80); // and later raw moves do not start anything
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Dispose_aborts_a_raw_gesture()
    {
        var r = new Rig(Standard());
        r.Press();
        r.Engine.Dispose();
        r.Mouse.RawMove(0, -80);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Click_only_binding_stays_on_the_plain_press_release_path()
    {
        var r = new Rig(Cfg(Def(click: Click)));
        r.Press();
        r.Mouse.RawMove(0, -100);
        r.Release();
        Assert.Equal([(Click, true), (Click, false)], r.Exec.Calls.ToArray());
    }

    // ---- divert set ----

    [Fact]
    public void Raw_xy_set_is_the_gesture_button_only_when_a_swipe_is_bound()
    {
        Assert.Equal([ControlId.Gesture], SlenderEngine.ComputeRawXySet(Standard(), null));
        Assert.Empty(SlenderEngine.ComputeRawXySet(Cfg(Def(click: Click)), null));
        Assert.Empty(SlenderEngine.ComputeRawXySet(Cfg(Def(new() { [GestureDirection.Up] = new ActionSpec.None() })), null));
    }

    [Fact]
    public void Raw_xy_set_follows_the_effective_profile()
    {
        var app = new Profile("teams", new Dictionary<ushort, ActionSpec>())
        { GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = new ActionSpec.None(), [GestureDirection.Left] = new ActionSpec.None(), [GestureDirection.Right] = new ActionSpec.None(), [GestureDirection.Down] = new ActionSpec.None() } };
        var cfg = Cfg(Def(new() { [GestureDirection.Up] = Up }, Click), app);
        Assert.Equal([ControlId.Gesture], SlenderEngine.ComputeRawXySet(cfg, null));
        Assert.Empty(SlenderEngine.ComputeRawXySet(cfg, "teams"));
        Assert.Equal([ControlId.Gesture], SlenderEngine.ComputeDivertSet(cfg, "teams")); // the click is still diverted, plainly
    }

    [Fact]
    public async Task Engine_asks_the_mouse_for_raw_xy_only_with_swipes_bound()
    {
        var swipes = new Rig(Standard());
        for (var i = 0; i < 200 && swipes.Mouse.RawDiverts.Count == 0; i++) await Task.Delay(10);
        Assert.Equal([ControlId.Gesture], swipes.Mouse.RawDiverts[0]);

        var clickOnly = new Rig(Cfg(Def(click: Click)));
        for (var i = 0; i < 200 && clickOnly.Mouse.RawDiverts.Count == 0; i++) await Task.Delay(10);
        Assert.Empty(clickOnly.Mouse.RawDiverts[0]);
    }
}
