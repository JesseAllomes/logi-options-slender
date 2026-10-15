using Slender.Engine;

namespace Slender.Tests.Engine;

public class MediaChordTests
{
    [Theory]
    [InlineData("MediaPlayPause", 0xB3)]
    [InlineData("MediaNext", 0xB0)]
    [InlineData("MediaPrevious", 0xB1)]
    [InlineData("MediaStop", 0xB2)]
    [InlineData("VolumeMute", 0xAD)]
    [InlineData("VolumeDown", 0xAE)]
    [InlineData("VolumeUp", 0xAF)]
    [InlineData("volumeup", 0xAF)]
    public void Parses(string chord, int vk)
    {
        Assert.True(ChordParser.TryParse(chord, out var keys));
        Assert.Equal([vk], keys.Select(k => (int)k.Vk));
    }

    [Fact]
    public void ModifierPlusMedia() => Assert.True(ChordParser.TryParse("Ctrl+VolumeUp", out var k) && k.Count == 2);
}

public class ThumbWheelBindingTests
{
    private const ushort Wheel = 0xF150;

    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExcel Excel = new();
        public FakeExecutor Exec = new();
        public long Now;
        public Rig(SlenderConfig cfg)
        {
            new SlenderEngine(Mouse, Hook, Fg, Excel, Exec, cfg, new ListLog(), new InlineQueue(), () => Now).Start();
        }
    }

    private static SlenderConfig Cfg(params Profile[] profiles) => SlenderConfig.Default with { Profiles = profiles };
    private static Profile P(string? app, ActionSpec a) => new(app, new Dictionary<ushort, ActionSpec> { [Wheel] = a });

    private static string[] Chords(Rig r) => r.Exec.Calls.Select(c => ((ActionSpec.Keys)c.Item1).Chord).ToArray();

    [Fact]
    public void Default_binding_sends_chord_right_and_shift_toggled_left()
    {
        var r = new Rig(Cfg(P(null, new ActionSpec.Keys("Ctrl+Tab"))));
        Assert.True(r.Hook.Wheel(120).Handled);
        r.Now = 10;
        r.Hook.Wheel(-120);
        Assert.Equal(["Ctrl+Tab", "Ctrl+Shift+Tab"], Chords(r));
    }

    [Fact]
    public void Pipe_form_gives_explicit_directions_and_multiple_notches()
    {
        var r = new Rig(Cfg(P(null, new ActionSpec.Keys("VolumeUp|VolumeDown"))));
        r.Hook.Wheel(240);
        r.Now = 10;
        r.Hook.Wheel(-120);
        Assert.Equal(["VolumeUp", "VolumeUp", "VolumeDown"], Chords(r));
    }

    [Fact]
    public void Partial_delta_swallowed_without_action()
    {
        var r = new Rig(Cfg(P(null, new ActionSpec.Keys("Tab"))));
        Assert.True(r.Hook.Wheel(60).Handled);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Explicit_none_in_app_ignores_default_and_uses_native_behaviour()
    {
        var cfg = Cfg(P(null, new ActionSpec.Keys("Tab")), P("excel", new ActionSpec.None()));
        var r = new Rig(cfg);
        r.Fg.ProcessName = "excel";
        // excel profile None -> pass through: the default's Tab binding is ignored, Excel column scroll applies
        r.Hook.Wheel(120);
        Assert.Empty(r.Exec.Calls);
        Assert.Equal([((nint)42, 1)], r.Excel.Scrolls);

        var r2 = new Rig(Cfg(P(null, new ActionSpec.None())));
        r2.Fg.ProcessName = "excel";
        r2.Hook.Wheel(120);
        Assert.Equal([((nint)42, 1)], r2.Excel.Scrolls);
        Assert.Empty(r2.Exec.Calls);
    }

    [Fact]
    public void No_binding_in_other_app_passes_through()
    {
        var r = new Rig(Cfg(P(null, new ActionSpec.None())));
        r.Fg.ProcessName = "notepad";
        Assert.False(r.Hook.Wheel(120).Handled);
    }

    [Fact]
    public void Excel_action_on_wheel_is_ignored()
    {
        var r = new Rig(Cfg(P(null, new ActionSpec.Excel(ExcelCommand.NextSheet))));
        r.Fg.ProcessName = "notepad";
        Assert.False(r.Hook.Wheel(120).Handled);
    }

    [Theory]
    [InlineData("Tab", "Tab", "Shift+Tab")]
    [InlineData("Ctrl+Shift+Tab", "Ctrl+Shift+Tab", "Ctrl+Tab")]
    [InlineData("Alt+Right|Alt+Left", "Alt+Right", "Alt+Left")]
    public void SplitWheelChord(string chord, string right, string left) =>
        Assert.Equal((right, left), SlenderEngine.SplitWheelChord(chord));
}
