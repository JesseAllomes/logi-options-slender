using Slender.Engine.Devices;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class DeviceTrayTextTests
{
    [Fact]
    public void Line_follows_the_registry()
    {
        Assert.Null(TrayText.Line("MX Master 4", DevicePresence.Hidden, false, null, false));
        Assert.Equal("MX Master 4 — asleep", TrayText.Line("MX Master 4", DevicePresence.Sleeping, false, null, false));
        Assert.Equal("MX Master 4 — disconnected", TrayText.Line("MX Master 4", DevicePresence.Disconnected, false, null, false));
        Assert.Equal("MX Master 4 — connected, 80%", TrayText.Line("MX Master 4", DevicePresence.Active, true, 80, false));
        Assert.Equal("MX Master 4 — not connected", TrayText.Line("MX Master 4", DevicePresence.Active, false, null, false));
    }

    [Fact]
    public void Line_without_a_registry_is_todays_text()
    {
        Assert.Equal(TrayText.Device("ERGO K860", false, null, false), TrayText.Line("ERGO K860", null, false, null, false));
        Assert.Equal(TrayText.Device("ERGO K860", true, 50, true), TrayText.Line("ERGO K860", null, true, 50, true));
    }

    [Fact]
    public void Tooltip_has_no_line_for_hidden_and_names_asleep_and_disconnected()
    {
        string tip = BatteryTip.Build(
        [
            new TipDevice("MX Master 4", false, null, false, null, DevicePresence.Sleeping),
            new TipDevice("ERGO K860", false, null, false, null, DevicePresence.Hidden),
        ]);
        Assert.Equal("Slender\nMX Master 4 asleep", tip);

        Assert.Equal("Slender\nERGO K860 disconnected", BatteryTip.Build([new TipDevice("ERGO K860", false, null, false, null, DevicePresence.Disconnected)]));
        Assert.Equal("Slender", BatteryTip.Build([new TipDevice("ERGO K860", true, 50, false, null, DevicePresence.Hidden)]));
    }

    [Fact]
    public void Tooltip_without_a_registry_is_unchanged_and_capped()
    {
        Assert.Equal("Slender", BatteryTip.Build([new TipDevice("MX Master 4", false, null, false, null)]));
        Assert.True(BatteryTip.Build([new TipDevice(new string('x', 300), false, null, false, null, DevicePresence.Sleeping)]).Length <= BatteryTip.MaxLength);
    }
}
