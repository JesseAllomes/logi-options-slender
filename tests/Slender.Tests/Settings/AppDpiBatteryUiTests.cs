using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Tray;

namespace Slender.Tests.Settings;

public class AppDpiBatteryUiTests
{
    private static SlenderConfig Cfg(int? global = null, params Profile[] apps) =>
        SlenderConfig.Default with
        {
            Pointer = new PointerSettings(Dpi: global),
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()), .. apps],
        };

    private static Profile App(string p, int? dpi = null) => new(p, new Dictionary<ushort, ActionSpec>()) { Dpi = dpi };

    [Fact]
    public void Setting_an_app_speed_snaps_it_and_leaves_other_apps_alone()
    {
        var cfg = Cfg(null, App("excel"), App("word", 800));
        var next = AppDpiModel.Set(cfg, "excel", 3333);
        Assert.Equal(3350, AppDpiModel.Own(next, "excel"));
        Assert.Equal(800, AppDpiModel.Own(next, "word"));
        Assert.Null(AppDpiModel.Own(cfg, "excel"));
    }

    [Fact]
    public void Clearing_goes_back_to_following_all_apps()
    {
        var cfg = Cfg(1600, App("excel", 3200));
        var next = AppDpiModel.Set(cfg, "excel", null);
        Assert.Null(AppDpiModel.Own(next, "excel"));
        Assert.Equal(1600, AppDpiModel.Inherited(next));
        Assert.Same(cfg, AppDpiModel.Set(cfg, "unknown", null)); // no profile made just to clear
    }

    [Fact]
    public void An_app_without_a_profile_gets_one_when_it_is_given_a_speed_and_values_clamp()
    {
        var next = AppDpiModel.Set(Cfg(), "excel", 99999);
        Assert.Equal(PointerMapping.DpiMax, AppDpiModel.Own(next, "excel"));
        Assert.Single(AppDpiModel.Apps(next));
    }

    [Fact]
    public void Inherited_is_the_all_apps_profile_then_the_global_speed()
    {
        Assert.Null(AppDpiModel.Inherited(Cfg()));
        Assert.Equal(1600, AppDpiModel.Inherited(Cfg(1600)));
        var all = Cfg(1600) with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { Dpi = 900 }] };
        Assert.Equal(900, AppDpiModel.Inherited(all));
    }

    [Fact]
    public void Battery_line_shows_level_and_the_estimate()
    {
        Assert.Equal("Not connected", BatteryLine.Text(null));
        Assert.Equal("Not connected", BatteryLine.Text(new DeviceState("m", "M", false, null, false)));
        Assert.Equal("80%", BatteryLine.Text(new DeviceState("m", "M", true, 80, false)));
        Assert.Equal("80% · About 12 days left", BatteryLine.Text(new DeviceState("m", "M", true, 80, false) { BatteryEstimate = "About 12 days left" }));
        Assert.Equal("80% · Not enough data yet", BatteryLine.Text(new DeviceState("m", "M", true, 80, false) { BatteryEstimate = "Not enough data yet" }));
        Assert.Equal("80% · charging", BatteryLine.Text(new DeviceState("m", "M", true, 80, true) { BatteryEstimate = "Charging" }));
    }

    [Fact]
    public void Tray_tooltip_stays_inside_the_limit_and_lists_connected_devices()
    {
        string tip = BatteryTip.Build([new TipDevice("MX Master 4", true, 80, false, "~12d"), new TipDevice("ERGO K860", true, 50, true, null)]);
        Assert.Equal("Slender\nMX Master 4 80% ~12d left\nERGO K860 50% charging", tip);
        Assert.Equal("Slender", BatteryTip.Build([new TipDevice("MX Master 4", false, null, false, null)]));
        string longName = BatteryTip.Build([new TipDevice(new string('x', 300), true, 80, false, "~12d")]);
        Assert.Equal(BatteryTip.MaxLength, longName.Length);
        Assert.EndsWith("…", longName);
    }

    [Fact]
    public void Menu_status_line_carries_the_estimate()
    {
        Assert.Equal("MX Master 4 — connected, 80%, ~12d left", TrayText.Device("MX Master 4", true, 80, false, "~12d"));
        Assert.Equal("MX Master 4 — connected, 80%", TrayText.Device("MX Master 4", true, 80, false, null));
        Assert.Equal("MX Master 4 — connected, 80% charging", TrayText.Device("MX Master 4", true, 80, true, "~12d"));
    }
}
