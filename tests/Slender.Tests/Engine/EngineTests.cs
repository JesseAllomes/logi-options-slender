using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class AccumulatorTests
{
    [Fact]
    public void FullNotchesGiveColumns()
    {
        var a = new ColumnScrollAccumulator();
        Assert.Equal(1, a.Add(120, 1, 0));
        Assert.Equal(-1, a.Add(-120, 1, 10));
        Assert.Equal(2, a.Add(240, 1, 20));
    }

    [Fact]
    public void HiResPartialDeltasAccumulate()
    {
        var a = new ColumnScrollAccumulator();
        Assert.Equal(0, a.Add(30, 1, 0));
        Assert.Equal(0, a.Add(30, 1, 10));
        Assert.Equal(0, a.Add(30, 1, 20));
        Assert.Equal(1, a.Add(30, 1, 30));
        Assert.Equal(0, a.Add(30, 1, 40));
    }

    [Fact]
    public void DirectionChangeResetsRemainder()
    {
        var a = new ColumnScrollAccumulator();
        a.Add(90, 1, 0);
        // Without a reset 90-60 = 30 would carry; with it the remainder is just -60.
        Assert.Equal(0, a.Add(-60, 1, 10));
        Assert.Equal(-1, a.Add(-60, 1, 20));
    }

    [Fact]
    public void IdleResetsRemainder()
    {
        var a = new ColumnScrollAccumulator();
        a.Add(90, 1, 0);
        Assert.Equal(0, a.Add(60, 1, 600)); // 90 dropped after idle; carried would give 1
        Assert.Equal(1, a.Add(60, 1, 700));
    }

    [Fact]
    public void ColumnsPerNotchMultiplies()
    {
        var a = new ColumnScrollAccumulator();
        Assert.Equal(3, a.Add(120, 3, 0));
        Assert.Equal(1, a.Add(40, 3, 10));
    }
}

public class ChordParserTests
{
    [Theory]
    [InlineData("Ctrl+Shift+P", new[] { 0xA2, 0xA0, 0x50 })]
    [InlineData("Alt+Left", new[] { 0xA4, 0x25 })]
    [InlineData("Win+Tab", new[] { 0x5B, 0x09 })]
    [InlineData("ctrl+pagedown", new[] { 0xA2, 0x22 })]
    [InlineData("F1", new[] { 0x70 })]
    [InlineData("Ctrl+F24", new[] { 0xA2, 0x87 })]
    [InlineData("Ctrl+5", new[] { 0xA2, 0x35 })]
    [InlineData("P+Ctrl", new[] { 0xA2, 0x50 })]
    public void Parses(string chord, int[] expected)
    {
        Assert.True(ChordParser.TryParse(chord, out var keys));
        Assert.Equal(expected, keys.Select(k => (int)k.Vk));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Banana")]
    [InlineData("F25")]
    public void Rejects(string? chord) => Assert.False(ChordParser.TryParse(chord, out _));

    [Fact]
    public void NavigationKeysAreExtended()
    {
        ChordParser.TryParse("Alt+Left", out var keys);
        Assert.False(keys[0].Extended);
        Assert.True(keys[1].Extended);
    }
}

public class ProfileResolverTests
{
    private static readonly SlenderConfig Cfg = SlenderConfig.Default;

    [Fact]
    public void MatchesCaseInsensitive() =>
        Assert.Equal("excel", ProfileResolver.Resolve(Cfg, "EXCEL").AppProcess);

    [Fact]
    public void ToleratesExeSuffixInConfig()
    {
        var cfg = Cfg with { Profiles = [new Profile("Excel.EXE", new Dictionary<ushort, ActionSpec>())] };
        Assert.Equal("Excel.EXE", ProfileResolver.Resolve(cfg, "excel").AppProcess);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData(null)]
    public void FallsBackToDefault(string? name) => Assert.Null(ProfileResolver.Resolve(Cfg, name).AppProcess);

    [Fact]
    public void NoDefaultProfileGivesEmpty()
    {
        var cfg = Cfg with { Profiles = [] };
        Assert.Null(ProfileResolver.Resolve(cfg, "x").AppProcess);
    }
}

public class BoundedWorkerTests
{
    [Fact]
    public void DropsOldestWhenFull()
    {
        using var w = new BoundedWorker("t", 3, sta: true);
        var gate = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        var ran = new List<int>();
        var dropped = new List<int>();
        w.Post(() => { started.Set(); gate.Wait(5000); }, null);
        Assert.True(started.Wait(5000));
        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            w.Post(() => { lock (ran) ran.Add(n); }, () => dropped.Add(n));
        }
        Assert.Equal([1, 2], dropped);
        Assert.Equal(2, w.DroppedCount);
        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => { lock (ran) return ran.Count == 3; }, 5000));
        lock (ran) Assert.Equal([3, 4, 5], ran);
    }

    [Fact]
    public void WorkExceptionsDoNotKillThread()
    {
        var log = new ListLog();
        using var w = new BoundedWorker("t", 8, sta: false, log);
        var done = new ManualResetEventSlim();
        w.Post(() => throw new InvalidOperationException());
        w.Post(done.Set);
        Assert.True(done.Wait(5000));
        Assert.Single(log.Errors);
    }

    [Fact]
    public void RunsOnStaThread()
    {
        using var w = new BoundedWorker("t", 8, sta: true);
        var state = ApartmentState.Unknown;
        var done = new ManualResetEventSlim();
        w.Post(() => { state = Thread.CurrentThread.GetApartmentState(); done.Set(); });
        Assert.True(done.Wait(5000));
        Assert.Equal(ApartmentState.STA, state);
    }
}

public class EngineTests
{
    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExcel Excel = new();
        public FakeExecutor Exec = new();
        public ListLog Log = new();
        public long Now;
        public SlenderEngine Engine;

        public Rig(SlenderConfig? cfg = null)
        {
            Engine = new SlenderEngine(Mouse, Hook, Fg, Excel, Exec, cfg ?? SlenderConfig.Default, Log, new InlineQueue(), () => Now);
            Engine.Start();
        }
    }

    private static SlenderConfig Cfg(ExcelSettings? ex = null, HapticSettings? h = null, params Profile[] profiles) =>
        SlenderConfig.Default with { Profiles = profiles, Excel = ex ?? new ExcelSettings(), Haptics = h ?? new HapticSettings() };

    private static Profile P(string? app, params (ushort, ActionSpec)[] b) =>
        new(app, b.ToDictionary(x => x.Item1, x => x.Item2));

    private static void WaitFor(Func<bool> c) => Assert.True(SpinWait.SpinUntil(c, 5000));

    [Fact]
    public void StartStartsEverything()
    {
        var r = new Rig();
        Assert.True(r.Hook.Started);
        Assert.True(r.Mouse.Started);
    }

    [Fact]
    public void WheelInExcelIsSwallowedAndScrolls()
    {
        var r = new Rig();
        r.Fg.ProcessName = "excel";
        var a = r.Hook.Wheel(120);
        Assert.True(a.Handled);
        Assert.Equal([((nint)42, 1)], r.Excel.Scrolls);
    }

    [Fact]
    public void WheelNegativeScrollsLeft()
    {
        var r = new Rig();
        r.Fg.ProcessName = "excel";
        r.Hook.Wheel(-240);
        Assert.Equal([((nint)42, -2)], r.Excel.Scrolls);
    }

    [Fact]
    public void PartialWheelIsSwallowedButNotYetScrolled()
    {
        var r = new Rig();
        r.Fg.ProcessName = "excel";
        Assert.True(r.Hook.Wheel(60).Handled);
        Assert.Empty(r.Excel.Scrolls);
        r.Now = 10;
        r.Hook.Wheel(60);
        Assert.Single(r.Excel.Scrolls);
    }

    [Fact]
    public void WheelInOtherAppPassesThrough()
    {
        var r = new Rig();
        r.Fg.ProcessName = "notepad";
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Excel.Scrolls);
    }

    [Fact]
    public void WheelWithSettingOffPassesThrough()
    {
        var r = new Rig(Cfg(new ExcelSettings(false, 1)));
        r.Fg.ProcessName = "excel";
        Assert.False(r.Hook.Wheel(120).Handled);
        Assert.Empty(r.Excel.Scrolls);
    }

    [Fact]
    public void ColumnsPerNotchApplied()
    {
        var r = new Rig(Cfg(new ExcelSettings(true, 4)));
        r.Fg.ProcessName = "excel";
        r.Hook.Wheel(120);
        Assert.Equal(4, r.Excel.Scrolls[0].Item2);
    }

    [Fact]
    public void BridgeExceptionNeverEscapesWheel()
    {
        var r = new Rig();
        r.Fg.ProcessName = "excel";
        r.Excel.Throw = true;
        var a = r.Hook.Wheel(120);
        Assert.True(a.Handled);
        Assert.NotEmpty(r.Log.Errors);
    }

    [Fact]
    public void ExecutorExceptionNeverEscapesButton()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Gesture, new ActionSpec.Keys("Ctrl+C")))));
        r.Exec.Throw = true;
        r.Mouse.Press(ControlId.Gesture, true);
        Assert.NotEmpty(r.Log.Errors);
    }

    [Fact]
    public void ProfileBindingWinsOverDefault()
    {
        var def = new ActionSpec.Keys("Ctrl+C");
        var ex = new ActionSpec.Excel(ExcelCommand.NextSheet);
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Forward, def)), P("excel", (ControlId.Forward, ex))));
        r.Fg.ProcessName = "excel";
        Assert.True(r.Hook.Button(HookButton.Forward, true).Handled);
        r.Hook.Button(HookButton.Forward, false);
        Assert.Equal([((ActionSpec)ex, true), (ex, false)], r.Exec.Calls);
    }

    [Fact]
    public void UnboundInProfileFallsBackToDefaultProfile()
    {
        var def = new ActionSpec.Keys("Ctrl+C");
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Gesture, def)), P("excel", (ControlId.Back, new ActionSpec.Excel(ExcelCommand.PreviousSheet)))));
        r.Fg.ProcessName = "excel";
        r.Mouse.Press(ControlId.Gesture, true);
        Assert.Equal(def, r.Exec.Calls.Single().Item1);
    }

    [Theory]
    [InlineData(HookButton.Back)]
    [InlineData(HookButton.Forward)]
    [InlineData(HookButton.Middle)]
    public void UnboundHookButtonIsNotHandled(HookButton b)
    {
        var r = new Rig(Cfg(null, null, P(null), P("excel", ((ushort)b, new ActionSpec.Keys("F5")))));
        r.Fg.ProcessName = "notepad";
        Assert.False(r.Hook.Button(b, true).Handled);
        Assert.False(r.Hook.Button(b, false).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Theory]
    [InlineData(HookButton.Back, "Back")]
    [InlineData(HookButton.Forward, "Forward")]
    [InlineData(HookButton.Middle, "middle")]
    public void BindingToOwnNativeButtonIsNotHandled(HookButton b, string name)
    {
        var r = new Rig(Cfg(null, null, P(null, ((ushort)b, new ActionSpec.MouseButton(name)))));
        Assert.False(r.Hook.Button(b, true).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void BindingToAnotherMouseButtonIsHandled()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Back, new ActionSpec.MouseButton("Middle")))));
        Assert.True(r.Hook.Button(HookButton.Back, true).Handled);
        Assert.Single(r.Exec.Calls);
    }

    [Fact]
    public void ExcelBoundHookButtonIsHandledAndExecutedOnlyInExcel()
    {
        var ex = new ActionSpec.Excel(ExcelCommand.NextSheet);
        var r = new Rig(Cfg(null, null, P(null), P("excel", (ControlId.Forward, ex))));
        r.Fg.ProcessName = "excel";
        Assert.True(r.Hook.Button(HookButton.Forward, true).Handled);
        Assert.False(r.Hook.Button(HookButton.Forward, false).Handled); // hook pairs the release itself
        Assert.Equal([((ActionSpec)ex, true), (ex, false)], r.Exec.Calls);
        r.Exec.Calls.Clear();
        r.Fg.ProcessName = "notepad";
        Assert.False(r.Hook.Button(HookButton.Forward, true).Handled);
        Assert.False(r.Hook.Button(HookButton.Forward, false).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void HookReleaseFollowsPressEvenIfForegroundChanged()
    {
        var a = new ActionSpec.Keys("F1");
        var r = new Rig(Cfg(null, null, P(null), P("excel", (ControlId.Back, a))));
        r.Fg.ProcessName = "excel";
        r.Hook.Button(HookButton.Back, true);
        r.Fg.ProcessName = "notepad";
        r.Hook.Button(HookButton.Back, false);
        Assert.Equal([((ActionSpec)a, true), (a, false)], r.Exec.Calls);
        r.Hook.Button(HookButton.Back, false); // stray release: nothing more
        Assert.Equal(2, r.Exec.Calls.Count);
    }

    [Fact]
    public void HookActionsRingRaisesEvent()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Middle, new ActionSpec.ActionsRing()))));
        var raised = 0;
        r.Engine.ActionsRingRequested += (_, _) => raised++;
        Assert.True(r.Hook.Button(HookButton.Middle, true).Handled);
        r.Hook.Button(HookButton.Middle, false);
        Assert.Equal(1, raised);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void HookButtonExecutorFailureNeverEscapes()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Back, new ActionSpec.Keys("F1")))));
        r.Exec.Throw = true;
        Assert.True(r.Hook.Button(HookButton.Back, true).Handled);
        Assert.NotEmpty(r.Log.Errors);
    }

    [Fact]
    public void StaleHidppEventsForHookButtonsAreIgnored()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Back, new ActionSpec.Keys("F1")))));
        r.Mouse.Press(ControlId.Back, true);
        r.Mouse.Press(ControlId.Back, false);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void UnboundGestureDoesNothing()
    {
        var r = new Rig(Cfg(null, null, P(null)));
        r.Mouse.Press(ControlId.Gesture, true);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void ReleaseUsesActionChosenAtPress()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Gesture, new ActionSpec.MouseButton("Left"))), P("excel", (ControlId.Gesture, new ActionSpec.MouseButton("Right")))));
        r.Fg.ProcessName = "excel";
        r.Mouse.Press(ControlId.Gesture, true);
        r.Fg.ProcessName = "notepad";
        r.Mouse.Press(ControlId.Gesture, false);
        Assert.Equal(new ActionSpec.MouseButton("Right"), r.Exec.Calls[1].Item1);
        Assert.False(r.Exec.Calls[1].Item2);
    }

    [Fact]
    public void ActionsRingRaisesEventNotExecutor()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.Gesture, new ActionSpec.ActionsRing()))));
        var raised = 0;
        r.Engine.ActionsRingRequested += (_, _) => raised++;
        r.Mouse.Press(ControlId.Gesture, true);
        r.Mouse.Press(ControlId.Gesture, false);
        Assert.Equal(1, raised);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void DivertSetIsUnionOfNonNoneBindings()
    {
        var cfg = Cfg(null, null,
            P(null, (ControlId.Back, new ActionSpec.Keys("F1")), (ControlId.Middle, new ActionSpec.Keys("F4")),
                (ControlId.ModeShift, new ActionSpec.None()), (ControlId.SensePanel, new ActionSpec.ActionsRing())),
            P("excel", (ControlId.Forward, new ActionSpec.Keys("F2")), (ControlId.Gesture, new ActionSpec.Keys("F3"))));
        Assert.Equal(new[] { ControlId.Gesture, ControlId.SensePanel }, SlenderEngine.ComputeDivertSet(cfg));
        var r = new Rig(cfg);
        WaitFor(() => r.Mouse.DivertCount == 1);
        Assert.Equal(new[] { ControlId.Gesture, ControlId.SensePanel }, r.Mouse.Diverts[0]);
    }

    [Fact]
    public void ApplyConfigRedivertsOnlyWhenSetChanges()
    {
        var r = new Rig(Cfg(null, null, P(null, (ControlId.ModeShift, new ActionSpec.Keys("F1")))));
        WaitFor(() => r.Mouse.DivertCount == 1);
        r.Engine.ApplyConfig(Cfg(null, null, P(null, (ControlId.ModeShift, new ActionSpec.Keys("F9")), (ControlId.Back, new ActionSpec.Keys("F5")))));
        Thread.Sleep(100);
        Assert.Equal(1, r.Mouse.DivertCount); // key change and hook-only button do not re-divert
        r.Engine.ApplyConfig(Cfg(null, null, P(null, (ControlId.ModeShift, new ActionSpec.Keys("F1")), (ControlId.Gesture, new ActionSpec.Keys("F2")))));
        WaitFor(() => r.Mouse.DivertCount == 2);
        Assert.Equal(new[] { ControlId.Gesture, ControlId.ModeShift }, r.Mouse.Diverts[1]);
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public void HapticOnPressFollowsSettings(bool enabled, bool onPress, int expected)
    {
        var r = new Rig(Cfg(null, new HapticSettings(enabled, 2, onPress, true), P(null, (ControlId.Back, new ActionSpec.Keys("F1")))));
        r.Hook.Button(HookButton.Back, true);
        r.Hook.Button(HookButton.Back, false);
        Assert.Equal(expected, r.Mouse.Haptics.Count);
        if (expected == 1) Assert.Equal((byte)1, r.Mouse.Haptics[0]);
    }

    [Fact]
    public void HapticFailureIsSwallowed()
    {
        var r = new Rig(Cfg(null, new HapticSettings(true, 2, true, true), P(null, (ControlId.Back, new ActionSpec.Keys("F1")))));
        r.Mouse.ThrowOnHaptic = true;
        r.Hook.Button(HookButton.Back, true);
        WaitFor(() => { lock (r.Log.Errors) return r.Log.Errors.Count > 0; });
        Assert.Single(r.Exec.Calls);
    }

    [Fact]
    public void StatusChangedOnConnection()
    {
        var r = new Rig();
        var seen = new List<EngineStatus>();
        r.Engine.StatusChanged += (_, s) => { lock (seen) seen.Add(s); };
        r.Mouse.Connect(true);
        WaitFor(() => { lock (seen) return seen.Any(s => s.BatteryPercent == 80); });
        lock (seen) Assert.True(seen[0].Connected);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        var r = new Rig();
        r.Fg.ProcessName = "excel";
        r.Engine.Dispose();
        Assert.False(r.Hook.Wheel(120).Handled);
    }
}
