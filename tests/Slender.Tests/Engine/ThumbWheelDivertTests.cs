using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class ThumbWheelDivertTests
{
    private sealed class ThumbMouse : ILogiMouse
    {
        public string Name => "fake";
        public bool IsConnected { get; set; } = true;
        public bool Accept = true;
        public bool? Touch { get; set; } = true;
        public List<bool> Calls { get; } = [];
        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<ButtonEvent>? ButtonChanged;
        public void Press(ushort id, bool pressed) => ButtonChanged?.Invoke(this, new ButtonEvent(id, pressed));
        public event EventHandler<ThumbWheelEvent>? ThumbWheelMoved;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> controlIds, CancellationToken ct) => Task.CompletedTask;
        public Task SetThumbWheelDivertedAsync(bool diverted, CancellationToken ct) => TrySetThumbWheelDivertedAsync(diverted, ct);
        public Task<bool> TrySetThumbWheelDivertedAsync(bool diverted, CancellationToken ct)
        {
            lock (Calls) Calls.Add(diverted);
            return Task.FromResult(Accept);
        }
        public bool? ThumbWheelTouchSupported => Touch;
        public Task PlayHapticAsync(byte waveform, CancellationToken ct) => Task.CompletedTask;
        public Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct) => Task.FromResult<BatteryStatus?>(null);
        public void Dispose() { }
        public void Report(ThumbWheelEvent e) => ThumbWheelMoved?.Invoke(this, e);
        /// <summary>A diverted report for <paramref name="units"/> of 1/120 notch (sent as device counts, ThumbWheelUnits.PerNotch per notch).</summary>
        public void Rotate(int units, bool touch = false) => ThumbWheelMoved?.Invoke(this, new ThumbWheelEvent(units * ThumbWheelUnits.PerNotch / 120, touch, touch, false));
        public void Connect(bool c) { IsConnected = c; ConnectionChanged?.Invoke(this, c); }
        public bool[] Snapshot() { lock (Calls) return [.. Calls]; }
    }

    private sealed class Rig
    {
        public ThumbMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExcel Excel = new();
        public FakeExecutor Exec = new();
        public SlenderEngine Engine;
        public long Now = 1000;
        public (int X, int Y) Cursor = (500, 500); // the real pointer never decides a test
        public Rig(SlenderConfig? cfg = null, string? process = "excel", Action<ThumbMouse>? setup = null, IActionExecutor? executor = null)
        {
            setup?.Invoke(Mouse);
            Fg.ProcessName = process;
            Engine = new SlenderEngine(Mouse, Hook, Fg, Excel, executor ?? Exec, cfg ?? SlenderConfig.Default, new ListLog(), new InlineQueue(), () => Now)
            {
                ForegroundDebounceMs = 0,
                ExcelColumnScrolling = true, // the dormant Excel column mode stays tested
            };
            Engine.CursorPosition = () => Cursor;
            Engine.Start();
        }
        public string[] Keys() => Exec.Calls.Where(c => c.Item1 is ActionSpec.Keys && c.Item2).Select(c => ((ActionSpec.Keys)c.Item1).Chord).ToArray();
        public int Columns() => Excel.Scrolls.Sum(s => s.Item2);
    }

    private static bool Until(Func<bool> cond)
    {
        for (var i = 0; i < 300; i++)
        {
            if (cond()) return true;
            Thread.Sleep(10);
        }
        return cond();
    }

    private static SlenderConfig WithDefault(Dictionary<ushort, ActionSpec> bindings, params Profile[] extra) =>
        SlenderConfig.Default with { Profiles = [new Profile(null, bindings), .. extra] };

    // ---- decision ----

    [Fact]
    public void Policy_follows_app_and_state()
    {
        var cfg = SlenderConfig.Default;
        Assert.True(ThumbWheelDivertPolicy.Wanted(cfg, "excel", false, true, excelColumns: true)); // Excel column mode (when on)
        Assert.False(ThumbWheelDivertPolicy.Wanted(cfg, "excel", false, true)); // off for now: Excel keeps the native side scroll
        Assert.False(ThumbWheelDivertPolicy.Wanted(cfg, "notepad", false, true)); // nothing needs it
        Assert.True(ThumbWheelDivertPolicy.Wanted(cfg, "notepad", true, true));   // ring open
        var off = cfg with { Excel = cfg.Excel with { ThumbWheelScrollsColumns = false } };
        Assert.False(ThumbWheelDivertPolicy.Wanted(off, "excel", false, true));
    }

    [Fact]
    public void Policy_bound_thumb_wheel_only_in_that_app()
    {
        var cfg = WithDefault([], new Profile("spotify", new Dictionary<ushort, ActionSpec> { [SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Keys("VolumeUp|VolumeDown") }));
        Assert.True(ThumbWheelDivertPolicy.Wanted(cfg, "spotify", false, true));
        Assert.False(ThumbWheelDivertPolicy.Wanted(cfg, "notepad", false, true));
    }

    [Fact]
    public void Policy_touch_and_tap_bindings_need_touch_support()
    {
        var tap = WithDefault(new() { [SlenderEngine.ThumbWheelTapBindingId] = new ActionSpec.Keys("Ctrl+Z") });
        var touch = WithDefault(new() { [SlenderEngine.ThumbWheelTouchBindingId] = new ActionSpec.Keys("Ctrl+Y") });
        Assert.True(ThumbWheelDivertPolicy.Wanted(tap, "notepad", false, true));
        Assert.True(ThumbWheelDivertPolicy.Wanted(tap, "notepad", false, null));
        Assert.False(ThumbWheelDivertPolicy.Wanted(tap, "notepad", false, false));
        Assert.True(ThumbWheelDivertPolicy.Wanted(touch, "notepad", false, true));
        Assert.False(ThumbWheelDivertPolicy.Wanted(SlenderConfig.Default, "notepad", false, false, true));
    }

    [Fact]
    public void Engine_diverts_in_excel_and_returns_to_native_elsewhere()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Mouse.Snapshot() is [true]));
        Assert.True(r.Engine.ThumbWheelDiverted);
        r.Fg.Switch("notepad");
        Assert.True(Until(() => r.Mouse.Snapshot() is [true, false]));
        Assert.False(r.Engine.ThumbWheelDiverted);
        r.Fg.Switch("notepad"); // no change: no extra call
        Thread.Sleep(50);
        Assert.Equal(2, r.Mouse.Snapshot().Length);
    }

    [Fact]
    public void Unchanged_need_never_touches_the_device()
    {
        var r = new Rig(process: "notepad");
        r.Fg.Switch("calc");
        Thread.Sleep(50);
        Assert.Empty(r.Mouse.Snapshot());
    }

    // ---- debounce / mid rotation ----

    [Fact]
    public void Never_switches_while_rotating_waits_for_a_pause()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Mouse.Snapshot().Length == 1));
        r.Mouse.Rotate(120);                  // turning at t=1000
        r.Now = 1050;
        r.Fg.Switch("notepad");               // wants native, but the wheel just moved
        Thread.Sleep(150);
        Assert.Single(r.Mouse.Snapshot());
        r.Now = 1100;
        r.Mouse.Rotate(120);                  // still turning: the wait restarts
        Thread.Sleep(100);
        Assert.Single(r.Mouse.Snapshot());
        r.Now = 1300;                         // 200 ms pause
        Assert.True(Until(() => r.Mouse.Snapshot() is [true, false]));
    }

    [Fact]
    public void A_wheel_that_never_pauses_is_switched_after_the_ten_second_cap()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Mouse.Snapshot().Length == 1));
        r.Mouse.Rotate(120);
        r.Fg.Switch("notepad");
        Assert.Single(r.Mouse.Snapshot());
        r.Now = 1000 + SlenderEngine.HeldDivertWaitMs;
        r.Mouse.Rotate(120); // rotating right now, but the cap is up
        Assert.True(Until(() => r.Mouse.Snapshot() is [true, false]));
    }

    // ---- rotation scaling ----

    [Fact]
    public void Excel_columns_from_diverted_rotation_120_per_notch()
    {
        var r = new Rig(SlenderConfig.Default with { Excel = new ExcelSettings(true, 3) });
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(120);
        Assert.Equal(3, r.Columns());
        r.Excel.Scrolls.Clear();
        r.Mouse.Rotate(-20);
        Assert.Equal(0, r.Columns());  // 20/120 of a notch at 3 columns is half a column: waits
        r.Mouse.Rotate(-20);
        Assert.Equal(-1, r.Columns());
    }

    [Fact]
    public void Excel_partial_notches_accumulate()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(40);
        r.Mouse.Rotate(40);
        Assert.Equal(0, r.Columns());
        r.Mouse.Rotate(40);
        Assert.Equal(1, r.Columns());
    }

    [Fact]
    public void Bound_pair_runs_one_key_per_notch_in_the_rotation_direction()
    {
        var cfg = WithDefault([], new Profile("spotify", new Dictionary<ushort, ActionSpec> { [SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Keys("VolumeUp|VolumeDown") }));
        var r = new Rig(cfg, "spotify");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(240);
        r.Now = 1010;
        r.Mouse.Rotate(-120);
        Assert.Equal(["VolumeUp", "VolumeUp", "VolumeDown"], r.Keys());
    }

    /// <summary>Tabs as the second action, and a tap binding so the wheel is diverted (rotation comes from HID++).</summary>
    private static SlenderConfig HoldCfg() => SlenderConfig.Default with
    {
        Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec> { [SlenderEngine.ThumbWheelTapBindingId] = new ActionSpec.Keys("Ctrl+Z") }) { ThumbWheelAlt = ThumbWheelModes.Tabs }],
    };

    private static void HoldTop(Rig r, bool down)
    {
        r.Mouse.Press(ControlId.ModeShift, down);
        Assert.True(Until(() => r.Engine.SecondActionHeld == down));
    }

    [Fact]
    public void Second_action_runs_from_diverted_rotation_while_the_top_button_is_held()
    {
        var r = new Rig(HoldCfg(), "msedge");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        HoldTop(r, true);
        r.Mouse.Rotate(60);
        r.Mouse.Rotate(60);
        r.Now = 1010;
        r.Mouse.Rotate(-120);
        Assert.True(Until(() => r.Keys().Length == 2));
        Assert.Equal(["Ctrl+Tab", "Ctrl+Shift+Tab"], r.Keys());
    }

    [Fact]
    public void Holding_the_top_button_never_changes_the_wheel_divert()
    {
        var cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = ThumbWheelModes.Tabs }] };
        var r = new Rig(cfg, "notepad");
        HoldTop(r, true);
        Assert.True(r.Hook.Wheel(120).Handled); // the hook path carries the second action
        HoldTop(r, false);
        Assert.Empty(r.Mouse.Snapshot());
    }

    [Fact]
    public void Ring_steering_gets_diverted_rotation_and_nothing_else_does()
    {
        var r = new Rig(process: "notepad");
        var got = new List<int>();
        r.Engine.RingThumbWheel += (_, d) => got.Add(d);
        r.Engine.SetRingOpen(true);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(120);
        r.Mouse.Rotate(-60);
        Assert.Equal([120, -60], got);
        Assert.Equal(0, r.Columns());
        Assert.Empty(r.Exec.Calls);
        r.Now = 5000;
        r.Engine.SetRingOpen(false);
        Assert.True(Until(() => r.Mouse.Snapshot() is [true, false]));
    }

    // ---- fallback and double firing ----

    [Fact]
    public void Failed_divert_keeps_the_hook_path_working()
    {
        var r = new Rig(setup: m => m.Accept = false);
        Assert.True(Until(() => r.Mouse.Snapshot().Length == 1));
        Thread.Sleep(30);
        Assert.False(r.Engine.ThumbWheelDiverted);
        var e = r.Hook.Wheel(120);
        Assert.True(e.Handled);
        Assert.Equal(1, r.Columns());
    }

    [Fact]
    public void Diverted_wheel_does_not_fire_twice()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        var e = r.Hook.Wheel(120); // a stray native event is left to the OS
        Assert.False(e.Handled);
        Assert.Equal(0, r.Columns());
        r.Mouse.Rotate(120);
        Assert.Equal(1, r.Columns());
    }

    [Fact]
    public void A_native_turn_long_after_diverting_means_the_mouse_dropped_it_and_it_is_taken_again()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        int sends = r.Mouse.Snapshot().Length;

        r.Now = 5000; // well after the divert settled: the mouse must have dropped it
        var e = r.Hook.Wheel(120);
        Assert.True(e.Handled);          // the hook path handles this turn
        Assert.Equal(1, r.Columns());
        r.Now = 5400; // the wheel paused: the divert may change now
        Assert.True(Until(() => r.Mouse.Snapshot().Length > sends && r.Mouse.Snapshot()[^1])); // divert sent again
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));

        r.Now = 5500; // in flight right after the repair: left to Windows, never handled twice
        Assert.False(r.Hook.Wheel(120).Handled);
    }

    [Fact]
    public void Disconnect_falls_back_to_the_hook_and_reconnect_reapplies()
    {
        var r = new Rig();
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Connect(false);
        Assert.False(r.Engine.ThumbWheelDiverted);
        Assert.True(r.Hook.Wheel(120).Handled);
        r.Now = 2000;
        r.Mouse.Connect(true);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        Assert.Equal([true, true], r.Mouse.Snapshot());
    }

    // ---- touch and tap ----

    private static SlenderConfig TapCfg() => WithDefault(new()
    {
        [SlenderEngine.ThumbWheelTapBindingId] = new ActionSpec.Keys("Ctrl+Z"),
        [SlenderEngine.ThumbWheelTouchBindingId] = new ActionSpec.Keys("F13"),
    });

    // ---- unclaimed diverted turns become a normal sideways scroll ----

    [Fact]
    public void Releasing_the_top_button_mid_turn_keeps_side_scroll_working()
    {
        var exec = new FakeKeyExecutor();
        var r = new Rig(HoldCfg(), "msedge", executor: exec);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        HoldTop(r, true);
        r.Mouse.Rotate(120); // a tab step

        HoldTop(r, false); // back to side scroll, mid-turn
        r.Mouse.Rotate(120);
        r.Mouse.Rotate(-40);

        Assert.True(r.Engine.ThumbWheelDiverted);
        lock (exec.SideScrolls) Assert.Equal([120, -40], exec.SideScrolls);
        Assert.Equal(1, exec.Events.Count(e => e.Key.Vk == 0x09 && !e.Up)); // only the one tab step
    }

    [Fact]
    public void Holding_the_top_button_keeps_ctrl_held_when_the_touch_flag_drops_mid_turn()
    {
        var exec = new FakeKeyExecutor();
        var r = new Rig(HoldCfg(), "msedge", executor: exec);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        HoldTop(r, true);

        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Rotate(120, touch: true);
        r.Mouse.Rotate(120, touch: false); // touch flag drops while still turning
        r.Mouse.Rotate(120, touch: true);

        Assert.Equal(1, exec.Events.Count(e => e.Key.Vk == 0xA2 && !e.Up)); // Ctrl pressed once for the whole turn
        Assert.DoesNotContain(exec.Events, e => e.Key.Vk == 0xA2 && e.Up);
        Assert.Equal(3, exec.Events.Count(e => e.Key.Vk == 0x09 && !e.Up));
    }

    [Fact]
    public void Outside_tab_mode_lifting_the_thumb_releases_a_held_wheel_shortcut()
    {
        var cfg = WithDefault(new() { [SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Keys("Ctrl+Tab") });
        var exec = new FakeKeyExecutor();
        var r = new Rig(cfg, "notepad", executor: exec);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));

        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Rotate(120, touch: true);
        Assert.DoesNotContain(exec.Events, e => e.Key.Vk == 0xA2 && e.Up);
        r.Mouse.Rotate(0, touch: false); // thumb lifted

        Assert.Contains(exec.Events, e => e.Key.Vk == 0xA2 && e.Up);
    }

    [Fact]
    public void Touch_only_divert_still_side_scrolls()
    {
        var exec = new FakeKeyExecutor();
        var r = new Rig(TapCfg(), "notepad", executor: exec);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));

        r.Mouse.Rotate(240);

        lock (exec.SideScrolls) Assert.Equal([240], exec.SideScrolls);
    }

    [Fact]
    public void Claimed_or_hook_turns_are_never_passed_through()
    {
        var exec = new FakeKeyExecutor();
        var cols = new Rig(SlenderConfig.Default with { Excel = new ExcelSettings(true, 1) }, executor: exec);
        Assert.True(Until(() => cols.Engine.ThumbWheelDiverted));
        cols.Mouse.Rotate(120); // Excel columns own it
        Assert.Equal(1, cols.Columns());

        var off = new Rig(WithDefault(new() { [SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Disabled() }), "notepad", executor: exec);
        Assert.True(Until(() => off.Engine.ThumbWheelDiverted));
        off.Mouse.Rotate(120); // turned off: swallowed

        var native = new Rig(process: "notepad", executor: exec);
        Assert.False(native.Hook.Wheel(120).Handled); // native path: Windows already has it

        lock (exec.SideScrolls) Assert.Empty(exec.SideScrolls);
    }

    [Fact]
    public void Tap_binding_diverts_and_touch_then_release_runs_it()
    {
        var r = new Rig(TapCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(0, touch: true);
        r.Now = 1200;
        r.Mouse.Rotate(0, touch: false);
        // A quick touch is a tap: the rest action never fires (the thumb did not rest).
        Assert.Equal(["Ctrl+Z"], r.Keys());
    }

    [Fact]
    public void Resting_still_fires_the_rest_action_once_and_turning_cancels_it()
    {
        var r = new Rig(TapCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(0, touch: true);
        r.Now = 5000;
        Assert.True(Until(() => r.Keys().Contains("F13"))); // after RestMs still
        Assert.Contains((new ActionSpec.Keys("F13"), false), r.Exec.Calls.Select(c => (c.Item1, c.Item2))); // a tap, not held
        r.Mouse.Rotate(0, touch: false);
        r.Exec.Calls.Clear();

        r.Now = 9000;
        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Rotate(120, touch: true); // scrolling, not resting
        Thread.Sleep(new PointerSettings().TouchRestMs + 300);
        Assert.DoesNotContain("F13", r.Keys());
    }

    private static SlenderConfig DoubleCfg() => WithDefault(new()
    {
        [SlenderEngine.ThumbWheelTapBindingId] = new ActionSpec.Keys("Ctrl+Z"),
        [SlenderEngine.ThumbWheelDoubleTapBindingId] = new ActionSpec.Keys("Ctrl+Y"),
    });

    private static void Tap(Rig r, long at)
    {
        r.Now = at;
        r.Mouse.Rotate(0, touch: true);
        r.Now = at + 80;
        r.Mouse.Rotate(0, touch: false);
    }

    [Fact]
    public void Two_quick_taps_are_a_double_tap_and_the_single_tap_never_runs()
    {
        var r = new Rig(DoubleCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        Tap(r, 1000);
        Tap(r, 1200);
        Assert.True(Until(() => r.Keys().Contains("Ctrl+Y")));
        Thread.Sleep(new PointerSettings().DoubleTapMs + 200);
        Assert.Equal(["Ctrl+Y"], r.Keys());
    }

    [Fact]
    public void One_tap_waits_for_a_possible_second_then_runs()
    {
        var r = new Rig(DoubleCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        Tap(r, 1000);
        Assert.Empty(r.Keys()); // still waiting
        Assert.True(Until(() => r.Keys().Contains("Ctrl+Z")));
        Assert.Equal(["Ctrl+Z"], r.Keys());
    }

    [Fact]
    public void Double_tap_alone_diverts_the_wheel()
    {
        var cfg = WithDefault(new() { [SlenderEngine.ThumbWheelDoubleTapBindingId] = new ActionSpec.Keys("Ctrl+Y") });
        Assert.True(ThumbWheelDivertPolicy.Wanted(cfg, "notepad", false, true));
    }

    private sealed class GestureSink : IThumbPreviewSink
    {
        public readonly List<string> Seen = [];
        public void Rotated(int delta) { }
        public void TopButton(bool down) { }
        public void Touch(bool down) { lock (Seen) Seen.Add(down ? "touch" : "lift"); }
        public void Gesture(TouchGesture gesture) { lock (Seen) Seen.Add(gesture.ToString()); }
        public string[] Snapshot() { lock (Seen) return [.. Seen]; }
    }

    [Fact]
    public void Card_preview_diverts_the_wheel_and_reports_touch_rest_and_double_tap_without_running_them()
    {
        var r = new Rig(SlenderConfig.Default, "notepad"); // nothing bound: the tester still reports every gesture
        var sink = new GestureSink();
        r.Engine.SetPreview(sink);
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));

        r.Mouse.Rotate(0, touch: true);
        Assert.True(Until(() => sink.Snapshot().Contains("Rest")));
        r.Now = 5000; // a long rest: lifting is not a tap
        r.Mouse.Rotate(0, touch: false);
        Tap(r, 10_000);
        Tap(r, 10_200);
        Assert.True(Until(() => sink.Snapshot().Contains("DoubleTap")));
        Assert.Equal(["touch", "Rest", "lift", "touch", "lift", "touch", "lift", "DoubleTap"], sink.Snapshot());
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Grabbing_the_mouse_is_not_a_tap_or_a_rest()
    {
        var r = new Rig(DoubleCfg() with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>
        {
            [SlenderEngine.ThumbWheelTapBindingId] = new ActionSpec.Keys("Ctrl+Z"),
            [SlenderEngine.ThumbWheelTouchBindingId] = new ActionSpec.Keys("F13"),
        })] }, "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));

        r.Mouse.Rotate(0, touch: true); // the hand lands on the mouse and moves it
        r.Cursor = (560, 520);
        r.Now = 1100;
        r.Mouse.Rotate(0, touch: false);
        Thread.Sleep(new PointerSettings().TouchRestMs + 200);
        Assert.Empty(r.Keys());

        r.Cursor = (560, 520);
        r.Mouse.Rotate(0, touch: true); // resting while the hand also moves the mouse
        r.Cursor = (600, 600);
        Thread.Sleep(new PointerSettings().TouchRestMs + 200);
        Assert.DoesNotContain("F13", r.Keys());
    }

    [Fact]
    public void A_small_pointer_drift_still_taps()
    {
        var r = new Rig(TapCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(0, touch: true);
        r.Cursor = (510, 505);
        r.Now = 1100;
        r.Mouse.Rotate(0, touch: false);
        Assert.True(Until(() => r.Keys().Contains("Ctrl+Z")));
    }

    [Fact]
    public void A_wobble_under_one_wheel_step_still_rests()
    {
        var r = new Rig(TapCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Rotate(60, touch: true); // 3 counts: nothing would move on screen
        r.Now = 5000;
        Assert.True(Until(() => r.Keys().Contains("F13")));
    }

    [Fact]
    public void A_turn_end_report_does_not_lift_the_thumb()
    {
        var r = new Rig(TapCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Report(new ThumbWheelEvent(0, false, false, false, Status: 3)); // the device's "turn ended" says no touch
        r.Now = 5000;
        Assert.True(Until(() => r.Keys().Contains("F13"))); // still resting: the rest action fires
        Assert.DoesNotContain("Ctrl+Z", r.Keys());           // and no tap was seen
    }

    [Fact]
    public void Slow_or_turning_touch_is_not_a_tap()
    {
        var r = new Rig(TapCfg(), "notepad");
        Assert.True(Until(() => r.Engine.ThumbWheelDiverted));
        r.Mouse.Rotate(0, touch: true);
        r.Now = 1251;                    // 251 ms
        r.Mouse.Rotate(0, touch: false);
        Assert.DoesNotContain("Ctrl+Z", r.Keys());
        r.Now = 2000;
        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Rotate(120, touch: true); // a whole wheel step is a turn
        r.Now = 2100;
        r.Mouse.Rotate(0, touch: false);
        Assert.DoesNotContain("Ctrl+Z", r.Keys());
    }

    [Fact]
    public void Touch_never_runs_when_the_device_has_no_touch()
    {
        var r = new Rig(TapCfg(), "notepad", m => m.Touch = false);
        Thread.Sleep(50);
        Assert.Empty(r.Mouse.Snapshot());
        r.Mouse.Rotate(0, touch: true);
        r.Mouse.Rotate(0, touch: false);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Tracker_thresholds()
    {
        var t = new ThumbTouchTracker();
        Assert.Equal(new ThumbTouchResult(true, false, false), t.OnReport(true, 0, 0));
        Assert.Equal(new ThumbTouchResult(false, true, true), t.OnReport(false, 0, 250));
        t.OnReport(true, 0, 1000);
        Assert.Equal(new ThumbTouchResult(false, true, false), t.OnReport(false, 0, 1251));
        t.OnReport(true, 5, 2000);
        Assert.True(t.OnReport(false, 0, 2100).Tap);   // 5 < 6 counts (under one wheel step)
        t.OnReport(true, 3, 3000);
        t.OnReport(true, -3, 3010);
        Assert.False(t.OnReport(false, 0, 3050).Tap);  // 3 + 3 = 6
        Assert.False(t.OnReport(false, 0, 3060).Up);   // not touching: no edge
    }
}
