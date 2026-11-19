using Slender.Engine.Devices;
using Xunit;

namespace Slender.Tests.Engine;

public class GalaxyBudsSettingsCatalogTests
{
    [Fact]
    public void KeysAreUnique()
    {
        var keys = GalaxyBudsSettingsCatalog.All.Select(d => d.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void ShowWhenPointsAtKnownKeys()
    {
        foreach (var d in GalaxyBudsSettingsCatalog.All)
            if (d.ShowWhenKey is { } k) Assert.NotNull(GalaxyBudsSettingsCatalog.Find(k));
    }

    [Fact]
    public void AcceptsOnlyListedChoicesAndRange()
    {
        var mode = GalaxyBudsSettingsCatalog.Find(GalaxyBudsSettingsCatalog.NoiseMode)!;
        Assert.True(mode.Accepts(3));
        Assert.False(mode.Accepts(4));
        Assert.Equal("Adaptive", mode.Text(3));
        var balance = GalaxyBudsSettingsCatalog.Find(GalaxyBudsSettingsCatalog.Balance)!;
        Assert.True(balance.Accepts(16));
        Assert.False(balance.Accepts(33));
        Assert.False(GalaxyBudsSettingsCatalog.Find(GalaxyBudsSettingsCatalog.Gestures)!.CanSet);
        var cycle = GalaxyBudsSettingsCatalog.Find(GalaxyBudsSettingsCatalog.CycleLeft)!;
        Assert.False(cycle.Accepts(8)); // one mode alone is not a cycle
        Assert.True(cycle.Accepts(9));
    }
}
