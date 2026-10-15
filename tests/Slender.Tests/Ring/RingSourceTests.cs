using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tests.Ring;

public class RingSourceTests
{
    private static Profile P(string? app, IReadOnlyList<RingSlot>? ring) =>
        new(app, new Dictionary<ushort, ActionSpec>()) { RingSlots = ring };

    private static SlenderConfig Cfg(params Profile[] profiles) => SlenderConfig.Default with { Profiles = profiles };

    private static RingSlot[] Slots(string chord) => Enumerable.Range(0, 8).Select(_ => new RingSlot(new ActionSpec.Keys(chord))).ToArray();

    [Fact]
    public void DefaultsAreTheOptionsPlusRing()
    {
        var d = RingDefaults.Slots();
        Assert.Equal(8, d.Count);
        Assert.Equal(["Cut", "Copy", "Paste", "Forward", "Back", "Undo", "Redo", "Folder"], d.Select(s => s.Name));
        Assert.Equal(new ActionSpec.Keys("Ctrl+X"), d[0].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+C"), d[1].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+V"), d[2].Action);
        Assert.Equal(new ActionSpec.MouseButton("Forward"), d[3].Action);
        Assert.Equal(new ActionSpec.MouseButton("Back"), d[4].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Z"), d[5].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Y"), d[6].Action);
        Assert.True(d[7].IsFolder);
        Assert.All(d[7].Folder!, s => Assert.True(s.IsEmpty));
    }

    [Fact]
    public void AppProfileRingWinsOverDefaultProfile()
    {
        var cfg = Cfg(P(null, Slots("F1")), P("excel", Slots("F2")));
        var src = RingSource.Resolve(cfg, "excel");
        Assert.Equal(RingSourceKind.AppProfile, src.Kind);
        Assert.All(src.Slots, s => Assert.Equal(new ActionSpec.Keys("F2"), s.Action));
    }

    [Fact]
    public void AppWithoutRingSlotsFollowsTheDefaultProfileAsAWholeRing()
    {
        var def = Enumerable.Range(0, 8).Select(i => i == 2 ? RingSlot.Empty : new RingSlot(new ActionSpec.Keys("F1"))).ToArray();
        var cfg = Cfg(P(null, def), P("excel", null));
        var src = RingSource.Resolve(cfg, "excel");
        Assert.Equal(RingSourceKind.DefaultProfile, src.Kind);
        Assert.Equal(def, src.Slots);
    }

    [Fact]
    public void UnknownAppUsesTheDefaultProfile()
    {
        var cfg = Cfg(P(null, Slots("F1")), P("excel", Slots("F2")));
        Assert.Equal(new ActionSpec.Keys("F1"), RingSource.Resolve(cfg, "notepad").Slots[0].Action);
        Assert.Equal(new ActionSpec.Keys("F1"), RingSource.Resolve(cfg, null).Slots[0].Action);
    }

    [Fact]
    public void NoRingSlotsAnywhereUsesBuiltInDefaults()
    {
        var src = RingSource.Resolve(Cfg(P(null, null), P("excel", null)), "excel");
        Assert.Equal(RingSourceKind.BuiltIn, src.Kind);
        Assert.Equal("Cut", src.Slots[0].Name);
        Assert.Equal(RingSourceKind.BuiltIn, RingSource.Resolve(SlenderConfig.Default, "excel").Kind);
    }

    [Fact]
    public void EmptySlotsInsideAnAppRingStayEmpty()
    {
        var own = new RingSlot[] { new(new ActionSpec.Keys("F2")) };
        var cfg = Cfg(P(null, Slots("F1")), P("excel", own));
        var src = RingSource.Resolve(cfg, "excel");
        Assert.Equal(8, src.Slots.Count);
        Assert.Equal(new ActionSpec.Keys("F2"), src.Slots[0].Action);
        Assert.All(src.Slots.Skip(1), s => Assert.True(s.IsEmpty)); // not filled from the default profile
    }

    [Theory]
    [InlineData("VolumeUp|VolumeDown", "VolumeUp", "VolumeDown")]
    [InlineData("WheelRight | WheelLeft", "WheelRight", "WheelLeft")]
    public void WheelPairKeys(string chord, string up, string down)
    {
        var pair = RingWheel.TryPair(new ActionSpec.Keys(chord));
        Assert.Equal((new ActionSpec.Keys(up), new ActionSpec.Keys(down)), pair);
    }

    [Fact]
    public void WheelPairBrightnessIsPlusAndMinusStep()
    {
        Assert.Equal((new ActionSpec.Brightness(10), new ActionSpec.Brightness(-10)), RingWheel.TryPair(new ActionSpec.Brightness(-10)));
        Assert.Null(RingWheel.TryPair(new ActionSpec.Brightness(0)));
    }

    [Theory]
    [InlineData("Ctrl+C")]
    [InlineData("|Up")]
    public void NotAdjustable(string chord) => Assert.Null(RingWheel.TryPair(new ActionSpec.Keys(chord)));

    [Fact]
    public void NotAdjustableOtherActions()
    {
        Assert.Null(RingWheel.TryPair(new ActionSpec.MouseButton("Back")));
        Assert.Null(RingWheel.TryPair(new ActionSpec.Text("x")));
        Assert.Null(RingWheel.TryPair(new ActionSpec.None()));
    }
}
