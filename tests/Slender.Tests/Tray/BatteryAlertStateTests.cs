using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class BatteryAlertStateTests
{
    [Fact]
    public void Fires_once_while_below_threshold()
    {
        var s = new BatteryAlertState();
        Assert.False(s.ShouldNotify("mouse", 40, false, true, 15));
        Assert.True(s.ShouldNotify("mouse", 15, false, true, 15));
        Assert.False(s.ShouldNotify("mouse", 14, false, true, 15));
    }

    [Fact]
    public void Rearms_when_charging()
    {
        var s = new BatteryAlertState();
        Assert.True(s.ShouldNotify("mouse", 10, false, true, 15));
        Assert.False(s.ShouldNotify("mouse", 10, true, true, 15));
        Assert.True(s.ShouldNotify("mouse", 9, false, true, 15));
    }

    [Fact]
    public void Rearms_when_level_rises_above_threshold()
    {
        var s = new BatteryAlertState();
        Assert.True(s.ShouldNotify("mouse", 10, false, true, 15));
        Assert.False(s.ShouldNotify("mouse", 80, false, true, 15));
        Assert.True(s.ShouldNotify("mouse", 15, false, true, 15));
    }

    [Fact]
    public void Ignores_disconnected_and_unknown_level()
    {
        var s = new BatteryAlertState();
        Assert.False(s.ShouldNotify("mouse", 5, false, false, 15));
        Assert.False(s.ShouldNotify("mouse", null, false, true, 15));
        Assert.True(s.ShouldNotify("mouse", 5, false, true, 15));
    }

    [Fact]
    public void Disabled_never_fires()
    {
        var s = new BatteryAlertState();
        Assert.False(s.ShouldNotify("mouse", 5, false, true, 15, enabled: false));
    }

    [Fact]
    public void Devices_are_tracked_independently()
    {
        var s = new BatteryAlertState();
        Assert.True(s.ShouldNotify("mouse", 5, false, true, 15));
        Assert.True(s.ShouldNotify("keyboard", 5, false, true, 10));
    }
}
