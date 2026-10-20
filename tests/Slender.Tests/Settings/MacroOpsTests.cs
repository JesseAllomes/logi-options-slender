using Slender.Engine;
using Slender.Engine.Macros;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class MacroOpsTests
{
    private static MacroDef Three() => new("m", "M", [MacroStep.Tap("A"), MacroStep.Pause(100), MacroStep.Tap("B")]);

    [Fact]
    public void New_macros_get_the_first_free_name_and_a_fresh_id()
    {
        var cfg = SlenderConfig.Default;
        var first = MacroOps.NewMacro(cfg);
        Assert.Equal("Macro 1", first.Name);
        cfg = MacroOps.Upsert(cfg, first);
        var second = MacroOps.NewMacro(cfg);
        Assert.Equal("Macro 2", second.Name);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Upsert_adds_then_replaces_and_normalises()
    {
        var cfg = MacroOps.Upsert(SlenderConfig.Default, Three());
        cfg = MacroOps.Upsert(cfg, Three() with { Name = "  Renamed  ", Steps = [MacroStep.Pause(1)] });
        var only = Assert.Single(cfg.Macros);
        Assert.Equal("Renamed", only.Name);
        Assert.Equal(MacroStep.PauseMinMs, only.Steps[0].Ms); // a pause below the floor is lifted to it
        Assert.Same(cfg, MacroOps.Upsert(cfg, new MacroDef("", "no id", [])));
    }

    [Fact]
    public void Remove_drops_only_that_macro()
    {
        var cfg = MacroOps.Upsert(MacroOps.Upsert(SlenderConfig.Default, Three()), new MacroDef("n", "N", []));
        Assert.Equal(["n"], MacroOps.Remove(cfg, "m").Macros.Select(m => m.Id));
        Assert.Same(cfg, MacroOps.Remove(cfg, "missing"));
    }

    [Fact]
    public void Steps_insert_delete_duplicate_and_move()
    {
        var m = Three();
        Assert.Equal(["Press A", "Pause 250 ms", "Pause 100 ms", "Press B"], MacroOps.Insert(m, 1, MacroStep.Pause(250)).Steps.Select(MacroOps.Describe));
        Assert.Equal(4, MacroOps.Insert(m, 99, MacroStep.Click("Left")).Steps.Count); // clamped to the end
        Assert.Equal(["Press A", "Press B"], MacroOps.Delete(m, 1).Steps.Select(MacroOps.Describe));
        Assert.Same(m, MacroOps.Delete(m, 7));
        Assert.Equal(["Press A", "Press A", "Pause 100 ms", "Press B"], MacroOps.Duplicate(m, 0).Steps.Select(MacroOps.Describe));
        Assert.Equal([MacroStep.Tap("B"), MacroStep.Pause(100), MacroStep.Tap("A")], MacroOps.Move(MacroOps.Move(m, 2, 0), 2, 1).Steps);
        Assert.Same(m, MacroOps.Move(m, 0, 9));
    }

    [Fact]
    public void A_full_macro_takes_no_more_steps()
    {
        var full = new MacroDef("f", "F", Enumerable.Repeat(MacroStep.Pause(10), MacroDef.MaxSteps).ToList());
        Assert.Same(full, MacroOps.Insert(full, 0, MacroStep.Pause(10)));
        Assert.Equal(MacroDef.MaxSteps, MacroOps.InsertRange(full, 0, [MacroStep.Pause(20)]).Steps.Count);
        Assert.Equal(MacroDef.MaxSteps, MacroOps.InsertRange(Three(), 0, Enumerable.Repeat(MacroStep.Pause(10), 1000).ToList()).Steps.Count);
    }

    [Fact]
    public void Pause_length_is_clamped_and_only_applies_to_pauses()
    {
        var m = Three();
        Assert.Equal(60_000, MacroOps.SetPause(m, 1, 9_000_000).Steps[1].Ms);
        Assert.Equal(10, MacroOps.SetPause(m, 1, 0).Steps[1].Ms);
        Assert.Equal(1234, MacroOps.SetPause(m, 1, 1234).Steps[1].Ms);
        Assert.Same(m, MacroOps.SetPause(m, 0, 500)); // step 0 is a tap
    }

    [Fact]
    public void Every_kind_you_can_add_starts_as_a_valid_step()
    {
        foreach (var (kind, title) in MacroOps.AddChoices)
        {
            var step = MacroOps.Default(kind);
            Assert.Equal(kind, step.Kind);
            Assert.NotNull(step.Normalized());
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.False(string.IsNullOrWhiteSpace(MacroOps.Describe(step)));
        }
    }

    [Theory]
    [InlineData(250, "250 ms")]
    [InlineData(1500, "1.5 s")]
    [InlineData(2000, "2 s")]
    [InlineData(12_400, "12 s")]
    [InlineData(60_000, "1 min")]
    [InlineData(65_000, "1 min 5 s")]
    public void Pauses_read_naturally(long ms, string text) => Assert.Equal(text, MacroOps.PauseText(ms));

    [Fact]
    public void Summary_mentions_steps_pauses_and_repeat_mode()
    {
        Assert.Equal("3 steps · 100 ms of pauses", MacroOps.Summary(Three()));
        Assert.Equal("1 step · repeat while held", MacroOps.Summary(new MacroDef("x", "X", [MacroStep.Click("Left")], MacroRunMode.WhileHeld)));
    }

    [Fact]
    public void Labels_use_the_macro_name_when_known_else_Macro()
    {
        var old = MacroLabels.Resolver;
        try
        {
            MacroLabels.Resolver = id => id == "m" ? "Sign off" : null;
            Assert.Equal("Macro: Sign off", ActionCatalog.Describe(new ActionSpec.Macro("m")).Text);
            Assert.Equal("Macro: Macro", ActionCatalog.Describe(new ActionSpec.Macro("gone")).Text);
            Assert.Equal("E768", RingGlyphs.AutoGlyph(new RingSlot(new ActionSpec.Macro("m"))));
        }
        finally { MacroLabels.Resolver = old; }
    }
}

public class MacroRecorderModelTests
{
    [Fact]
    public void The_real_gaps_become_pauses_and_the_first_wait_is_dropped()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Key(1000, 0x41, true);
        r.Key(1050, 0x41, false);   // A: a tap
        r.Key(1650, 0x42, true);    // 600 ms later
        r.Key(1700, 0x42, false);
        r.Key(1710, 0x43, true);    // 10 ms: too small for a pause
        r.Key(1760, 0x43, false);
        Assert.Equal([MacroStep.Tap("A"), MacroStep.Pause(600), MacroStep.Tap("B"), MacroStep.Tap("C")], r.Stop(2000));
    }

    [Fact]
    public void A_slow_press_stays_down_and_up_with_the_hold_as_a_pause()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Key(0, 0x41, true);
        r.Key(900, 0x41, false);
        Assert.Equal([MacroStep.KeyDown("A"), MacroStep.Pause(900), MacroStep.KeyUp("A")], r.Stop(1000));
    }

    [Fact]
    public void Modifiers_stay_down_and_up_around_a_tap()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Key(0, 0xA2, true);   // Ctrl
        r.Key(30, 0x43, true);  // C
        r.Key(80, 0x43, false);
        r.Key(120, 0xA2, false);
        Assert.Equal([MacroStep.KeyDown("Ctrl"), MacroStep.Pause(30), MacroStep.Tap("C"), MacroStep.Pause(40), MacroStep.KeyUp("Ctrl")], r.Stop(500));
    }

    [Fact]
    public void Auto_repeat_and_unnamed_keys_are_ignored_but_counted()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Key(0, 0x41, true);
        r.Key(30, 0x41, true);  // auto-repeat
        r.Key(60, 0x41, true);
        r.Key(90, 0x41, false);
        r.Key(100, 0xFF, true); // no name
        r.Key(110, 0xFF, false);
        Assert.Equal([MacroStep.Tap("A")], r.Stop(200));
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Clicks_wheel_and_relative_moves_are_recorded_with_their_gaps()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Pointer(0, 10, 10);                     // the starting point only
        r.Pointer(100, 60, 30);                   // a move of (50, 20)
        r.Button(300, "Left", true, 60, 30);
        r.Button(340, "Left", false, 60, 30);     // quick: one click
        r.Wheel(1000, -3, 0);
        Assert.Equal(
            [MacroStep.MoveBy(50, 20), MacroStep.Pause(200), MacroStep.Click("Left"), MacroStep.Pause(660), MacroStep.Wheel(-3)],
            r.Stop(1200));
    }

    [Fact]
    public void A_drag_keeps_the_button_down_and_the_moves_between()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Pointer(0, 0, 0);
        r.Button(100, "Left", true, 0, 0);
        r.Pointer(200, 40, 0);
        r.Button(400, "Left", false, 90, 10);
        var steps = r.Stop(500);
        Assert.Equal(MacroStepKind.MouseDown, steps[0].Kind);
        Assert.Equal(MacroStepKind.MouseUp, steps[^1].Kind);
        Assert.Contains(steps, s => s.Kind == MacroStepKind.Move);
        Assert.Equal(90, steps.Where(s => s.Kind == MacroStepKind.Move).Sum(s => s.X));
    }

    [Fact]
    public void Moves_are_throttled_and_can_be_switched_off()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Pointer(0, 0, 0);
        for (int i = 1; i <= 10; i++) r.Pointer(i * 5, i * 10, 0); // 50 ms of movement: one sample, then the final travel on stop
        var steps = r.Stop(100);
        Assert.InRange(steps.Count(s => s.Kind == MacroStepKind.Move), 1, 3);
        Assert.Equal(100, steps.Where(s => s.Kind == MacroStepKind.Move).Sum(s => s.X)); // nothing lost

        var off = new MacroRecorderModel(recordMoves: false);
        off.Start();
        off.Pointer(0, 0, 0);
        off.Pointer(200, 500, 500);
        Assert.Empty(off.Stop(300));
    }

    [Fact]
    public void Nothing_is_recorded_before_Start_or_after_Stop()
    {
        var r = new MacroRecorderModel();
        r.Key(0, 0x41, true);
        Assert.Empty(r.Stop(10));
        r.Start();
        r.Stop(10);
        r.Key(20, 0x41, true);
        Assert.Equal(0, r.EventCount);
        Assert.False(r.IsRunning);
    }

    [Fact]
    public void A_very_long_gap_is_capped_at_the_longest_pause_and_the_result_is_valid()
    {
        var r = new MacroRecorderModel();
        r.Start();
        r.Key(0, 0x41, true);
        r.Key(10, 0x41, false);
        r.Key(10_000_000, 0x42, true);
        r.Key(10_000_050, 0x42, false);
        var steps = r.Stop(10_000_100);
        Assert.Equal(MacroStep.PauseMaxMs, steps[1].Ms);
        Assert.All(steps, s => Assert.NotNull(s.Normalized()));
    }

    [Fact]
    public void Recording_stops_taking_events_at_the_step_limit()
    {
        var r = new MacroRecorderModel();
        r.Start();
        for (int i = 0; i < MacroDef.MaxSteps + 50; i++) r.Wheel(i * 100, 1, 0);
        Assert.True(r.Full);
        Assert.Equal(MacroDef.MaxSteps, r.EventCount);
        Assert.True(r.Stop(1_000_000).Count <= MacroDef.MaxSteps);
    }
}
