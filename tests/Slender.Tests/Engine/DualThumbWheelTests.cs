using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class DualThumbWheelTests
{
    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExcel Excel = new();
        public FakeExecutor Exec = new();
        public SlenderEngine Engine;
        public List<ThumbWheelModeEventArgs> Modes = [];
        public long Now;
        public Rig(SlenderConfig cfg, string? process = "msedge")
        {
            Fg.ProcessName = process;
            Engine = new SlenderEngine(Mouse, Hook, Fg, Excel, Exec, cfg, new ListLog(), new InlineQueue(), () => Now);
            Engine.ThumbWheelModeChanged += (_, e) => Modes.Add(e);
            Engine.Start();
        }
        public void Flip() => Mouse.Press(ControlId.ModeShift, true);
        public string[] Keys() => Exec.Calls.Where(c => c.Item1 is ActionSpec.Keys && c.Item2).Select(c => ((ActionSpec.Keys)c.Item1).Chord).ToArray();
    }

    private static readonly ThumbWheelSecond Zoom = new(true, new ActionSpec.Keys("Ctrl+Plus"), new ActionSpec.Keys("Ctrl+Minus"));

    private static SlenderConfig Cfg(params Profile[] extra) => SlenderConfig.Default with
    {
        Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.ModeShift] = new ActionSpec.ToggleThumbWheelMode() }), .. extra],
    };

    [Fact]
    public void Browser_flips_to_tabs_and_back()
    {
        var r = new Rig(Cfg());
        r.Flip();
        Assert.True(r.Engine.IsThumbWheelSecond());
        Assert.Equal("Tabs", r.Modes.Single().Label);
        Assert.True(r.Hook.Wheel(120).Handled);
        r.Now = 10;
        r.Hook.Wheel(-120);
        Assert.Equal(["Ctrl+Tab", "Ctrl+Shift+Tab"], r.Keys());
        r.Flip();
        Assert.False(r.Engine.IsThumbWheelSecond());
        Assert.Equal("Side scroll", r.Modes[1].Label);
        r.Exec.Calls.Clear();
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Each_notch_runs_press_then_release_and_hires_deltas_accumulate()
    {
        var r = new Rig(Cfg(new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = Zoom }));
        r.Flip();
        r.Exec.Calls.Clear();
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
        var r = new Rig(Cfg(new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = two }));
        r.Flip();
        Assert.Equal("", r.Modes.Single().Label);
        r.Hook.Wheel(120);
        r.Now = 10;
        r.Hook.Wheel(-120);
        Assert.Contains(r.Exec.Calls, c => c.Item1 is ActionSpec.Launch && c.Item2);
        Assert.Contains(r.Exec.Calls, c => c.Item1 is ActionSpec.Excel && c.Item2);
    }

    [Fact]
    public void Mode_is_remembered_per_app_across_foreground_switches()
    {
        var r = new Rig(Cfg());
        r.Flip();
        r.Fg.Switch("chrome");
        Assert.False(r.Engine.IsThumbWheelSecond());
        Assert.False(r.Hook.Wheel(120).Handled);
        r.Fg.Switch("msedge");
        Assert.True(r.Engine.IsThumbWheelSecond());
        Assert.True(r.Hook.Wheel(120).Handled);
    }

    [Fact]
    public void Toggle_in_app_without_second_role_does_nothing_but_says_so()
    {
        var r = new Rig(Cfg(), "notepad");
        r.Flip();
        Assert.False(r.Engine.IsThumbWheelSecond());
        var e = Assert.Single(r.Modes);
        Assert.Equal("No second role in Notepad", e.Label);
        Assert.Null(e.Role);
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Turning_the_setting_off_while_flipped_falls_back_to_normal()
    {
        var r = new Rig(Cfg());
        r.Flip();
        r.Engine.ApplyConfig(Cfg(new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = Zoom with { Enabled = false } }));
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Exec.Calls);
        r.Engine.ApplyConfig(Cfg());
        Assert.False(r.Engine.IsThumbWheelSecond()); // not silently flipped again when the role returns
    }

    [Fact]
    public void Excel_columns_unaffected_until_flipped()
    {
        var zoom = new Profile("excel", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = Zoom };
        var r = new Rig(Cfg(zoom), "excel");
        Assert.True(r.Hook.Wheel(120).Handled);
        Assert.Single(r.Excel.Scrolls);
        r.Flip();
        r.Now = 10;
        r.Hook.Wheel(120);
        Assert.Single(r.Excel.Scrolls); // the flipped wheel no longer scrolls columns
        Assert.Equal(["Ctrl+Plus"], r.Keys());
        r.Flip();
        r.Now = 20;
        r.Hook.Wheel(120);
        Assert.Equal(2, r.Excel.Scrolls.Count);
    }

    [Fact]
    public void Toggle_works_from_hook_button_and_ring_slot()
    {
        var cfg = SlenderConfig.Default with
        {
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec> { [(ushort)HookButton.Back] = new ActionSpec.ToggleThumbWheelMode() })],
        };
        var r = new Rig(cfg);
        r.Hook.Button(HookButton.Back, true);
        r.Hook.Button(HookButton.Back, false);
        Assert.True(r.Engine.IsThumbWheelSecond());
        r.Engine.RunRingAction(new ActionSpec.ToggleThumbWheelMode());
        Assert.False(r.Engine.IsThumbWheelSecond());
        Assert.DoesNotContain(r.Exec.Calls, c => c.Item1 is ActionSpec.ToggleThumbWheelMode);
    }

    [Fact]
    public void Flip_bumps_when_haptics_allow()
    {
        var on = Cfg() with { Haptics = new HapticSettings(true, 2, true, true) };
        var r = new Rig(on);
        r.Flip();
        Assert.Single(r.Mouse.Haptics);
        var off = new Rig(Cfg() with { Haptics = new HapticSettings(false) });
        off.Flip();
        Assert.Empty(off.Mouse.Haptics);
    }
}
