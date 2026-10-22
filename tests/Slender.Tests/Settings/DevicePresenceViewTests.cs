using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class DevicePresenceViewTests
{
    private const string Mouse = DeviceIds.Mouse;
    private const string Kb = DeviceIds.Keyboard;

    private static DeviceState Dev(string id, bool connected, string? presence) =>
        new(id, id, connected, 80, false) { Presence = presence };

    private static SlenderStatus Status(params DeviceState[] devices) => new(devices, false, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Registry_aware_tray_hides_a_device_it_does_not_list()
    {
        SlenderStatus s = Status(Dev(Mouse, true, "active"));
        Assert.True(DevicePresenceView.Visible(s, Mouse));
        Assert.False(DevicePresenceView.Visible(s, Kb));
    }

    [Fact]
    public void Legacy_tray_lists_both_and_both_are_visible()
    {
        SlenderStatus s = Status(Dev(Mouse, true, null), Dev(Kb, false, null));
        Assert.False(DevicePresenceView.RegistryAware(s));
        Assert.True(DevicePresenceView.Visible(s, Mouse));
        Assert.True(DevicePresenceView.Visible(s, Kb));
        Assert.True(DevicePresenceView.Visible(null, Kb));
    }

    [Fact]
    public void Active_is_shown_normally()
    {
        SlenderStatus s = Status(Dev(Mouse, true, "active"));
        Assert.False(DevicePresenceView.Dimmed(s, Mouse));
        Assert.False(DevicePresenceView.Removable(s, Mouse));
        Assert.Equal("Connected", DevicePresenceView.Subtitle(s, Mouse));
        Assert.Equal(string.Empty, DevicePresenceView.AccessibleSuffix(s, Mouse));
        Assert.Null(DevicePresenceView.InfoBar(s, Mouse, "MX", true));
    }

    [Fact]
    public void Sleeping_is_not_dimmed_and_says_asleep()
    {
        SlenderStatus s = Status(Dev(Mouse, false, "sleeping"));
        Assert.False(DevicePresenceView.Dimmed(s, Mouse));
        Assert.False(DevicePresenceView.Removable(s, Mouse));
        Assert.Equal("Asleep", DevicePresenceView.Subtitle(s, Mouse));
        Assert.Equal("Asleep", DevicePresenceView.BatteryWord(s, Mouse));
        Assert.Equal(("MX is asleep", "Changes apply when it wakes."), DevicePresenceView.InfoBar(s, Mouse, "MX", true));
    }

    [Fact]
    public void Disconnected_is_dimmed_and_removable()
    {
        SlenderStatus s = Status(Dev(Mouse, true, "active"), Dev(Kb, false, "disconnected"));
        Assert.True(DevicePresenceView.Dimmed(s, Kb));
        Assert.True(DevicePresenceView.Removable(s, Kb));
        Assert.False(DevicePresenceView.Removable(s, Mouse));
        Assert.Equal("Disconnected", DevicePresenceView.Subtitle(s, Kb));
        Assert.Equal(", disconnected", DevicePresenceView.AccessibleSuffix(s, Kb));
        Assert.NotNull(DevicePresenceView.InfoBar(s, Kb, "K", false));
    }

    [Fact]
    public void Legacy_keeps_connected_based_behaviour()
    {
        SlenderStatus s = Status(Dev(Mouse, true, null), Dev(Kb, false, null));
        Assert.False(DevicePresenceView.Dimmed(s, Mouse));
        Assert.True(DevicePresenceView.Dimmed(s, Kb));
        Assert.False(DevicePresenceView.Removable(s, Kb));
        Assert.Equal("Connected", DevicePresenceView.Subtitle(s, Mouse));
        Assert.Equal("Not connected", DevicePresenceView.Subtitle(s, Kb));
        Assert.Null(DevicePresenceView.BatteryWord(s, Kb));
        Assert.Equal(", not connected", DevicePresenceView.AccessibleSuffix(s, Kb));
        Assert.Equal("Slender doesn't control this keyboard yet. Your changes are saved for when it does.", DevicePresenceView.InfoBar(s, Kb, "K", false)!.Value.Message);
    }

    [Fact]
    public void Tray_down_and_unseen_device_use_the_old_words()
    {
        Assert.True(DevicePresenceView.Dimmed(null, Mouse));
        Assert.Equal("Slender isn't running", DevicePresenceView.Subtitle(null, Mouse));
        Assert.Equal("Not seen yet", DevicePresenceView.Subtitle(Status(), Mouse));
    }
}
