using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

/// <summary>The thumb wheel's second action: held on the top button, released = off, a quick press keeps the button's own job.</summary>
public class TopButtonSecondActionTests
{
    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExcel Excel = new();
        public FakeExecutor Exec = new();
        public SlenderEngine Engine;
        public long Now;
        public Rig(SlenderConfig cfg, string? process = "msedge")
        {
            Fg.ProcessName = process;
            Engine = new SlenderEngine(Mouse, Hook, Fg, Excel, Exec, cfg, new ListLog(), new InlineQueue(), () => Now) { ExcelColumnScrolling = true };
            Engine.Start();
        }
        public void Down() => Mouse.Press(ControlId.ModeShift, true);
        public void Up() => Mouse.Press(ControlId.ModeShift, false);
        public string[] Keys() => Exec.Calls.Where(c => c.Item1 is ActionSpec.Keys && c.Item2).Select(c => ((ActionSpec.Keys)c.Item1).Chord).ToArray();
    }

    private static readonly ThumbWheelSecond Zoom = new(true, new ActionSpec.Keys("Ctrl+Plus"), new ActionSpec.Keys("Ctrl+Minus"));
    private static readonly ActionSpec Copy = new ActionSpec.Keys("Ctrl+C");

    private static SlenderConfig Cfg(ActionSpec? top, ThumbWheelSecond? second, params Profile[] extra)
    {
        var bindings = new Dictionary<ushort, ActionSpec>();
        if (top is not null) bindings[ControlId.ModeShift] = top;
        return SlenderConfig.Default with { Profiles = [new Profile(null, bindings) { ThumbWheelAlt = second }, .. extra] };
    }

    [Fact]
    public void Holding_the_top_button_runs_the_second_action_and_release_ends_it()
    {
        var r = new Rig(Cfg(Copy, Zoom));
        Assert.False(r.Hook.Wheel(120).Handled); // not held: normal side scroll
        r.Down();
        Assert.True(r.Hook.Wheel(120).Handled);
        r.Now = 10;
        r.Hook.Wheel(-120);
        Assert.Equal(["Ctrl+Plus", "Ctrl+Minus"], r.Keys());
        r.Now = 20;
        r.Up();
        Assert.DoesNotContain(r.Exec.Calls, c => c.Item1 == Copy); // the wheel was used: no tap
        r.Exec.Calls.Clear();
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void A_quick_press_runs_the_buttons_own_job_on_release()
    {
        var r = new Rig(Cfg(Copy, Zoom));
        r.Down();
        Assert.Empty(r.Exec.Calls); // it waits: this may become a hold
        r.Now = 100;
        r.Up();
        Assert.Equal([(Copy, true), (Copy, false)], r.Exec.Calls.Select(c => (c.Item1, c.Item2)));
    }

    [Fact]
    public void A_long_hold_without_turning_does_nothing()
    {
        var r = new Rig(Cfg(Copy, Zoom));
        r.Down();
        r.Now = ActionSpec.TapHold.HoldMs;
        r.Up();
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Nothing_set_means_the_button_works_as_before()
    {
        var r = new Rig(Cfg(Copy, null), "notepad");
        r.Down();
        Assert.Contains((Copy, true), r.Exec.Calls.Select(c => (c.Item1, c.Item2))); // straight away, no waiting
        Assert.False(r.Hook.Wheel(120).Handled);
        r.Up();
        Assert.Contains((Copy, false), r.Exec.Calls.Select(c => (c.Item1, c.Item2)));
    }

    [Fact]
    public void Both_directions_unassigned_counts_as_nothing_set()
    {
        var blank = new ThumbWheelSecond(true, new ActionSpec.None(), new ActionSpec.None());
        Assert.Null(ThumbWheelModes.SecondFor(Cfg(Copy, blank), "notepad"));
        Assert.Null(ThumbWheelModes.SecondFor(Cfg(Copy, Zoom with { Enabled = false }), "notepad"));
    }

    [Fact]
    public void Browsers_have_no_built_in_second_action()
    {
        Assert.Null(ThumbWheelModes.SecondFor(SlenderConfig.Default, "msedge"));
        var r = new Rig(Cfg(Copy, null));
        r.Down();
        Assert.False(r.Hook.Wheel(120).Handled);
    }

    [Fact]
    public void Each_notch_runs_press_then_release_and_hires_deltas_accumulate()
    {
        var r = new Rig(Cfg(Copy, null, new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = Zoom }));
        r.Down();
        Assert.True(r.Hook.Wheel(40).Handled);
        r.Hook.Wheel(40);
        Assert.Empty(r.Exec.Calls);
        r.Hook.Wheel(40);
        Assert.Equal([(new ActionSpec.Keys("Ctrl+Plus"), true), (new ActionSpec.Keys("Ctrl+Plus"), false)], r.Exec.Calls.Select(c => (c.Item1, c.Item2)));
        r.Exec.Calls.Clear();
        r.Hook.Wheel(-240);
        Assert.Equal(["Ctrl+Minus", "Ctrl+Minus"], r.Keys());
    }

    [Fact]
    public void Forward_and_back_can_be_any_action()
    {
        var two = new ThumbWheelSecond(true, new ActionSpec.Launch("calc.exe"), new ActionSpec.Excel(ExcelCommand.ZoomIn));
        var r = new Rig(Cfg(Copy, two));
        r.Down();
        r.Hook.Wheel(120);
        r.Now = 10;
        r.Hook.Wheel(-120);
        Assert.Contains(r.Exec.Calls, c => c.Item1 is ActionSpec.Launch && c.Item2);
        Assert.Contains(r.Exec.Calls, c => c.Item1 is ActionSpec.Excel && c.Item2);
    }

    [Fact]
    public void Excel_columns_only_while_not_held()
    {
        var r = new Rig(Cfg(Copy, null, new Profile("excel", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = Zoom }), "excel");
        Assert.True(r.Hook.Wheel(120).Handled);
        Assert.Single(r.Excel.Scrolls);
        r.Down();
        r.Now = 10;
        r.Hook.Wheel(120);
        Assert.Single(r.Excel.Scrolls); // held: the wheel runs the second action instead
        Assert.Equal(["Ctrl+Plus"], r.Keys());
        r.Up();
        r.Now = 20;
        r.Hook.Wheel(120);
        Assert.Equal(2, r.Excel.Scrolls.Count);
    }

    [Fact]
    public void A_native_top_button_is_diverted_and_a_quick_press_flips_ratchet_itself()
    {
        var r = new Rig(Cfg(null, Zoom));
        Assert.Contains(ControlId.ModeShift, SlenderEngine.ComputeDivertSet(r.Engine.Config, "msedge"));
        r.Down();
        r.Now = 100;
        r.Up();
        Assert.Equal(1, r.Mouse.WheelModeToggles);
        Assert.Empty(r.Exec.Calls);
        // Turning the wheel while held is the second action, never a ratchet flip.
        r.Down();
        r.Hook.Wheel(120);
        r.Up();
        Assert.Equal(1, r.Mouse.WheelModeToggles);
    }

    [Fact]
    public void A_native_top_button_stays_native_when_nothing_is_set()
    {
        Assert.DoesNotContain(ControlId.ModeShift, SlenderEngine.ComputeDivertSet(Cfg(null, null), "msedge"));
    }

    [Fact]
    public void A_press_hold_binding_gives_its_quick_press()
    {
        var r = new Rig(Cfg(new ActionSpec.TapHold(Copy, new ActionSpec.ActionsRing()), Zoom));
        r.Down();
        r.Now = 100;
        r.Up();
        Assert.Equal([(Copy, true), (Copy, false)], r.Exec.Calls.Select(c => (c.Item1, c.Item2)));
    }

    [Fact]
    public void Disconnect_while_held_ends_the_second_action()
    {
        var r = new Rig(Cfg(Copy, Zoom));
        r.Down();
        r.Mouse.Connect(false);
        r.Mouse.Connect(true);
        Assert.False(r.Hook.Wheel(120).Handled);
    }

    [Fact]
    public void Old_switch_bindings_load_as_nothing()
    {
        var json = """{"type":"toggleThumbWheel"}""";
        var action = System.Text.Json.JsonSerializer.Deserialize<ActionSpec>(json, new System.Text.Json.JsonSerializerOptions { Converters = { new ActionSpecJsonConverter() } });
        Assert.IsType<ActionSpec.None>(action);
    }
}
