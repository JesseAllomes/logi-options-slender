using Slender.Engine;
using Slender.Engine.Macros;
using Slender.Hidpp;
using Slender.Tests.Engine;

namespace Slender.Tests.Macros;

/// <summary>Records what a macro sends; never touches the real input.</summary>
internal sealed class FakeMacroInput : IMacroInput
{
    public List<string> Log { get; } = [];
    public bool EscDown;
    public bool Fail;
    /// <summary>After this chord is sent Esc reads as down once (a macro's own Esc tap).</summary>
    public bool EscDownAfterEscChord;
    private readonly HashSet<string> _down = [];

    public IReadOnlySet<string> Down => _down;

    private static string Name(KeyStroke k) => $"{k.Vk:X2}";

    public bool KeyDown(KeyStroke key) { if (Fail) return false; Log.Add("kd " + Name(key)); _down.Add("k" + Name(key)); return true; }
    public bool KeyUp(KeyStroke key) { Log.Add("ku " + Name(key)); _down.Remove("k" + Name(key)); return true; }
    public bool Chord(IReadOnlyList<KeyStroke> keys)
    {
        if (Fail) return false;
        Log.Add("chord " + string.Join("+", keys.Select(Name)));
        if (EscDownAfterEscChord && keys.Any(k => k.Vk == 0x1B)) EscDown = true;
        return true;
    }
    public bool Type(IReadOnlyList<TextKey> keys) { Log.Add("type " + string.Concat(keys.Select(k => (char)k.Unit))); return true; }
    public bool MouseButton(string button, bool down)
    {
        if (Fail && down) return false;
        Log.Add($"mouse {button} {(down ? "down" : "up")}");
        if (down) _down.Add("m" + button); else _down.Remove("m" + button);
        return true;
    }
    public bool MoveBy(int dx, int dy) { Log.Add($"by {dx},{dy}"); return true; }
    public bool MoveTo(int x, int y) { Log.Add($"to {x},{y}"); return true; }
    public bool Wheel(int vertical, int horizontal) { Log.Add($"wheel {vertical},{horizontal}"); return true; }
    public void Launch(string target, string? arguments) => Log.Add($"launch {target} {arguments}");
    public bool EscapeHeld()
    {
        var down = EscDown;
        if (EscDownAfterEscChord) EscDown = false; // a one-shot read
        return down;
    }
}

/// <summary>Virtual time: Sleep advances it and runs the test's hook, so a test can press or release things at a chosen moment.</summary>
internal sealed class FakeMacroClock : IMacroClock
{
    public long NowMs { get; private set; }
    public Action<long>? OnSleep;
    public int Sleeps;
    public void Sleep(int ms) { Sleeps++; NowMs += ms; OnSleep?.Invoke(NowMs); }
}

public class MacroRunnerTests
{
    private sealed class Rig : IDisposable
    {
        public FakeMacroInput Input = new();
        public FakeMacroClock Clock = new();
        public ListLog Log = new();
        public MacroRunner Runner;
        public List<(string Id, MacroEnd End)> Ends = [];
        public List<Action> Spawned = [];

        public Rig(MacroDef macro, bool inline = true)
        {
            Runner = new MacroRunner(Input, Clock, Log, inline ? w => w() : Spawned.Add)
            {
                Lookup = id => id == macro.Id ? macro : null,
            };
            Runner.Ended += (id, end) => Ends.Add((id, end));
        }

        public void Dispose() => Runner.Dispose();
    }

    private static MacroDef Def(MacroRunMode mode, int cap = 60, params MacroStep[] steps) => new("m1", "Test", steps, mode, cap);

    [Fact]
    public void Once_runs_the_steps_in_order_with_the_pause_between()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 60,
            MacroStep.Tap("Ctrl+C"), MacroStep.Pause(500), MacroStep.Type("hi"), MacroStep.Click("Left"), MacroStep.MoveBy(5, -3), MacroStep.MoveTo(100, 200),
            MacroStep.Wheel(-2, 1), MacroStep.Launch("calc.exe", "/x")));
        r.Runner.Handle("m1", true);
        Assert.Equal(["chord A2+43", "type hi", "mouse Left down", "mouse Left up", "by 5,-3", "to 100,200", "wheel -240,120", "launch calc.exe /x"], r.Input.Log);
        Assert.InRange(r.Clock.NowMs, 500, 520); // the pause really waited, and no more than a slice over
        Assert.Equal([("m1", MacroEnd.Finished)], r.Ends);
        Assert.Equal(0, r.Runner.RunningCount);
    }

    [Fact]
    public void Everything_pressed_is_released_when_the_macro_ends()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 60,
            MacroStep.KeyDown("Ctrl"), MacroStep.KeyDown("Shift"), MacroStep.MouseDown("Left"), MacroStep.Pause(100)));
        r.Runner.Handle("m1", true);
        Assert.Empty(r.Input.Down);
        Assert.Equal(["kd A2", "kd A0", "mouse Left down", "ku A0", "ku A2", "mouse Left up"], r.Input.Log); // last pressed, first released
    }

    [Fact]
    public void A_key_the_macro_lifted_itself_is_not_lifted_twice()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 60, MacroStep.KeyDown("A"), MacroStep.KeyUp("A")));
        r.Runner.Handle("m1", true);
        Assert.Equal(["kd 41", "ku 41"], r.Input.Log);
    }

    [Fact]
    public void A_second_press_ends_a_toggle_macro_and_it_lets_go()
    {
        using var r = new Rig(Def(MacroRunMode.Toggle, 60, MacroStep.KeyDown("Shift"), MacroStep.Click("Left"), MacroStep.Pause(100)));
        r.Clock.OnSleep = now => { if (now >= 1000) r.Runner.Handle("m1", true); };
        r.Runner.Handle("m1", true);
        Assert.Equal([("m1", MacroEnd.Pressed)], r.Ends);
        Assert.InRange(r.Input.Log.Count(l => l == "mouse Left down"), 5, 12); // it really looped
        Assert.Empty(r.Input.Down);
        Assert.Equal("ku A0", r.Input.Log[^1]);
    }

    [Fact]
    public void A_release_does_not_end_a_toggle_macro()
    {
        using var r = new Rig(Def(MacroRunMode.Toggle, 1, MacroStep.Click("Left"), MacroStep.Pause(100)), inline: false);
        r.Runner.Handle("m1", true);
        r.Runner.Handle("m1", false);
        r.Clock.OnSleep = now => { if (now >= 300) r.Runner.Handle("m1", false); };
        r.Spawned.Single()();
        Assert.Equal(MacroEnd.TimeCap, r.Ends.Single().End); // only the cap ended it
    }

    [Fact]
    public void Esc_ends_a_running_macro()
    {
        using var r = new Rig(Def(MacroRunMode.Toggle, 60, MacroStep.MouseDown("Right"), MacroStep.Pause(100), MacroStep.MouseUp("Right"), MacroStep.Pause(100)));
        r.Clock.OnSleep = now => r.Input.EscDown = now >= 450;
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Escape, r.Ends.Single().End);
        Assert.InRange(r.Clock.NowMs, 450, 480);
        Assert.Empty(r.Input.Down);
    }

    [Fact]
    public void Esc_already_down_at_the_start_does_not_stop_the_macro_until_it_was_let_go()
    {
        // A control bound to Esc starts the macro with Esc still down: that same press must not end it.
        using var r = new Rig(Def(MacroRunMode.Toggle, 60, MacroStep.Click("Left"), MacroStep.Pause(100)));
        r.Input.EscDown = true;
        r.Clock.OnSleep = now => r.Input.EscDown = now >= 400; // let go at the first sleep, pressed again at 400: this one stops it
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Escape, r.Ends.Single().End);
        Assert.True(r.Clock.NowMs >= 400);
    }

    [Fact]
    public void The_macros_own_Esc_press_is_not_taken_for_the_user()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 60, MacroStep.Tap("Esc"), MacroStep.Pause(100), MacroStep.Type("after")));
        r.Input.EscDownAfterEscChord = true;
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Finished, r.Ends.Single().End);
        Assert.Contains("type after", r.Input.Log);
    }

    [Fact]
    public void The_time_cap_stops_a_runaway_toggle_macro()
    {
        using var r = new Rig(Def(MacroRunMode.Toggle, 2, MacroStep.KeyDown("Ctrl"), MacroStep.Pause(50)));
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.TimeCap, r.Ends.Single().End);
        Assert.InRange(r.Clock.NowMs, 2000, 2060);
        Assert.Empty(r.Input.Down);
    }

    [Fact]
    public void The_cap_also_cuts_a_very_long_pause()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 1, MacroStep.MouseDown("Left"), MacroStep.Pause(60_000), MacroStep.Type("never")));
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.TimeCap, r.Ends.Single().End);
        Assert.DoesNotContain("type never", r.Input.Log);
        Assert.Empty(r.Input.Down);
    }

    [Fact]
    public void A_toggle_macro_with_no_pauses_never_spins_flat_out()
    {
        using var r = new Rig(Def(MacroRunMode.Toggle, 1, MacroStep.Click("Left")));
        r.Runner.Handle("m1", true);
        Assert.InRange(r.Input.Log.Count(l => l == "mouse Left down"), 1, 1000 / MacroStep.PauseMinMs + 1); // at most one pass per shortest pause
    }

    [Fact]
    public void WhileHeld_repeats_until_release_then_finishes_the_pass()
    {
        using var r = new Rig(Def(MacroRunMode.WhileHeld, 60, MacroStep.Tap("A"), MacroStep.Pause(100), MacroStep.Tap("B")));
        r.Clock.OnSleep = now => { if (now >= 450) r.Runner.Handle("m1", false); };
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Finished, r.Ends.Single().End);
        // every pass that started ended with its B: a release never cuts a pass in half
        Assert.Equal(r.Input.Log.Count(l => l == "chord 41"), r.Input.Log.Count(l => l == "chord 42"));
        Assert.True(r.Input.Log.Count(l => l == "chord 41") >= 4);
    }

    [Fact]
    public void WhileHeld_released_at_once_runs_exactly_one_pass()
    {
        // A ring slot or a tap presses and releases in the same instant, before the macro's thread even starts.
        using var r = new Rig(Def(MacroRunMode.WhileHeld, 60, MacroStep.Tap("A"), MacroStep.Pause(100)), inline: false);
        r.Runner.Handle("m1", true);
        r.Runner.Handle("m1", false);
        r.Spawned.Single()();
        Assert.Equal(["chord 41"], r.Input.Log);
        Assert.Equal(MacroEnd.Finished, r.Ends.Single().End);
    }

    [Fact]
    public void A_failed_send_stops_the_macro_and_still_lets_go()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 60, MacroStep.KeyDown("Shift"), MacroStep.MouseDown("Left"), MacroStep.Type("x")));
        r.Clock.OnSleep = _ => { };
        r.Input.Fail = false;
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Finished, r.Ends.Single().End); // sanity: works without the failure

        using var f = new Rig(Def(MacroRunMode.Once, 60, MacroStep.KeyDown("Shift"), MacroStep.Pause(10), MacroStep.MouseDown("Left"), MacroStep.Type("x")));
        f.Clock.OnSleep = _ => f.Input.Fail = true; // the target goes away after the Shift is down
        f.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Failed, f.Ends.Single().End);
        Assert.Single(f.Log.Errors);
        Assert.DoesNotContain("type x", f.Input.Log);
        Assert.Empty(f.Input.Down);
    }

    [Fact]
    public void An_unknown_macro_is_logged_and_nothing_runs()
    {
        using var r = new Rig(Def(MacroRunMode.Once, 60, MacroStep.Type("x")));
        r.Runner.Handle("other", true);
        Assert.Empty(r.Input.Log);
        Assert.Single(r.Log.Errors);
        Assert.Equal(0, r.Runner.RunningCount);
    }

    [Fact]
    public void An_empty_macro_finishes_at_once_even_in_toggle_mode()
    {
        using var r = new Rig(Def(MacroRunMode.Toggle, 60));
        r.Runner.Handle("m1", true);
        Assert.Equal(MacroEnd.Finished, r.Ends.Single().End);
        Assert.Equal(0, r.Clock.Sleeps);
    }

    [Fact]
    public void At_most_four_macros_run_at_once()
    {
        var steps = new[] { MacroStep.Pause(100) };
        var macros = Enumerable.Range(0, 6).Select(i => new MacroDef("m" + i, "M" + i, steps)).ToList();
        var spawned = new List<Action>();
        var runner = new MacroRunner(new FakeMacroInput(), new FakeMacroClock(), new ListLog(), spawned.Add) { Lookup = id => macros.FirstOrDefault(m => m.Id == id) };
        foreach (var m in macros) runner.Handle(m.Id, true);
        Assert.Equal(MacroRunner.MaxRunning, spawned.Count);
        Assert.Equal(MacroRunner.MaxRunning, runner.RunningCount);
        runner.StopAll();
        foreach (var run in spawned) run();
        Assert.Equal(0, runner.RunningCount);
    }

    [Fact]
    public void StopAll_ends_running_macros_and_a_disposed_runner_ignores_presses()
    {
        var input = new FakeMacroInput();
        var def = Def(MacroRunMode.Toggle, 60, MacroStep.KeyDown("Ctrl"), MacroStep.Pause(100));
        var spawned = new List<Action>();
        var runner = new MacroRunner(input, new FakeMacroClock(), new ListLog(), spawned.Add) { Lookup = _ => def };
        runner.Handle("m1", true);
        runner.StopAll();
        spawned.Single()();
        Assert.Empty(input.Down);
        Assert.Equal(0, runner.RunningCount);
        runner.Dispose();
        input.Log.Clear();
        runner.Handle("m1", true);
        Assert.Single(spawned); // nothing new started
    }

    [Fact]
    public void The_executor_hands_presses_and_releases_to_the_macro_host()
    {
        var host = new RecordingHost();
        var ex = new ActionExecutor(new FakeExcel(), new FakeForeground()) { Macros = host };
        ex.Execute(new ActionSpec.Macro("abc"), true);
        ex.Execute(new ActionSpec.Macro("abc"), false);
        Assert.Equal([("abc", true), ("abc", false)], host.Calls);
    }

    [Fact]
    public void Without_a_macro_host_the_executor_logs_instead_of_throwing()
    {
        var log = new ListLog();
        var ex = new ActionExecutor(new FakeExcel(), new FakeForeground(), log);
        ex.Execute(new ActionSpec.Macro("abc"), true);
        ex.Execute(new ActionSpec.Macro("abc"), false);
        Assert.Single(log.Errors);
    }

    private sealed class RecordingHost : IMacroHost
    {
        public List<(string, bool)> Calls { get; } = [];
        public void Handle(string macroId, bool pressed) => Calls.Add((macroId, pressed));
        public void StopAll() { }
    }

    [Fact]
    public void A_bound_button_sends_press_and_release_to_the_executor()
    {
        var exec = new FakeExecutor();
        var cfg = SlenderConfig.Default with
        {
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.Back] = new ActionSpec.Macro("m1") })],
        };
        var hook = new FakeHook();
        using var engine = new SlenderEngine(new FakeMouse(), hook, new FakeForeground(), new FakeExcel(), exec, cfg, new ListLog(), new InlineQueue(), () => 0);
        engine.Start();
        Assert.True(hook.Button(HookButton.Back, true).Handled);
        hook.Button(HookButton.Back, false);
        Assert.Equal([(new ActionSpec.Macro("m1"), true), (new ActionSpec.Macro("m1"), false)], exec.Calls);
    }

    [Fact]
    public void A_ring_slot_runs_a_macro_as_press_then_release()
    {
        var exec = new FakeExecutor();
        using var engine = new SlenderEngine(new FakeMouse(), new FakeHook(), new FakeForeground(), new FakeExcel(), exec, SlenderConfig.Default, new ListLog(), new InlineQueue(), () => 0);
        engine.RunRingAction(new ActionSpec.Macro("m1"));
        Assert.Equal([(new ActionSpec.Macro("m1"), true), (new ActionSpec.Macro("m1"), false)], exec.Calls);
    }
}
