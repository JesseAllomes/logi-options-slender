using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

/// <summary>An executor that also sends raw key events (the real one does; FakeExecutor does not, so it keeps the old path).</summary>
internal sealed class FakeKeyExecutor : IActionExecutor
{
    public List<(ActionSpec, bool)> Calls { get; } = [];
    public List<KeyEvent> Events { get; } = [];
    public bool Fail;
    public void Execute(ActionSpec a, bool pressed) => Calls.Add((a, pressed));
    public bool SupportsKeyEvents => true;
    public bool SendKeyEvents(IReadOnlyList<KeyEvent> events)
    {
        Events.AddRange(events);
        return !Fail;
    }
    public List<int> SideScrolls { get; } = [];
    public bool SendHorizontalWheel(int delta)
    {
        lock (SideScrolls) SideScrolls.Add(delta);
        return true;
    }
}

public class WheelKeyHoldTests
{
    private static readonly KeyStroke Ctrl = new(0xA2, false);
    private static readonly KeyStroke Shift = new(0xA0, false);
    private static readonly KeyStroke Alt = new(0xA4, false);
    private static readonly KeyStroke Tab = new(0x09, false);

    private static IReadOnlyList<KeyStroke> Keys(string chord)
    {
        Assert.True(ChordParser.TryParse(chord, out var k));
        return k;
    }

    [Fact]
    public void Shared_modifiers_are_the_ones_both_directions_use()
    {
        Assert.Equal([Ctrl], WheelKeyHold.SharedModifiers("Ctrl+Tab", "Ctrl+Shift+Tab"));
        Assert.Equal([Ctrl], WheelKeyHold.SharedModifiers("Ctrl+PageDown", "Ctrl+PageUp"));
        Assert.Equal([Ctrl], WheelKeyHold.SharedModifiers("Ctrl+Tab", null));
        Assert.Null(WheelKeyHold.SharedModifiers("VolumeUp", "VolumeDown"));
        Assert.Null(WheelKeyHold.SharedModifiers("Ctrl+Tab", "Alt+Left"));
        Assert.Null(WheelKeyHold.SharedModifiers("Enter, Ctrl+W", null));
        Assert.Null(WheelKeyHold.SharedModifiers("Ctrl+Tab", "Bogus+Key"));
    }

    [Fact]
    public void First_step_presses_the_modifier_later_steps_send_only_the_key()
    {
        var h = new WheelKeyHold();

        Assert.Equal([new KeyEvent(Ctrl, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true)], h.Step([Ctrl], Keys("Ctrl+Tab"), 1));
        Assert.Equal([new KeyEvent(Tab, false), new KeyEvent(Tab, true), new KeyEvent(Tab, false), new KeyEvent(Tab, true)], h.Step([Ctrl], Keys("Ctrl+Tab"), 2));
        Assert.Equal([Ctrl], h.Held);
    }

    [Fact]
    public void Back_step_adds_its_own_modifier_around_the_key_only()
    {
        var h = new WheelKeyHold();
        h.Step([Ctrl], Keys("Ctrl+Tab"), 1);

        Assert.Equal(
            [new KeyEvent(Shift, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true), new KeyEvent(Shift, true)],
            h.Step([Ctrl], Keys("Ctrl+Shift+Tab"), 1));
    }

    [Fact]
    public void A_new_shared_set_releases_what_is_no_longer_shared_first()
    {
        var h = new WheelKeyHold();
        h.Step([Ctrl], Keys("Ctrl+Tab"), 1);

        IReadOnlyList<KeyEvent> e = h.Step([Alt], Keys("Alt+Tab"), 1);
        Assert.Equal([new KeyEvent(Ctrl, true), new KeyEvent(Alt, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true)], e);
        Assert.Equal([Alt], h.Held);
    }

    [Fact]
    public void Release_lets_go_in_reverse_order_and_only_once()
    {
        var h = new WheelKeyHold();
        h.Step([Ctrl, Alt], Keys("Ctrl+Alt+Tab"), 1);

        Assert.Equal([new KeyEvent(Alt, true), new KeyEvent(Ctrl, true)], h.Release());
        Assert.False(h.IsHolding);
        Assert.Empty(h.Release());
    }
}

public class WheelHeldModifierEngineTests
{
    private static readonly KeyStroke Ctrl = new(0xA2, false);
    private static readonly KeyStroke Shift = new(0xA0, false);
    private static readonly KeyStroke Tab = new(0x09, false);
    private static readonly KeyStroke PageDown = new(0x22, true);

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

        public Rig(SlenderConfig cfg, string? process = "msedge")
        {
            Fg.ProcessName = process;
            Engine = new SlenderEngine(Mouse, Hook, Fg, Excel, Exec, cfg, new ListLog(), new InlineQueue(), () => Now, Timer);
            Engine.Start();
        }

        public void Hold() => Mouse.Press(ControlId.ModeShift, true);
        public void Release() => Mouse.Press(ControlId.ModeShift, false);

        public bool CtrlHeld()
        {
            var down = false;
            foreach (var e in Exec.Events.Where(e => e.Key == Ctrl)) down = !e.Up;
            return down;
        }
    }

    private static SlenderConfig Cfg(params Profile[] extra) => SlenderConfig.Default with
    {
        Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = ThumbWheelModes.Tabs }, .. extra],
    };

    [Fact]
    public void Tab_mode_holds_ctrl_and_taps_tab_per_notch()
    {
        var r = new Rig(Cfg());
        r.Hold();
        r.Exec.Events.Clear();

        r.Hook.Wheel(120);
        r.Now = 10;
        r.Hook.Wheel(240);
        r.Now = 20;
        r.Hook.Wheel(-120);

        Assert.Equal(
        [
            new KeyEvent(Ctrl, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true),
            new KeyEvent(Tab, false), new KeyEvent(Tab, true), new KeyEvent(Tab, false), new KeyEvent(Tab, true),
            new KeyEvent(Shift, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true), new KeyEvent(Shift, true),
        ], r.Exec.Events);
        Assert.DoesNotContain(r.Exec.Calls, c => c.Item1 is ActionSpec.Keys);
        Assert.True(r.CtrlHeld());
    }

    [Fact]
    public void Ctrl_stays_down_while_the_top_button_is_held()
    {
        var r = new Rig(Cfg());
        r.Hold();
        r.Hook.Wheel(120);

        r.Now = 1_000; // the wheel is quiet, but the button is still down
        r.Timer.Elapse();
        Assert.True(r.CtrlHeld());

        r.Release();
        Assert.False(r.CtrlHeld());
        Assert.Equal(new KeyEvent(Ctrl, true), r.Exec.Events[^1]);
    }

    [Fact]
    public void A_button_press_releases_ctrl_before_it_acts()
    {
        var r = new Rig(Cfg());
        r.Hold();
        r.Hook.Wheel(120);
        Assert.True(r.CtrlHeld());

        r.Mouse.Press(ControlId.Gesture, true); // another button: Ctrl must be up before it acts
        Assert.False(r.CtrlHeld());
    }

    [Fact]
    public void Switching_apps_releases_ctrl()
    {
        var r = new Rig(Cfg());
        r.Hold();
        r.Hook.Wheel(120);

        r.Fg.Switch("notepad");
        Assert.False(r.CtrlHeld());
    }

    [Fact]
    public void Dispose_never_leaves_ctrl_down()
    {
        var r = new Rig(Cfg());
        r.Hold();
        r.Hook.Wheel(120);

        r.Engine.Dispose();
        Assert.False(r.CtrlHeld());
    }

    [Fact]
    public void Excel_sheets_use_held_ctrl_and_pagedown_not_com()
    {
        var sheets = new ThumbWheelSecond(true, new ActionSpec.Excel(ExcelCommand.NextSheet), new ActionSpec.Excel(ExcelCommand.PreviousSheet));
        var r = new Rig(Cfg(new Profile("excel", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = sheets }), "excel");
        r.Hold();
        r.Exec.Events.Clear();

        r.Hook.Wheel(240);

        Assert.Equal(
            [new KeyEvent(Ctrl, false), new KeyEvent(PageDown, false), new KeyEvent(PageDown, true), new KeyEvent(PageDown, false), new KeyEvent(PageDown, true)],
            r.Exec.Events);
        Assert.Empty(r.Excel.Commands);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void A_pair_with_nothing_shared_keeps_whole_chords()
    {
        var volume = new ThumbWheelSecond(true, new ActionSpec.Keys("VolumeUp"), new ActionSpec.Keys("VolumeDown"));
        var r = new Rig(Cfg(new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = volume }));
        r.Hold();
        r.Exec.Events.Clear();

        r.Hook.Wheel(120);

        Assert.Empty(r.Exec.Events);
        Assert.Contains(r.Exec.Calls, c => c.Item1 is ActionSpec.Keys { Chord: "VolumeUp" } && c.Item2);
    }

    [Fact]
    public void A_non_key_back_action_runs_with_ctrl_released()
    {
        var mixed = new ThumbWheelSecond(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Launch("calc.exe"));
        var r = new Rig(Cfg(new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = mixed }));
        r.Hold();
        r.Hook.Wheel(120);
        Assert.True(r.CtrlHeld());

        r.Now = 10;
        r.Hook.Wheel(-120);

        Assert.False(r.CtrlHeld());
        Assert.Contains(r.Exec.Calls, c => c.Item1 is ActionSpec.Launch && c.Item2);
    }

    [Fact]
    public void Thumb_wheel_keys_binding_holds_shared_modifiers_too()
    {
        var bound = new Profile("msedge", new Dictionary<ushort, ActionSpec> { [SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Keys("Ctrl+Tab") });
        var r = new Rig(Cfg(bound));
        r.Exec.Events.Clear();

        r.Hook.Wheel(120);
        r.Now = 10;
        r.Hook.Wheel(120);

        Assert.Equal(
            [new KeyEvent(Ctrl, false), new KeyEvent(Tab, false), new KeyEvent(Tab, true), new KeyEvent(Tab, false), new KeyEvent(Tab, true)],
            r.Exec.Events);
    }

    [Fact]
    public void A_failed_send_lets_go_of_ctrl()
    {
        var r = new Rig(Cfg());
        r.Hold();
        r.Exec.Fail = true;

        r.Hook.Wheel(120);

        Assert.False(r.CtrlHeld());
    }
}
