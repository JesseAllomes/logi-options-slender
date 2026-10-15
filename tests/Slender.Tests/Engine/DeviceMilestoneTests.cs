using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Import;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

/// <summary>m22: per-app divert set, brightness action, device DPI in status.json.</summary>
public class PerAppDivertTests
{
    private static SlenderConfig Cfg(params Profile[] profiles) =>
        SlenderConfig.Default with { Profiles = profiles };

    private static Profile P(string? app, params (ushort, ActionSpec)[] b) =>
        new(app, b.ToDictionary(x => x.Item1, x => x.Item2));

    private static void WaitFor(Func<bool> c) => Assert.True(SpinWait.SpinUntil(c, 5000));

    private static SlenderConfig TeamsNone() => Cfg(
        P(null, (ControlId.ModeShift, new ActionSpec.Keys("F2")), (ControlId.SensePanel, new ActionSpec.Keys("F3"))),
        P("teams", (ControlId.ModeShift, new ActionSpec.None())));

    [Fact]
    public void Effective_set_drops_a_control_the_app_sets_to_None()
    {
        var cfg = TeamsNone();
        Assert.Equal(new[] { ControlId.ModeShift, ControlId.SensePanel }, SlenderEngine.ComputeDivertSet(cfg, "excel"));
        Assert.Equal(new[] { ControlId.SensePanel }, SlenderEngine.ComputeDivertSet(cfg, "teams"));
    }

    [Fact]
    public void Gesture_is_diverted_when_a_swipe_is_bound_for_the_effective_profile()
    {
        var def = P(null);
        var app = P("excel") with { GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Left] = new ActionSpec.Keys("Alt+Left") } };
        var cfg = Cfg(def, app);
        Assert.Equal(new[] { ControlId.Gesture }, SlenderEngine.ComputeDivertSet(cfg, "excel"));
        Assert.Empty(SlenderEngine.ComputeDivertSet(cfg, "notepad"));
        var inherit = Cfg(def with { GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = new ActionSpec.Keys("F5") } }, P("teams"));
        Assert.Equal(new[] { ControlId.Gesture }, SlenderEngine.ComputeDivertSet(inherit, "teams"));
    }

    [Fact]
    public void Foreground_Teams_undiverts_mode_shift_and_leaving_rediverts()
    {
        var mouse = new FakeMouse();
        var fg = new FakeForeground();
        using var engine = new SlenderEngine(mouse, new FakeHook(), fg, new FakeExcel(), new FakeExecutor(), TeamsNone(), new ListLog(), new InlineQueue(), null)
        {
            ForegroundDebounceMs = 0,
        };
        engine.Start();
        WaitFor(() => mouse.DivertCount == 1);
        Assert.Equal(new[] { ControlId.ModeShift, ControlId.SensePanel }, mouse.Diverts[0]);

        fg.Switch("teams");
        WaitFor(() => mouse.DivertCount == 2);
        Assert.Equal(new[] { ControlId.SensePanel }, mouse.Diverts[1]);

        fg.Switch("teams"); // same effective set: nothing re-sent
        fg.Switch("excel");
        WaitFor(() => mouse.DivertCount == 3);
        Assert.Equal(new[] { ControlId.ModeShift, ControlId.SensePanel }, mouse.Diverts[2]);
        Thread.Sleep(50);
        Assert.Equal(3, mouse.DivertCount);
    }

    [Fact]
    public void Rapid_foreground_switches_are_debounced_into_one_change()
    {
        var mouse = new FakeMouse();
        var fg = new FakeForeground();
        using var engine = new SlenderEngine(mouse, new FakeHook(), fg, new FakeExcel(), new FakeExecutor(), TeamsNone(), new ListLog(), new InlineQueue(), null)
        {
            ForegroundDebounceMs = 80,
        };
        engine.Start();
        WaitFor(() => mouse.DivertCount == 1);
        fg.Switch("teams");
        fg.Switch("excel");
        fg.Switch("teams"); // settles on teams
        WaitFor(() => mouse.DivertCount == 2);
        Thread.Sleep(200);
        Assert.Equal(2, mouse.DivertCount);
        Assert.Equal(new[] { ControlId.SensePanel }, mouse.Diverts[1]);
    }

    [Fact]
    public void Divert_change_waits_while_a_diverted_control_is_held()
    {
        var mouse = new FakeMouse();
        var fg = new FakeForeground();
        using var engine = new SlenderEngine(mouse, new FakeHook(), fg, new FakeExcel(), new FakeExecutor(), TeamsNone(), new ListLog(), new InlineQueue(), null)
        {
            ForegroundDebounceMs = 20,
        };
        engine.Start();
        WaitFor(() => mouse.DivertCount == 1);

        mouse.Press(ControlId.ModeShift, true);
        fg.Switch("teams"); // would un-divert mode shift and lose its release
        Thread.Sleep(200);
        Assert.Equal(1, mouse.DivertCount);

        mouse.Press(ControlId.ModeShift, false);
        WaitFor(() => mouse.DivertCount == 2);
        Assert.Equal(new[] { ControlId.SensePanel }, mouse.Diverts[1]);
    }

    [Fact]
    public void Keyboard_explicit_None_undiverts_and_returning_rediverts()
    {
        Profile K(string? app, params (ushort, ActionSpec)[] keys) =>
            new(app, new Dictionary<ushort, ActionSpec>()) { KeyBindings = keys.ToDictionary(k => k.Item1, k => k.Item2) };
        var cfg = Cfg(K(null, (KeyboardId.Mute, new ActionSpec.Keys("Ctrl+M"))), K("teams", (KeyboardId.Mute, new ActionSpec.None())));
        var kb = new FakeKeyboard();
        var fg = new FakeForeground();
        using var router = new KeyboardRouter(kb, fg, new FakeExecutor(), cfg, new ListLog(), new InlineQueue()) { ForegroundDebounceMs = 0 };
        router.Start();
        WaitFor(() => kb.DivertCount == 1);
        Assert.Equal(new[] { KeyboardId.Mute }, kb.Diverts[0]);
        fg.Switch("teams");
        WaitFor(() => kb.DivertCount == 2);
        Assert.Empty(kb.Diverts[1]);
        fg.Switch("excel");
        WaitFor(() => kb.DivertCount == 3);
        Assert.Equal(new[] { KeyboardId.Mute }, kb.Diverts[2]);
    }
}

public class BrightnessTests
{
    private sealed class FakeDevice(Dictionary<nint, int> levels, nint monitor, List<string> calls, bool supported = true, bool acceptSet = true) : IBrightnessDevice
    {
        public bool TryGet(out int percent) { percent = levels.GetValueOrDefault(monitor); calls.Add("get"); return supported; }

        public bool TrySet(int percent)
        {
            calls.Add("set" + percent);
            if (!acceptSet) return false;
            levels[monitor] = percent;
            return true;
        }

        public void Dispose() { }
    }

    private sealed class FakeMonitors : IMonitorProvider
    {
        public nint Cursor = 1;
        public Dictionary<nint, int> Levels = new() { [1] = 50, [2] = 95 };
        public HashSet<nint> Unsupported = [];
        public List<string> Calls = [];
        public nint MonitorAtCursor() => Cursor;
        public IBrightnessDevice? Open(nint monitor) => Unsupported.Contains(monitor) ? null : new FakeDevice(Levels, monitor, Calls);
    }

    [Fact]
    public void Steps_the_monitor_under_the_cursor_and_clamps()
    {
        var m = new FakeMonitors();
        var c = new BrightnessController(m, null, w => w());
        c.Adjust(10);
        Assert.Equal(60, m.Levels[1]);
        m.Cursor = 2;
        c.Adjust(10);
        Assert.Equal(100, m.Levels[2]);
        c.Adjust(-10);
        Assert.Equal(90, m.Levels[2]);
        Assert.Equal(60, m.Levels[1]);
    }

    [Fact]
    public void Unsupported_monitor_is_ignored_without_throwing()
    {
        var m = new FakeMonitors { Unsupported = [1] };
        var c = new BrightnessController(m, null, w => w());
        c.Adjust(10);
        c.Adjust(10);
        Assert.Equal(50, m.Levels[1]);
        Assert.Empty(m.Calls);
    }

    [Fact]
    public void Presses_during_a_slow_call_coalesce_into_one_write()
    {
        var m = new FakeMonitors();
        Action? queued = null;
        var c = new BrightnessController(m, null, w => queued = w);
        c.Adjust(10);
        c.Adjust(10);
        c.Adjust(10); // worker not started yet: all three are summed
        queued!();
        Assert.Equal(80, m.Levels[1]);
        Assert.Single(m.Calls, "set80");
    }

    private sealed class ConfigurableMonitors : IMonitorProvider
    {
        public bool Supported = true, AcceptSet = true;
        public Dictionary<nint, int> Levels = new() { [1] = 50 };
        public List<string> Calls = [];
        public nint MonitorAtCursor() => 1;
        public IBrightnessDevice? Open(nint monitor) => new FakeDevice(Levels, monitor, Calls, Supported, AcceptSet);
    }

    private sealed class InfoLog : ILog
    {
        public List<string> Lines = [];
        public void Info(string message) => Lines.Add(message);
        public void Error(string message, Exception? exception = null) => Lines.Add(message);
    }

    [Fact]
    public void Refused_set_is_logged_and_not_retried()
    {
        var m = new ConfigurableMonitors { AcceptSet = false };
        var log = new InfoLog();
        var c = new BrightnessController(m, log, w => w());
        c.Adjust(10);
        Assert.Equal(50, m.Levels[1]);
        Assert.Contains(log.Lines, l => l.Contains("refused brightness"));
        c.Adjust(10);
        Assert.Equal(2, m.Calls.Count(x => x == "set60")); // the monitor is not marked unsupported
    }

    [Fact]
    public void Failed_read_is_retried_after_thirty_seconds()
    {
        var m = new ConfigurableMonitors { Supported = false };
        var log = new InfoLog();
        long now = 0;
        var c = new BrightnessController(m, log, w => w(), () => now);

        c.Adjust(10); // t = 0: the monitor does not answer
        Assert.Equal(["get"], m.Calls);
        Assert.Single(log.Lines);

        m.Supported = true;
        now = 10_000;
        c.Adjust(10); // still inside the 30 s back-off: not even asked
        Assert.Equal(["get"], m.Calls);
        Assert.Equal(50, m.Levels[1]);

        now = 31_000;
        c.Adjust(10);
        Assert.Equal(["get", "get", "set60"], m.Calls);
        Assert.Equal(60, m.Levels[1]);
    }

    [Fact]
    public void Brightness_entry_without_step_is_rejected()
    {
        static ActionSpec Read(string json)
        {
            var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
            reader.Read();
            return new ActionSpecJsonConverter().Read(ref reader, typeof(ActionSpec), new System.Text.Json.JsonSerializerOptions());
        }

        var missing = Assert.Throws<FormatException>(() => Read("""{"type":"brightness"}"""));
        Assert.Contains("step", missing.Message);
        var wrongType = Assert.Throws<FormatException>(() => Read("""{"type":"brightness","step":"ten"}"""));
        Assert.Contains("step", wrongType.Message);
    }

    [Fact]
    public void Action_json_round_trips()
    {
        var cfg = SlenderConfig.Default with
        {
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { KeyBindings = new Dictionary<ushort, ActionSpec> { [199] = new ActionSpec.Brightness(-10) } }],
        };
        var dir = Path.Combine(Path.GetTempPath(), "slender-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore(dir);
            store.Save(cfg);
            var json = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.Contains("\"brightness\"", json);
            Assert.Contains("-10", json);
            Assert.Equal(new ActionSpec.Brightness(-10), store.Load().Profiles[0].KeyBindings[199]);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Status_json_carries_device_dpi()
    {
        var status = new SlenderStatus([new DeviceState("mx-master-4", "MX Master 4", true, 80, false) { Dpi = 1200 }], false, null, DateTimeOffset.UnixEpoch);
        var back = StatusJson.Parse(StatusJson.Serialize(status))!;
        Assert.Equal(1200, back.Devices[0].Dpi);
        Assert.Contains("\"dpi\": 1200", StatusJson.Serialize(status));
    }
}
