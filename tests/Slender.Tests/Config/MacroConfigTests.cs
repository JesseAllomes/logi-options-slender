using System.Text.Json;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Macros;
using Slender.Hidpp;

namespace Slender.Tests.Config;

public sealed class MacroConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private JsonConfigStore Store() => new(_dir);

    private static SlenderConfig With(params MacroDef[] macros) => SlenderConfig.Default with { Macros = macros };

    private static SlenderConfig Parse(string macrosJson) =>
        JsonConfigStore.Parse("{\"version\":2,\"profiles\":[],\"macros\":" + macrosJson + "}");

    [Fact]
    public void Every_step_kind_round_trips()
    {
        var macro = new MacroDef("abc12345", "Everything",
        [
            MacroStep.KeyDown("Ctrl"), MacroStep.KeyUp("Ctrl"), MacroStep.Tap("Ctrl+Shift+P"), MacroStep.Tap("Enter, Ctrl+W"), MacroStep.Type("Kind regards,\nJesse"),
            MacroStep.MouseDown("Left"), MacroStep.MouseUp("Left"), MacroStep.Click("Back"), MacroStep.MoveBy(-40, 25), MacroStep.MoveTo(-1200, 300),
            MacroStep.Wheel(-3, 2), MacroStep.Launch("notepad.exe", "a.txt"), MacroStep.Pause(250), MacroStep.Pause(60_000),
        ], MacroRunMode.WhileHeld, 120);
        Store().Save(With(macro, new MacroDef("zz", "Second", [MacroStep.Pause(10)], MacroRunMode.Toggle, 5)));
        var loaded = Store().Load();
        Assert.Equal(2, loaded.Macros.Count);
        Assert.Equal(macro, loaded.Macros[0]);
        Assert.Equal(MacroRunMode.Toggle, loaded.Macros[1].Mode);
        Assert.Equal(5, loaded.Macros[1].CapSeconds);
    }

    [Fact]
    public void A_macro_action_round_trips_in_bindings_and_ring_slots()
    {
        var cfg = SlenderConfig.Default with
        {
            Profiles =
            [
                new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.Back] = new ActionSpec.Macro("abc") })
                {
                    RingSlots = [new RingSlot(new ActionSpec.Macro("abc")) { Name = "Do it" }, .. Enumerable.Repeat(RingSlot.Empty, 7)],
                    GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = new ActionSpec.Macro("abc") },
                },
            ],
            Macros = [new MacroDef("abc", "M", [MacroStep.Click("Left")])],
        };
        Store().Save(cfg);
        var loaded = Store().Load();
        Assert.Equal(new ActionSpec.Macro("abc"), loaded.Profiles[0].Bindings[ControlId.Back]);
        Assert.Equal(new ActionSpec.Macro("abc"), loaded.Profiles[0].RingSlots![0].Action);
        Assert.Equal(new ActionSpec.Macro("abc"), loaded.Profiles[0].GestureSwipes[GestureDirection.Up]);
    }

    [Fact]
    public void A_macro_can_sit_in_a_press_hold_half()
    {
        var spec = new ActionSpec.TapHold(new ActionSpec.Macro("a"), new ActionSpec.Macro("b"));
        var cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.Back] = spec })] };
        Store().Save(cfg);
        Assert.Equal(spec, Store().Load().Profiles[0].Bindings[ControlId.Back]);
    }

    [Fact]
    public void The_library_is_empty_for_old_files_and_written_as_null_when_empty()
    {
        Store().Save(SlenderConfig.Default);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "config.json")));
        Assert.True(!doc.RootElement.TryGetProperty("macros", out var written) || written.ValueKind == JsonValueKind.Null);
        Assert.Empty(Store().Load().Macros);
    }

    [Fact]
    public void Pauses_are_clamped_to_10_ms_through_60_s()
    {
        var cfg = Parse("""[{"id":"a","name":"A","steps":[{"kind":"Pause","ms":1},{"kind":"Pause","ms":-50},{"kind":"Pause","ms":9999999},{"kind":"Pause","ms":777}]}]""");
        Assert.Equal([10, 10, 60_000, 777], cfg.Macros[0].Steps.Select(s => s.Ms));
        Assert.Equal(10, MacroStep.PauseMinMs);
        Assert.Equal(60_000, MacroStep.PauseMaxMs);
    }

    [Fact]
    public void Moves_wheel_cap_and_mode_are_clamped_or_defaulted()
    {
        var cfg = Parse("""[{"id":"a","name":"A","mode":"Sideways","capSeconds":999999,"steps":[{"kind":"Move","x":99999,"y":-99999},{"kind":"Wheel","y":500,"x":-500}]},{"id":"b","name":"B","capSeconds":-4,"steps":[]}]""");
        Assert.Equal(MacroRunMode.Once, cfg.Macros[0].Mode);
        Assert.Equal(MacroDef.CapMaxSeconds, cfg.Macros[0].CapSeconds);
        Assert.Equal((MacroStep.MoveLimit, -MacroStep.MoveLimit), (cfg.Macros[0].Steps[0].X, cfg.Macros[0].Steps[0].Y));
        Assert.Equal((-MacroStep.WheelLimit, MacroStep.WheelLimit), (cfg.Macros[0].Steps[1].X, cfg.Macros[0].Steps[1].Y));
        Assert.Equal(MacroDef.CapMinSeconds, cfg.Macros[1].CapSeconds);
    }

    [Fact]
    public void Garbage_steps_and_macros_are_dropped_not_the_file()
    {
        var cfg = Parse("""
            [
              {"id":"ok","name":"  Fine  ","steps":[
                {"kind":"Tap","text":"Ctrl+NoSuchKey"},{"kind":"Tap","text":"Ctrl+C"},{"kind":"Nope"},{"kind":"KeyDown","text":"Ctrl+C"},{"kind":"KeyDown","text":"Shift"},
                {"kind":"Click","text":"Thumb"},{"kind":"Click","text":"left"},{"kind":"Type","text":""},{"kind":"Launch","text":"  "},{"kind":"Wheel"},null,{"kind":null}]},
              {"id":"","name":"No id","steps":[]},
              {"id":"ok","name":"Duplicate id","steps":[]},
              null,
              {"name":"Missing id"}
            ]
            """);
        var only = Assert.Single(cfg.Macros);
        Assert.Equal("Fine", only.Name);
        Assert.Equal([MacroStep.Tap("Ctrl+C"), MacroStep.KeyDown("Shift"), MacroStep.Click("Left")], only.Steps);
    }

    [Fact]
    public void Names_are_trimmed_limited_and_defaulted_and_the_library_is_capped()
    {
        var long_ = new string('x', 200);
        var steps = string.Join(",", Enumerable.Range(0, MacroDef.MaxSteps + 25).Select(_ => """{"kind":"Pause","ms":50}"""));
        var cfg = Parse($$"""[{"id":"a","name":"{{long_}}","steps":[{{steps}}]},{"id":"b","name":"   ","steps":[]}]""");
        Assert.Equal(MacroDef.NameMax, cfg.Macros[0].Name.Length);
        Assert.Equal(MacroDef.MaxSteps, cfg.Macros[0].Steps.Count);
        Assert.Equal("Macro", cfg.Macros[1].Name);

        var many = string.Join(",", Enumerable.Range(0, MacroDef.MaxMacros + 10).Select(i => $$"""{"id":"m{{i}}","name":"M","steps":[]}"""));
        Assert.Equal(MacroDef.MaxMacros, Parse("[" + many + "]").Macros.Count);
    }

    [Fact]
    public void A_text_step_is_cut_at_its_limit_and_an_unknown_action_still_fails_the_file()
    {
        var cfg = Parse($$"""[{"id":"a","name":"A","steps":[{"kind":"Type","text":"{{new string('y', 5000)}}"}]}]""");
        Assert.Equal(MacroStep.TextMax, cfg.Macros[0].Steps[0].Text!.Length);
        Assert.Throws<FormatException>(() => JsonConfigStore.Parse("""{"version":2,"profiles":[{"bindings":{"0x0053":{"type":"macro"}}}]}"""));
    }

    [Fact]
    public void Macro_equality_compares_the_steps()
    {
        var a = new MacroDef("x", "X", [MacroStep.Pause(100)]);
        Assert.Equal(a, new MacroDef("x", "X", [MacroStep.Pause(100)]));
        Assert.NotEqual(a, new MacroDef("x", "X", [MacroStep.Pause(101)]));
        Assert.Equal(100, a.PauseTotalMs);
    }
}
