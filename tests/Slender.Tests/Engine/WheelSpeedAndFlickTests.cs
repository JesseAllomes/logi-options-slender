using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Engine;

public class WheelSpeedAndFlickTests
{
    private static (SlenderEngine Engine, FakeHook Hook, FakeKeyExecutor Exec, Func<long, long> SetNow) Rig(SlenderConfig cfg, string process = "excel")
    {
        long now = 1000;
        var hook = new FakeHook();
        var exec = new FakeKeyExecutor();
        var engine = new SlenderEngine(new FakeMouse(), hook, new FakeForeground { ProcessName = process }, new FakeExcel(), exec, cfg, new ListLog(), new InlineQueue(), () => now);
        engine.Start();
        return (engine, hook, exec, n => now = n);
    }

    private static SlenderConfig With(params Profile[] profiles) => SlenderConfig.Default with { Profiles = profiles };

    private static Profile P(string? app) => new(app, new Dictionary<ushort, ActionSpec>());

    [Fact]
    public void Main_wheel_is_left_alone_at_normal_speed()
    {
        var (_, hook, exec, _) = Rig(With(P(null)));
        Assert.False(hook.MainWheel(120).Handled);
        Assert.Empty(exec.MainScrolls);
    }

    [Fact]
    public void Main_wheel_speed_per_app_rescales_and_carries_fractions()
    {
        var (_, hook, exec, _) = Rig(With(P(null), P("excel") with { WheelSpeed = 50 }));
        Assert.True(hook.MainWheel(120).Handled);
        hook.MainWheel(-120);
        hook.MainWheel(30);
        hook.MainWheel(30);
        Assert.Equal([60, -60, 15, 15], exec.MainScrolls);
    }

    [Fact]
    public void Main_wheel_speed_follows_all_apps()
    {
        var (_, hook, exec, _) = Rig(With(P(null) with { WheelSpeed = 200 }), "notepad");
        Assert.True(hook.MainWheel(120).Handled);
        Assert.Equal([240], exec.MainScrolls);
    }

    [Fact]
    public void Detector_fires_once_per_fast_spin_and_swallows_the_rest_until_quiet()
    {
        var d = new FlickDetector();
        Assert.Equal(FlickResult.None, d.Add(240, 0));
        Assert.Equal(FlickResult.None, d.Add(240, 40));
        Assert.Equal(FlickResult.Fired, d.Add(240, 80));    // 720 units in 80 ms
        Assert.Equal(FlickResult.Swallow, d.Add(240, 150));
        Assert.Equal(FlickResult.None, d.Add(120, 500));    // quiet again: a normal turn
    }

    [Fact]
    public void Slow_turning_never_flicks()
    {
        var d = new FlickDetector();
        for (long t = 0; t < 2000; t += 100)
        {
            Assert.Equal(FlickResult.None, d.Add(120, t)); // a notch every 100 ms
        }
    }

    [Fact]
    public void A_turn_back_starts_the_count_over()
    {
        var d = new FlickDetector();
        d.Add(360, 0);
        Assert.Equal(FlickResult.None, d.Add(-360, 20));
    }

    [Fact]
    public void A_flick_runs_its_action_and_does_not_side_scroll()
    {
        var flick = new ThumbWheelFlick(true, new ActionSpec.Keys("Ctrl+End"), new ActionSpec.Keys("Ctrl+Home"));
        var (_, hook, exec, setNow) = Rig(With(P(null) with { ThumbFlick = flick }, P("excel") with { SideScrollSpeed = 50 }));
        hook.Wheel(-300);
        setNow(1040);
        Assert.True(hook.Wheel(-360).Handled);
        Assert.Contains(exec.Calls, c => c.Item1 is ActionSpec.Keys { Chord: "Ctrl+Home" } && c.Item2);
        Assert.DoesNotContain(exec.Calls, c => c.Item1 is ActionSpec.Keys { Chord: "Ctrl+End" });
    }

    [Fact]
    public void Card_model_edits_wheel_speed_and_flick_per_app()
    {
        SlenderConfig c = ThumbCardModel.SetWheelSpeed(With(P(null)), "edge", 9000);
        Assert.Equal(new ThumbSpeedView(PointerSettings.SpeedMax, false), ThumbCardModel.WheelSpeed(c, "edge"));
        Assert.Equal(new ThumbSpeedView(100, true), ThumbCardModel.WheelSpeed(c, "excel"));

        var flick = new ThumbWheelFlick(true, new ActionSpec.Keys("Ctrl+End"), new ActionSpec.None());
        c = ThumbCardModel.SetFlick(c, null, flick);
        Assert.Equal((flick, true), ThumbCardModel.Flick(c, "excel"));
        Assert.Equal((flick, false), ThumbCardModel.Flick(c, null));
    }
}
