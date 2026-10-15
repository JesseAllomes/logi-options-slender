using Slender.Engine;
using Slender.Engine.Ring;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class TextInputTests
{
    [Fact]
    public void AsciiIsOneKeyPerChar()
    {
        var keys = TextInput.Build("Hi!");
        Assert.Equal([(ushort)'H', (ushort)'i', (ushort)'!'], keys.Select(k => k.Unit));
        Assert.All(keys, k => Assert.False(k.Enter));
    }

    [Fact]
    public void NewlinesAreEnterKeys()
    {
        // "\n", "\r" and "\r\n" are one Enter each.
        Assert.Equal([false, true, false, true, true, false], TextInput.Build("a\nb\r\n\rc").Select(k => k.Enter));
    }

    [Fact]
    public void SurrogatePairsArriveAsTwoUnitsInOrder()
    {
        var keys = TextInput.Build("a\U0001F600");
        Assert.Equal(3, keys.Count);
        Assert.Equal(0xD83D, keys[1].Unit);
        Assert.Equal(0xDE00, keys[2].Unit);
    }

    [Fact]
    public void EmptyGivesNothing() => Assert.Empty(TextInput.Build(""));
}

public class TextActionTests
{
    private sealed class FakeText : ITextSender
    {
        public List<IReadOnlyList<TextKey>> Sent { get; } = [];
        public bool Result = true;
        public bool Send(IReadOnlyList<TextKey> keys) { Sent.Add(keys); return Result; }
    }

    private sealed class NoBrightness : IBrightnessController { public void Adjust(int stepPercent) { } }

    private static ActionExecutor Make(FakeText text, ListLog? log = null) =>
        new(new FakeExcel(), new FakeForeground(), log, brightness: new NoBrightness(), text: text);

    [Fact]
    public void TextActionTypesOnPressOnly()
    {
        var text = new FakeText();
        var ex = Make(text);
        ex.Execute(new ActionSpec.Text("ab\nc"), true);
        ex.Execute(new ActionSpec.Text("ab\nc"), false);
        var sent = Assert.Single(text.Sent);
        Assert.Equal(4, sent.Count);
        Assert.True(sent[2].Enter);
    }

    [Fact]
    public void EmptyTextSendsNothingAndFailureIsLogged()
    {
        var text = new FakeText { Result = false };
        var log = new ListLog();
        var ex = Make(text, log);
        ex.Execute(new ActionSpec.Text(""), true);
        Assert.Empty(text.Sent);
        ex.Execute(new ActionSpec.Text("x"), true);
        Assert.Single(log.Errors);
    }
}

public class RingEngineTests
{
    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExecutor Exec = new();
        public SlenderEngine Engine;
        public List<bool> Triggers = [];

        public Rig(SlenderConfig cfg)
        {
            Engine = new SlenderEngine(Mouse, Hook, Fg, new FakeExcel(), Exec, cfg, new ListLog(), new InlineQueue(), () => 0);
            Engine.RingTrigger += (_, e) => Triggers.Add(e.Pressed);
            Engine.Start();
        }
    }

    private static SlenderConfig Cfg(HapticSettings? h = null, params (ushort, ActionSpec)[] b) =>
        SlenderConfig.Default with
        {
            Profiles = [new Profile(null, b.ToDictionary(x => x.Item1, x => x.Item2))],
            Haptics = h ?? new HapticSettings(),
        };

    [Fact]
    public void SensePanelPressAndReleaseBothReachTheRing()
    {
        var r = new Rig(Cfg(null, (ControlId.SensePanel, new ActionSpec.ActionsRing())));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Equal([true, false], r.Triggers);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void HookButtonTriggerPressAndRelease()
    {
        var r = new Rig(Cfg(null, (ControlId.Middle, new ActionSpec.ActionsRing())));
        r.Hook.Button(HookButton.Middle, true);
        r.Hook.Button(HookButton.Middle, false);
        Assert.Equal([true, false], r.Triggers);
    }

    [Fact]
    public void RunRingActionPressesThenReleasesOnTheQueue()
    {
        var r = new Rig(Cfg());
        var a = new ActionSpec.Keys("Ctrl+C");
        r.Engine.RunRingAction(a);
        Assert.Equal([((ActionSpec)a, true), (a, false)], r.Exec.Calls);
    }

    [Theory]
    [InlineData(RingHaptic.Tick, 4)]
    [InlineData(RingHaptic.Bump, 1)]
    public void RingHapticUsesHoverAndRunWaveforms(RingHaptic kind, int waveform)
    {
        var r = new Rig(Cfg());
        r.Engine.PlayRingHaptic(kind);
        Assert.Equal([(byte)waveform], r.Mouse.Haptics);
    }

    [Fact]
    public void RingHapticRespectsSettings()
    {
        var off = new Rig(Cfg(new HapticSettings(Enabled: false)));
        off.Engine.PlayRingHaptic(RingHaptic.Tick);
        Assert.Empty(off.Mouse.Haptics);
        var noRing = new Rig(Cfg(new HapticSettings(OnActionsRing: false)));
        noRing.Engine.PlayRingHaptic(RingHaptic.Bump);
        Assert.Empty(noRing.Mouse.Haptics);
    }
}
