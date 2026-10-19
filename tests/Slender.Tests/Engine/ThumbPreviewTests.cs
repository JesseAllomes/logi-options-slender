using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

/// <summary>Preview mode for the thumb wheel card, the pipe line protocol, and per-app thumb wheel speeds.</summary>
public class ThumbPreviewTests
{
    private sealed class Sink : IThumbPreviewSink
    {
        public List<string> Lines = [];
        public void Rotated(int delta) => Lines.Add(ThumbPreview.Rotation(delta));
        public void TopButton(bool down) => Lines.Add(ThumbPreview.Top(down));
    }

    private static readonly ThumbWheelSecond Tabs = new(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Keys("Ctrl+Shift+Tab"));

    private static (SlenderEngine Engine, FakeMouse Mouse, FakeHook Hook, FakeExecutor Exec) Rig(SlenderConfig cfg, string process = "msedge")
    {
        var mouse = new FakeMouse();
        var hook = new FakeHook();
        var exec = new FakeExecutor();
        var engine = new SlenderEngine(mouse, hook, new FakeForeground { ProcessName = process }, new FakeExcel(), exec, cfg, new ListLog(), new InlineQueue(), () => 0);
        engine.Start();
        return (engine, mouse, hook, exec);
    }

    private static SlenderConfig Cfg(ThumbWheelSecond? second = null, ActionSpec? top = null)
    {
        var bindings = new Dictionary<ushort, ActionSpec>();
        if (top is not null) bindings[ControlId.ModeShift] = top;
        return SlenderConfig.Default with { Profiles = [new Profile(null, bindings) { ThumbWheelAlt = second }] };
    }

    [Fact]
    public void Preview_gets_turns_and_the_top_button_and_nothing_runs()
    {
        var (engine, mouse, hook, exec) = Rig(Cfg(Tabs, new ActionSpec.Keys("Ctrl+C")));
        var sink = new Sink();
        engine.SetPreview(sink);

        Assert.True(hook.Wheel(120).Handled);
        mouse.Press(ControlId.ModeShift, true);
        hook.Wheel(-60);
        mouse.Press(ControlId.ModeShift, false);

        Assert.Equal(["r 120", "t 1", "r -60", "t 0"], sink.Lines);
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void Preview_diverts_the_top_button_even_when_nothing_is_set_and_ending_it_restores_the_wheel()
    {
        var (engine, mouse, hook, exec) = Rig(Cfg(), "notepad");
        engine.SetPreview(new Sink());
        Assert.True(SpinWait.SpinUntil(() => LastDivert(mouse).Contains(ControlId.ModeShift), 2000)); // diverts run on a chain

        engine.SetPreview(null);
        Assert.True(SpinWait.SpinUntil(() => !LastDivert(mouse).Contains(ControlId.ModeShift), 2000));
        Assert.False(hook.Wheel(120).Handled); // native side scroll again
    }

    private static ushort[] LastDivert(FakeMouse m) { lock (m.Diverts) return m.Diverts.Count == 0 ? [] : m.Diverts[^1]; }

    [Fact]
    public void Preview_started_mid_hold_ends_the_hold()
    {
        var (engine, mouse, hook, _) = Rig(Cfg(Tabs, new ActionSpec.Keys("Ctrl+C")));
        mouse.Press(ControlId.ModeShift, true);
        Assert.True(engine.SecondActionHeld);
        engine.SetPreview(new Sink());
        Assert.False(engine.SecondActionHeld);
        engine.SetPreview(null);
        Assert.False(hook.Wheel(120).Handled);
    }

    [Theory]
    [InlineData("r 120", 'r', 120)]
    [InlineData("r -7", 'r', -7)]
    [InlineData("t 1", 't', 1)]
    [InlineData("t 0", 't', 0)]
    [InlineData("h 1", 'h', 1)]
    [InlineData("g 3", 'g', 3)]
    public void Lines_parse(string line, char kind, int value)
    {
        Assert.True(ThumbPreview.TryParse(line, out var k, out var v));
        Assert.Equal((kind, value), (k, v));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t 2")]
    [InlineData("x 1")]
    [InlineData("r")]
    [InlineData("r abc")]
    [InlineData("g 4")]
    [InlineData("h 2")]
    public void Bad_lines_are_refused(string? line) => Assert.False(ThumbPreview.TryParse(line, out _, out _));

    [Fact]
    public void Speeds_resolve_app_then_all_apps_then_global()
    {
        var cfg = SlenderConfig.Default with
        {
            Pointer = new PointerSettings(SideScrollSpeed: 60, SecondActionSpeed: 80),
            Profiles =
            [
                new Profile(null, new Dictionary<ushort, ActionSpec>()) { SideScrollSpeed = 150, ThumbWheelAlt = Tabs },
                new Profile("excel", new Dictionary<ushort, ActionSpec>()) { SideScrollSpeed = 50, ThumbWheelAlt = Tabs with { Speed = 200 } },
            ],
        };
        Assert.Equal(50, ThumbWheelModes.SideScrollSpeedFor(cfg, "excel"));
        Assert.Equal(150, ThumbWheelModes.SideScrollSpeedFor(cfg, "notepad"));
        Assert.Equal(60, ThumbWheelModes.SideScrollSpeedFor(SlenderConfig.Default with { Pointer = cfg.Pointer }, "notepad"));
        Assert.Equal(200, ThumbWheelModes.SpeedOf(cfg, ThumbWheelModes.SecondFor(cfg, "excel")!));
        Assert.Equal(80, ThumbWheelModes.SpeedOf(cfg, ThumbWheelModes.SecondFor(cfg, "notepad")!));
    }

    [Fact]
    public void Per_app_speeds_and_ring_motion_round_trip_and_clamp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-speed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cfg = SlenderConfig.Default with
            {
                Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()), new Profile("excel", new Dictionary<ushort, ActionSpec>()) { SideScrollSpeed = 9000, ThumbWheelAlt = Tabs with { Speed = 150 } }],
                Ring = new RingAppearance(Opening: RingOpening.FlyIn, AnimationSpeed: RingAnimationSpeed.Slowest),
            };
            var store = new JsonConfigStore(dir);
            store.Save(cfg);
            var back = store.Load();
            var excel = back.Profiles.Single(p => p.AppProcess == "excel");
            Assert.Equal(PointerSettings.SpeedMax, excel.SideScrollSpeed);
            Assert.Equal(150, excel.ThumbWheelAlt!.Speed);
            Assert.Null(back.Profiles.Single(p => p.AppProcess is null).SideScrollSpeed);
            Assert.Equal(RingOpening.FlyIn, back.Ring.Opening);
            Assert.Equal(RingAnimationSpeed.Slowest, back.Ring.AnimationSpeed);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Per_app_side_scroll_speed_scales_the_turn_in_that_app()
    {
        var cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()), new Profile("notepad", new Dictionary<ushort, ActionSpec>()) { SideScrollSpeed = 50 }] };
        var mouse = new FakeMouse();
        var hook = new FakeHook();
        var exec = new FakeKeyExecutor();
        var engine = new SlenderEngine(mouse, hook, new FakeForeground { ProcessName = "notepad" }, new FakeExcel(), exec, cfg, new ListLog(), new InlineQueue(), () => 0);
        engine.Start();
        Assert.True(hook.Wheel(120).Handled);
        Assert.Equal([60], exec.SideScrolls);
    }
}
