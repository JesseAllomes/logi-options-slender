using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class TabScrollChordTests
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

        public bool CtrlHeld()
        {
            var down = false;
            foreach (var e in Exec.Events.Where(e => e.Key == Ctrl)) down = !e.Up;
            return down;
        }
    }

    private static SlenderConfig WithPointer(PointerSettings p, params Profile[] profiles) =>
        SlenderConfig.Default with { Pointer = p, Profiles = profiles.Length > 0 ? profiles : SlenderConfig.Default.Profiles };

    [Fact]
    public void Holding_left_and_right_turns_the_thumb_wheel_into_tab_steps_with_ctrl_held()
    {
        var r = new Rig();
        r.Hook.Chord(true);

        Assert.True(r.Hook.Wheel(120).Handled);
        r.Now = 10;
        r.Hook.Wheel(120);
        r.Now = 20;
        r.Hook.Wheel(-120);

        Assert.Equal(1, r.Exec.Events.Count(e => e.Key == Ctrl && !e.Up));
        Assert.Equal(3, r.Tabs());
        Assert.Equal(1, r.Tabs(shift: true));
        Assert.True(r.CtrlHeld());
    }

    [Fact]
    public void Ctrl_stays_down_while_the_buttons_are_held_and_goes_up_with_them()
    {
        var r = new Rig();
        r.Hook.Chord(true);
        r.Hook.Wheel(120);

        r.Now = 5_000; // the wheel has been still a long time, but the buttons are still held
        r.Timer.Elapse();
        Assert.True(r.CtrlHeld());

        r.Hook.Chord(false);
        Assert.False(r.CtrlHeld());
    }

    [Fact]
    public void Without_the_chord_the_wheel_side_scrolls_natively()
    {
        var r = new Rig();
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Exec.Events);
        Assert.Empty(r.Exec.SideScrolls);
    }

    [Fact]
    public void A_missing_back_direction_falls_back_to_previous_tab()
    {
        var forwardOnly = new ThumbWheelSecond(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.None());
        var cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = forwardOnly }] };
        var r = new Rig(cfg);
        r.Hook.Chord(true);

        r.Hook.Wheel(-120);

        Assert.Equal(1, r.Tabs(shift: true));
    }

    [Fact]
    public void Excel_tab_scroll_uses_its_sheet_pair()
    {
        var sheets = new ThumbWheelSecond(true, new ActionSpec.Excel(ExcelCommand.NextSheet), new ActionSpec.Excel(ExcelCommand.PreviousSheet));
        var cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()), new Profile("excel", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = sheets }] };
        var r = new Rig(cfg, "excel");
        r.Hook.Chord(true);

        r.Hook.Wheel(120);

        Assert.Contains(new KeyEvent(new KeyStroke(0x22, true), false), r.Exec.Events); // PageDown with Ctrl held
        Assert.Empty(r.Excel.Commands);
    }

    [Theory]
    [InlineData(200, 120, 2)]
    [InlineData(50, 120, 0)]
    [InlineData(50, 240, 1)]
    [InlineData(100, 120, 1)]
    public void Tab_scroll_speed_scales_tabs_per_turn(int speed, int delta, int tabs)
    {
        var r = new Rig(WithPointer(new PointerSettings(TabScrollSpeed: speed)));
        r.Hook.Chord(true);

        r.Hook.Wheel(delta);

        Assert.Equal(tabs, r.Tabs());
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
    public void The_chord_switch_reaches_the_hook()
    {
        var r = new Rig(WithPointer(new PointerSettings(TabScrollChord: false)));
        Assert.False(r.Hook.ChordEnabled);
        r.Engine.ApplyConfig(WithPointer(new PointerSettings(TabScrollChord: true)));
        Assert.True(r.Hook.ChordEnabled);
    }

    [Fact]
    public void Excel_gets_the_normal_side_scroll_now_columns_are_off()
    {
        var r = new Rig(SlenderConfig.Default with { Excel = new ExcelSettings(true, 3) }, "excel");

        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Excel.Scrolls);
    }
}
