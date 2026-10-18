using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

/// <summary>Thumb wheel speeds: the second action (top button held) and the side scroll.</summary>
public class ScrollSpeedTests
{
    private static readonly KeyStroke Ctrl = new(0xA2, false);
    private static readonly KeyStroke Shift = new(0xA0, false);
    private static readonly KeyStroke Tab = new(0x09, false);

    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExcel Excel = new();
        public FakeKeyExecutor Exec = new();
        public FakeHoldTimer Timer = new();
        public SlenderEngine Engine;
        public long Now;

        public Rig(SlenderConfig? cfg = null, string? process = "notepad")
        {
            Fg.ProcessName = process;
            Engine = new SlenderEngine(Mouse, Hook, Fg, Excel, Exec, cfg ?? SlenderConfig.Default, new ListLog(), new InlineQueue(), () => Now, Timer);
            Engine.Start();
        }

        public int Tabs(bool shift = false) =>
            Exec.Events.Count(e => e.Key == Tab && !e.Up) is var n && !shift ? n : CountShiftTabs();

        private int CountShiftTabs()
        {
            int n = 0;
            bool shiftDown = false;
            foreach (var e in Exec.Events)
            {
                if (e.Key == Shift) shiftDown = !e.Up;
                if (e.Key == Tab && !e.Up && shiftDown) n++;
            }
            return n;
        }

        public void HoldTop(bool down) => Mouse.Press(ControlId.ModeShift, down);

        public bool CtrlHeld()
        {
            var down = false;
            foreach (var e in Exec.Events.Where(e => e.Key == Ctrl)) down = !e.Up;
            return down;
        }
    }

    private static SlenderConfig WithPointer(PointerSettings p, params Profile[] profiles) =>
        SlenderConfig.Default with { Pointer = p, Profiles = profiles.Length > 0 ? profiles : SlenderConfig.Default.Profiles };

    private static readonly ThumbWheelSecond EdgeTabs = new(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Keys("Ctrl+Shift+Tab"));

    private static SlenderConfig Held(PointerSettings? p = null, ThumbWheelSecond? second = null) => SlenderConfig.Default with
    {
        Pointer = p ?? new PointerSettings(),
        Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = second ?? EdgeTabs }],
    };

    [Fact]
    public void Turning_back_after_forward_presses_ctrl_again_with_shift()
    {
        var r = new Rig(Held(), "msedge");
        r.HoldTop(true);
        r.Hook.Wheel(120);
        r.Exec.Events.Clear();
        r.Now = 10;

        r.Hook.Wheel(-120);

        Assert.Equal(
        [
            new KeyEvent(Ctrl, true), new KeyEvent(Ctrl, false),
            new KeyEvent(Shift, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true), new KeyEvent(Shift, true),
        ], r.Exec.Events);
        Assert.True(r.CtrlHeld());
    }

    [Fact]
    public void Excel_sheet_pair_steps_with_ctrl_held()
    {
        var sheets = new ThumbWheelSecond(true, new ActionSpec.Excel(ExcelCommand.NextSheet), new ActionSpec.Excel(ExcelCommand.PreviousSheet));
        var r = new Rig(Held(second: sheets), "excel");
        r.HoldTop(true);

        r.Hook.Wheel(120);

        Assert.Contains(new KeyEvent(new KeyStroke(0x22, true), false), r.Exec.Events); // PageDown with Ctrl held
        Assert.Empty(r.Excel.Commands);
    }

    [Theory]
    [InlineData(200, 120, 2)]
    [InlineData(50, 120, 0)]
    [InlineData(50, 240, 1)]
    [InlineData(100, 120, 1)]
    public void Second_action_speed_scales_steps_per_turn(int speed, int delta, int tabs)
    {
        var r = new Rig(Held(new PointerSettings(SecondActionSpeed: speed)));
        r.HoldTop(true);

        r.Hook.Wheel(delta);

        Assert.Equal(tabs, r.Tabs());
    }

    [Fact]
    public void A_still_wheel_lets_ctrl_go_after_a_while_even_if_the_top_button_release_is_lost()
    {
        var r = new Rig(Held());
        r.HoldTop(true);
        r.Hook.Wheel(120);

        r.Now = 1_000; // still under the cap: Ctrl stays down through a pause
        r.Timer.Elapse();
        Assert.True(r.CtrlHeld());

        r.Now = 2_000; // the release never came: Ctrl goes up anyway
        r.Timer.Elapse();
        Assert.False(r.CtrlHeld());
    }

    [Fact]
    public void An_app_switch_ends_a_hold_whose_release_is_lost()
    {
        var r = new Rig(Held(), "msedge");
        r.HoldTop(true);
        r.Hook.Wheel(120);

        r.Fg.Switch("notepad");

        Assert.False(r.CtrlHeld());
        Assert.False(r.Engine.SecondActionHeld);
        Assert.False(r.Hook.Wheel(120).Handled); // normal side scroll again
    }

    [Fact]
    public void Side_scroll_speed_swallows_the_native_turn_and_sends_a_scaled_one()
    {
        var r = new Rig(WithPointer(new PointerSettings(SideScrollSpeed: 50)));

        Assert.True(r.Hook.Wheel(120).Handled);
        r.Hook.Wheel(30); // half of 30 each time
        r.Hook.Wheel(30);

        Assert.Equal([60, 15, 15], r.Exec.SideScrolls);
    }

    [Fact]
    public void Side_scroll_speed_carries_fractions()
    {
        var r = new Rig(WithPointer(new PointerSettings(SideScrollSpeed: 150)));
        r.Hook.Wheel(1);
        r.Hook.Wheel(1);

        Assert.Equal([1, 2], r.Exec.SideScrolls); // 1.5 then 1.5 + carried 0.5
    }

    [Fact]
    public void Excel_gets_the_normal_side_scroll_now_columns_are_off()
    {
        var r = new Rig(SlenderConfig.Default with { Excel = new ExcelSettings(true, 3) }, "excel");

        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Excel.Scrolls);
    }
}
