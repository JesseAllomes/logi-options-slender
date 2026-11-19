using System.Text;
using Slender.Engine.Devices;
using Slender.GalaxyBuds;

namespace Slender.Tests.GalaxyBuds;

public sealed class BudsStatusParserTests
{
    private static int Raw(BudsModel m, string key) => m.Get(key) ?? throw new InvalidOperationException(key + " not reported");

    [Fact]
    public void RealExtendedStatusParses()
    {
        var m = BudsTestData.ModelFromEsu();
        Assert.Equal(100, m.BatteryLeft);
        Assert.Equal(100, m.BatteryRight);
        Assert.Equal(95, m.BatteryCase);
        Assert.Equal(BudsPlacement.InCase, m.PlacementLeft);
        Assert.Equal(BudsPlacement.InCase, m.PlacementRight);
        Assert.False(m.ChargingLeft);
        Assert.False(m.ChargingRight);
        Assert.False(m.ChargingCase);
        Assert.Equal(0, Raw(m, GalaxyBudsSettingsCatalog.NoiseMode));
        Assert.Equal(4, Raw(m, GalaxyBudsSettingsCatalog.AmbientLevel));
        Assert.Equal(3, Raw(m, GalaxyBudsSettingsCatalog.EqPreset));
        Assert.Equal(9, Raw(m, GalaxyBudsSettingsCatalog.CycleLeft));
        Assert.Equal(9, Raw(m, GalaxyBudsSettingsCatalog.CycleRight));
        Assert.Equal(16, Raw(m, GalaxyBudsSettingsCatalog.Balance));
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.Audio360));
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.HeadTracking));
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.AdaptiveEq));
        Assert.Equal(0, Raw(m, GalaxyBudsSettingsCatalog.Sidetone));
        Assert.Equal(2, Raw(m, GalaxyBudsSettingsCatalog.TouchHoldLeft));
        Assert.Equal(2, Raw(m, GalaxyBudsSettingsCatalog.TouchHoldRight));
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.AmbientCustomLeft));
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.AmbientCustomRight));
        Assert.Equal(2, Raw(m, GalaxyBudsSettingsCatalog.AmbientCustomTone));
    }

    [Fact]
    public void InEarCallsAndSeamlessAreInvertedFromTheWire()
    {
        var m = BudsTestData.ModelFromEsu();
        // Wire 0 = on, so the logical raw is 1.
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.InEarCalls));
        Assert.Equal(1, Raw(m, GalaxyBudsSettingsCatalog.Seamless));

        var p = BudsTestData.Hex(BudsTestData.EsuPayloadHex);
        p[34] = 1;
        p[19] = 1;
        var off = new BudsModel();
        BudsStatusParser.Apply(off, new BudsFrame(97, p));
        Assert.Equal(0, Raw(off, GalaxyBudsSettingsCatalog.InEarCalls));
        Assert.Equal(0, Raw(off, GalaxyBudsSettingsCatalog.Seamless));
    }

    [Fact]
    public void StateListsEveryCatalogSettingWithTextAndGestures()
    {
        var state = BudsTestData.ModelFromEsu().ToState(true, false, null);
        Assert.True(state.Connected);
        Assert.Equal("in case", state.PlacementLeft);
        Assert.Equal(GalaxyBudsSettingsCatalog.All.Select(d => d.Key), state.Settings!.Select(s => s.Key));
        Assert.Equal("Dynamic", state.Settings!.First(s => s.Key == GalaxyBudsSettingsCatalog.EqPreset).Value);
        Assert.Equal("Off", state.Settings!.First(s => s.Key == GalaxyBudsSettingsCatalog.NoiseMode).Value);
        Assert.Equal("All on", state.Settings!.First(s => s.Key == GalaxyBudsSettingsCatalog.Gestures).Value);
        Assert.Equal("Noise cancelling, Ambient", state.Settings!.First(s => s.Key == GalaxyBudsSettingsCatalog.CycleLeft).Value);
    }

    [Fact]
    public void GestureTextListsEnabledOnes()
    {
        Assert.Equal("All off", BudsModel.GesturesText(0));
        Assert.Equal("Single tap, Double tap", BudsModel.GesturesText(0x88));
    }

    [Fact]
    public void ChargingBitsAndStatusUpdate()
    {
        var m = new BudsModel();
        // 96: [rev, battL, battR, coupled, main, placement, caseBatt, chargingBits]; L and case charging.
        BudsStatusParser.Apply(m, new BudsFrame(96, [4, 80, 70, 1, 1, 0x12, 60, 0x11]));
        Assert.Equal(80, m.BatteryLeft);
        Assert.Equal(70, m.BatteryRight);
        Assert.Equal(60, m.BatteryCase);
        Assert.Equal(BudsPlacement.Wearing, m.PlacementLeft);
        Assert.Equal(BudsPlacement.Idle, m.PlacementRight);
        Assert.True(m.ChargingLeft);
        Assert.False(m.ChargingRight);
        Assert.True(m.ChargingCase);
        Assert.True(m.AnyWorn);

        BudsStatusParser.Apply(m, new BudsFrame(97, [.. BudsTestData.Hex(BudsTestData.EsuPayloadHex)[..43], 0x04, .. new byte[18]]));
        Assert.True(m.ChargingRight);
        Assert.False(m.ChargingLeft);
    }

    [Fact]
    public void NoiseUpdateSetsTheMode()
    {
        var m = BudsTestData.ModelFromEsu();
        Assert.True(BudsStatusParser.Apply(m, new BudsFrame(119, BudsTestData.Hex("0233010009090202010202"))));
        Assert.Equal(2, Raw(m, GalaxyBudsSettingsCatalog.NoiseMode));
    }

    [Fact]
    public void FirmwareAndModelStrings()
    {
        var m = new BudsModel();
        var version = BudsTestData.Hex(BudsTestData.VersionFrameHex);
        BudsStatusParser.Apply(m, new BudsFrame(104, version[4..^3]));
        Assert.Equal("R640XXU0AZD2", m.Firmware);

        // 34: a 14-character SKU per bud (padded here), serial-free.
        var sku = new byte[28];
        Encoding.ASCII.GetBytes("SM-R640NZAAXXX").CopyTo(sku, 0);
        Encoding.ASCII.GetBytes("SM-R640NZAAXXX").CopyTo(sku, 14);
        BudsStatusParser.Apply(m, new BudsFrame(34, sku));
        Assert.Equal("SM-R640", m.Model);
    }

    [Fact]
    public void ShortAndUnknownMessagesAreIgnored()
    {
        var m = new BudsModel();
        Assert.False(BudsStatusParser.Apply(m, new BudsFrame(242, [1, 2, 3])));
        BudsStatusParser.Apply(m, new BudsFrame(97, [4, 0x0d]));
        BudsStatusParser.Apply(m, new BudsFrame(96, [1]));
        BudsStatusParser.Apply(m, new BudsFrame(104, [1]));
        Assert.Null(m.BatteryLeft);
        Assert.Null(m.Firmware);
    }

    [Fact]
    public void ShorterExtendedStatusReportsOnlyWhatItCarries()
    {
        var p = BudsTestData.Hex(BudsTestData.EsuPayloadHex)[..30];
        var m = new BudsModel();
        BudsStatusParser.Apply(m, new BudsFrame(97, p));
        Assert.Equal(100, m.BatteryLeft);
        Assert.Null(m.Get(GalaxyBudsSettingsCatalog.Sidetone));
        Assert.Equal(4, m.Get(GalaxyBudsSettingsCatalog.AmbientLevel));
    }

    [Fact]
    public void ImplausibleBatteryIsNotShown()
    {
        var m = new BudsModel();
        BudsStatusParser.Apply(m, new BudsFrame(96, [4, 255, 50, 1, 1, 0x33, 0xFF, 0]));
        Assert.Null(m.BatteryLeft);
        Assert.Equal(50, m.BatteryRight);
        Assert.Null(m.BatteryCase);
    }
}
