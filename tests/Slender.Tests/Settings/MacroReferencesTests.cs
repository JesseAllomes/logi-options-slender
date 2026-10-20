using Slender.Engine;
using Slender.Engine.Macros;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class MacroReferencesTests
{
    private static readonly ActionSpec M = new ActionSpec.Macro("m1");
    private static readonly ActionSpec Other = new ActionSpec.Macro("m2");
    private static readonly ActionSpec Off = new ActionSpec.None();

    private static SlenderConfig Config(params Profile[] profiles) => SlenderConfig.Default with
    {
        Profiles = profiles,
        Macros = [new MacroDef("m1", "One", []), new MacroDef("m2", "Two", [])],
    };

    private static Profile Prof(string? app = null) => new(app, new Dictionary<ushort, ActionSpec>());

    [Fact]
    public void Unused_macro_counts_zero_and_the_config_is_returned_as_is()
    {
        var c = Config(Prof() with { Bindings = new Dictionary<ushort, ActionSpec> { [1] = Other } });
        Assert.Equal(0, MacroReferences.Count(c, "m1"));
        Assert.Same(c, MacroReferences.Scrub(c, "m1", out int n));
        Assert.Equal(0, n);
    }

    [Fact]
    public void Mouse_buttons_keys_and_swipes_are_found_and_cleared()
    {
        var c = Config(Prof() with
        {
            Bindings = new Dictionary<ushort, ActionSpec> { [1] = M, [2] = Other },
            KeyBindings = new Dictionary<ushort, ActionSpec> { [3] = M },
            GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = M },
        });
        Assert.Equal(3, MacroReferences.Count(c, "m1"));
        var p = MacroReferences.Scrub(c, "m1", out _).Profiles[0];
        Assert.Equal(Off, p.Bindings[1]);
        Assert.Equal(Other, p.Bindings[2]);
        Assert.Equal(Off, p.KeyBindings[3]);
        Assert.Equal(Off, p.GestureSwipes[GestureDirection.Up]);
    }

    [Fact]
    public void Every_profile_is_searched()
    {
        var c = Config(
            Prof() with { Bindings = new Dictionary<ushort, ActionSpec> { [1] = M } },
            Prof("excel") with { KeyBindings = new Dictionary<ushort, ActionSpec> { [1] = M } });
        Assert.Equal(2, MacroReferences.Count(c, "m1"));
        Assert.Equal(0, MacroReferences.Count(MacroReferences.Scrub(c, "m1", out _), "m1"));
    }

    [Fact]
    public void Tap_hold_halves_clear_one_side_or_the_whole_binding()
    {
        var c = Config(Prof() with
        {
            Bindings = new Dictionary<ushort, ActionSpec>
            {
                [1] = new ActionSpec.TapHold(M, new ActionSpec.ActionsRing()),
                [2] = new ActionSpec.TapHold(new ActionSpec.MouseButton("Back"), M),
                [3] = new ActionSpec.TapHold(M, M),
            },
        });
        Assert.Equal(4, MacroReferences.Count(c, "m1"));
        var b = MacroReferences.Scrub(c, "m1", out _).Profiles[0].Bindings;
        Assert.Equal(new ActionSpec.TapHold(Off, new ActionSpec.ActionsRing()), b[1]);
        Assert.Equal(new ActionSpec.TapHold(new ActionSpec.MouseButton("Back"), Off), b[2]);
        Assert.Equal(Off, b[3]);
    }

    [Fact]
    public void Ring_slots_and_nested_folders_are_cleared_but_names_stay()
    {
        var slots = Enumerable.Repeat(RingSlot.Empty, Profile.RingSlotCount).ToList();
        slots[0] = new RingSlot(M) { Name = "Mine" };
        slots[1] = new RingSlot(Off) { Folder = [new RingSlot(M), new RingSlot(Other)] };
        slots[2] = new RingSlot(M) { Folder = [RingSlot.Empty] }; // a folder's own action is ignored
        var c = Config(Prof() with { RingSlots = slots });
        Assert.Equal(2, MacroReferences.Count(c, "m1"));
        var after = MacroReferences.Scrub(c, "m1", out _).Profiles[0].RingSlots!;
        Assert.Equal(Off, after[0].Action);
        Assert.Equal("Mine", after[0].Name);
        Assert.Equal(Off, after[1].Folder![0].Action);
        Assert.Equal(Other, after[1].Folder![1].Action);
    }

    [Fact]
    public void Thumb_wheel_second_action_and_flick_are_cleared()
    {
        var c = Config(Prof() with
        {
            ThumbWheelAlt = new ThumbWheelSecond(true, M, Other) { Speed = 150 },
            ThumbFlick = new ThumbWheelFlick(true, Other, M),
        });
        Assert.Equal(2, MacroReferences.Count(c, "m1"));
        var p = MacroReferences.Scrub(c, "m1", out _).Profiles[0];
        Assert.Equal(Off, p.ThumbWheelAlt!.Forward);
        Assert.Equal(Other, p.ThumbWheelAlt.Back);
        Assert.Equal(150, p.ThumbWheelAlt.Speed);
        Assert.Equal(Other, p.ThumbFlick!.Right);
        Assert.Equal(Off, p.ThumbFlick.Left);
    }

    [Fact]
    public void Delete_removes_the_macro_and_every_reference_but_keeps_the_others()
    {
        var c = Config(Prof() with { Bindings = new Dictionary<ushort, ActionSpec> { [1] = M, [2] = Other } });
        var after = MacroReferences.Delete(c, "m1");
        Assert.Equal(["m2"], after.Macros.Select(m => m.Id));
        Assert.Equal(Off, after.Profiles[0].Bindings[1]);
        Assert.Equal(Other, after.Profiles[0].Bindings[2]);
    }

    [Fact]
    public void Usage_text_says_how_many()
    {
        Assert.Contains("Not used", MacroReferences.UsageText(0));
        Assert.Contains("1 control.", MacroReferences.UsageText(1));
        Assert.Contains("5 controls", MacroReferences.UsageText(5));
    }
}
