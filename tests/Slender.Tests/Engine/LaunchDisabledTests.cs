using System.Text.Json;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

internal sealed class FakeShell : IShellLauncher
{
    public List<(string Target, string? Args)> Opened { get; } = [];
    public bool Throw;
    public void Open(string target, string? arguments)
    {
        if (Throw) throw new InvalidOperationException("no handler");
        Opened.Add((target, arguments));
    }
}

public class LaunchDisabledTests
{
    private static readonly JsonSerializerOptions Opts = new() { Converters = { new ActionSpecJsonConverter() } };

    [Fact]
    public void Converter_round_trips_launch_and_disabled()
    {
        foreach (ActionSpec a in new ActionSpec[]
                 {
                     new ActionSpec.Launch(@"C:\Program Files\Excel.exe", "/safe"),
                     new ActionSpec.Launch("https://example.com"),
                     new ActionSpec.Disabled(),
                 })
        {
            Assert.Equal(a, JsonSerializer.Deserialize<ActionSpec>(JsonSerializer.Serialize(a, Opts), Opts));
        }

        Assert.Contains("\"type\":\"disabled\"", JsonSerializer.Serialize<ActionSpec>(new ActionSpec.Disabled(), Opts));
        Assert.Equal(new ActionSpec.Launch("x", null), JsonSerializer.Deserialize<ActionSpec>("""{"type":"launch","target":"x"}""", Opts));
    }

    [Fact]
    public void Executor_launches_on_press_only()
    {
        var shell = new FakeShell();
        var ex = new ActionExecutor(new FakeExcel(), new FakeForeground(), shell: shell);
        var a = new ActionSpec.Launch("calc.exe", "-x");
        ex.Execute(a, true);
        ex.Execute(a, false);
        Assert.Equal([("calc.exe", (string?)"-x")], shell.Opened);
    }

    [Fact]
    public void Executor_logs_launch_failure_and_disabled_does_nothing()
    {
        var shell = new FakeShell { Throw = true };
        var log = new ListLog();
        var ex = new ActionExecutor(new FakeExcel(), new FakeForeground(), log, shell);
        ex.Execute(new ActionSpec.Launch("nope"), true);
        ex.Execute(new ActionSpec.Disabled(), true);
        ex.Execute(new ActionSpec.Disabled(), false);
        Assert.Single(log.Errors);
        Assert.Empty(shell.Opened);
    }

    [Fact]
    public void Disabled_is_diverted_but_none_is_not()
    {
        var p = new Profile(null, new Dictionary<ushort, ActionSpec>
        {
            [ControlId.Gesture] = new ActionSpec.Disabled(),
            [ControlId.SensePanel] = new ActionSpec.None(),
        });
        var cfg = SlenderConfig.Default with { Profiles = [p] };
        Assert.Equal(new[] { ControlId.Gesture }, SlenderEngine.ComputeDivertSet(cfg));
    }

    [Fact]
    public void Hook_button_disabled_is_swallowed_and_runs_nothing_visible()
    {
        var p = new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.Back] = new ActionSpec.Disabled() });
        var cfg = SlenderConfig.Default with { Profiles = [p] };
        var hook = new FakeHook();
        var exec = new FakeExecutor();
        new SlenderEngine(new FakeMouse(), hook, new FakeForeground(), new FakeExcel(), exec, cfg, new ListLog(), new InlineQueue(), () => 0).Start();
        Assert.True(hook.Button(HookButton.Back, true).Handled);
        hook.Button(HookButton.Back, false);
        Assert.All(exec.Calls, c => Assert.IsType<ActionSpec.Disabled>(c.Item1));
    }

    [Fact]
    public void Thumb_wheel_disabled_is_swallowed()
    {
        var p = new Profile(null, new Dictionary<ushort, ActionSpec> { [SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Disabled() });
        var cfg = SlenderConfig.Default with { Profiles = [p] };
        var hook = new FakeHook();
        var exec = new FakeExecutor();
        new SlenderEngine(new FakeMouse(), hook, new FakeForeground(), new FakeExcel(), exec, cfg, new ListLog(), new InlineQueue(), () => 0).Start();
        Assert.True(hook.Wheel(120).Handled);
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void Keyboard_disabled_key_is_diverted()
    {
        var p = new Profile(null, new Dictionary<ushort, ActionSpec>()) { KeyBindings = new Dictionary<ushort, ActionSpec> { [KeyboardId.Mute] = new ActionSpec.Disabled(), [KeyboardId.Lock] = new ActionSpec.None() } };
        var cfg = SlenderConfig.Default with { Profiles = [p] };
        Assert.Equal(new[] { KeyboardId.Mute }, KeyboardRouter.ComputeDivertSet(cfg));
    }

    [Fact]
    public void Empty_launch_target_is_logged_and_not_opened()
    {
        var shell = new FakeShell();
        var log = new ListLog();
        var ex = new ActionExecutor(new FakeExcel(), new FakeForeground(), log, shell);
        ex.Execute(new ActionSpec.Launch(""), true);
        ex.Execute(new ActionSpec.Launch("   "), true);
        Assert.Empty(shell.Opened);
        Assert.Equal(2, log.Errors.Count(e => e.Contains("launch target is empty")));
    }

    [Fact]
    public void Sequence_stops_at_the_first_failed_step()
    {
        var log = new ListLog();
        var sends = new List<IReadOnlyList<KeyStroke>>();
        var ex = new ActionExecutor(new FakeExcel(), new FakeForeground(), log, sendChord: keys =>
        {
            sends.Add(keys);
            return sends.Count != 2;
        });
        ex.Execute(new ActionSpec.Keys("Enter, Ctrl+W, Esc"), true);
        Assert.Equal(2, sends.Count);
        Assert.Contains(log.Errors, e => e.Contains("SendInput failed"));
    }
}
