using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tests.Ring;

public sealed class RingDefaultsTests
{
    [Fact]
    public void Defaults_are_the_optionsplus_ring()
    {
        IReadOnlyList<RingSlot> slots = RingDefaults.Slots();

        Assert.Equal(Profile.RingSlotCount, slots.Count);
        Assert.Equal(new ActionSpec.Keys("Ctrl+X"), slots[0].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+C"), slots[1].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+V"), slots[2].Action);
        Assert.Equal(new ActionSpec.MouseButton("Forward"), slots[3].Action);
        Assert.Equal(new ActionSpec.MouseButton("Back"), slots[4].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Z"), slots[5].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Y"), slots[6].Action);
        Assert.Equal("Folder", slots[7].Name);
        Assert.Equal(Profile.RingSlotCount, slots[7].Folder!.Count);
        Assert.All(slots[7].Folder!, s => Assert.True(s.IsEmpty));
    }

    [Fact]
    public void Effective_prefers_own_then_default_then_builtin()
    {
        RingSlot[] mine = [.. Enumerable.Repeat(RingSlot.Empty, 8)];
        RingSlot[] shared = [.. RingDefaults.Slots().Reverse()];
        var config = SlenderConfig.Default with
        {
            Profiles =
            [
                new Profile(null, new Dictionary<ushort, ActionSpec>()),
                new Profile("excel", new Dictionary<ushort, ActionSpec>()),
                new Profile("word", new Dictionary<ushort, ActionSpec>()) { RingSlots = mine },
            ],
        };

        Assert.Equal(RingDefaults.Slots().Count, RingDefaults.Effective(config, "excel").Count);
        Assert.Equal(new ActionSpec.Keys("Ctrl+X"), RingDefaults.Effective(config, "excel")[0].Action);

        config = config with { Profiles = [config.Profiles[0] with { RingSlots = shared }, .. config.Profiles.Skip(1)] };
        Assert.Same(shared, RingDefaults.Effective(config, "excel"));
        Assert.Same(mine, RingDefaults.Effective(config, "WORD"));
    }
}
