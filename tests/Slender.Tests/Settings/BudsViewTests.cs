using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class BudsViewTests
{
    private static BudsSetting S(string key, int raw)
    {
        BudsSettingDef def = GalaxyBudsSettingsCatalog.Find(key)!;
        return new BudsSetting(key, def.Group, def.Label, def.Text(raw), raw);
    }

    private static BudsState State(bool connected = true, params BudsSetting[] settings) => new(connected, Settings: settings);

    private static BudsRow? Find(BudsState b, string key) => BudsView.Groups(b).SelectMany(g => g.Rows).FirstOrDefault(r => r.Def.Key == key);

    [Fact]
    public void Unreported_settings_are_hidden_and_none_reported_means_no_groups()
    {
        Assert.Empty(BudsView.Groups(null));
        Assert.Empty(BudsView.Groups(new BudsState(true)));
        BudsState b = State(true, S(GalaxyBudsSettingsCatalog.SirenDetect, 1));
        Assert.Single(BudsView.Groups(b));
        Assert.Null(Find(b, GalaxyBudsSettingsCatalog.EqPreset));
        Assert.NotNull(Find(b, GalaxyBudsSettingsCatalog.SirenDetect));
    }

    [Fact]
    public void Voice_detect_timeout_follows_voice_detect()
    {
        Assert.Null(Find(State(true, S(GalaxyBudsSettingsCatalog.VoiceDetect, 0), S(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 1)), GalaxyBudsSettingsCatalog.VoiceDetectTimeout));
        Assert.NotNull(Find(State(true, S(GalaxyBudsSettingsCatalog.VoiceDetect, 1), S(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 1)), GalaxyBudsSettingsCatalog.VoiceDetectTimeout));
    }

    [Fact]
    public void Cycle_rows_show_only_when_touch_hold_switches_noise_controls()
    {
        BudsState other = State(true, S(GalaxyBudsSettingsCatalog.TouchHoldLeft, 1), S(GalaxyBudsSettingsCatalog.CycleLeft, 9));
        Assert.Null(Find(other, GalaxyBudsSettingsCatalog.CycleLeft));
        BudsState cycle = State(true, S(GalaxyBudsSettingsCatalog.TouchHoldLeft, 2), S(GalaxyBudsSettingsCatalog.CycleLeft, 9));
        Assert.NotNull(Find(cycle, GalaxyBudsSettingsCatalog.CycleLeft));
    }

    [Fact]
    public void Controls_follow_the_catalog_kind()
    {
        BudsState b = State(true,
            S(GalaxyBudsSettingsCatalog.SirenDetect, 1),
            S(GalaxyBudsSettingsCatalog.EqPreset, 2),
            S(GalaxyBudsSettingsCatalog.Balance, 16),
            S(GalaxyBudsSettingsCatalog.Gestures, 0),
            S(GalaxyBudsSettingsCatalog.AdaptiveVolume, 1),
            S(GalaxyBudsSettingsCatalog.AmbientLevel, 2));
        Assert.Equal(BudsControlKind.Toggle, Find(b, GalaxyBudsSettingsCatalog.SirenDetect)!.Kind);
        Assert.Equal(BudsControlKind.Choice, Find(b, GalaxyBudsSettingsCatalog.EqPreset)!.Kind);
        Assert.Equal(BudsControlKind.Choice, Find(b, GalaxyBudsSettingsCatalog.AmbientLevel)!.Kind);
        Assert.Equal(BudsControlKind.Slider, Find(b, GalaxyBudsSettingsCatalog.Balance)!.Kind);
        Assert.Equal(BudsControlKind.Text, Find(b, GalaxyBudsSettingsCatalog.Gestures)!.Kind);
        Assert.Equal(BudsControlKind.Text, Find(b, GalaxyBudsSettingsCatalog.AdaptiveVolume)!.Kind);
        Assert.Equal("Soft", Find(b, GalaxyBudsSettingsCatalog.EqPreset)!.Text);
        Assert.Equal("16", Find(b, GalaxyBudsSettingsCatalog.Balance)!.Text);
    }

    [Fact]
    public void Read_only_rows_are_never_enabled_and_disconnected_disables_everything()
    {
        BudsSetting[] list = [S(GalaxyBudsSettingsCatalog.SirenDetect, 1), S(GalaxyBudsSettingsCatalog.Balance, 16), S(GalaxyBudsSettingsCatalog.Gestures, 0)];
        BudsState on = State(true, list);
        Assert.True(Find(on, GalaxyBudsSettingsCatalog.SirenDetect)!.Enabled);
        Assert.True(Find(on, GalaxyBudsSettingsCatalog.Balance)!.Enabled);
        Assert.False(Find(on, GalaxyBudsSettingsCatalog.Gestures)!.Enabled);

        BudsState off = State(false, list);
        Assert.All(BudsView.Groups(off).SelectMany(g => g.Rows), r => Assert.False(r.Enabled));
        Assert.False(BudsView.IsConnected(off));
        Assert.False(BudsView.IsConnected(null));
    }

    [Fact]
    public void Groups_keep_catalog_order()
    {
        BudsState b = State(true, S(GalaxyBudsSettingsCatalog.AutoPause, 1), S(GalaxyBudsSettingsCatalog.SirenDetect, 0), S(GalaxyBudsSettingsCatalog.EqPreset, 0));
        Assert.Equal([GalaxyBudsSettingsCatalog.GroupNoise, GalaxyBudsSettingsCatalog.GroupSound, GalaxyBudsSettingsCatalog.GroupConnection], BudsView.Groups(b).Select(g => g.Name));
    }

    [Fact]
    public void A_held_change_replaces_the_report_and_drives_ShowWhen()
    {
        BudsState b = State(true, S(GalaxyBudsSettingsCatalog.VoiceDetect, 0), S(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 1));
        BudsState? held = BudsView.WithHeld(b, new Dictionary<string, int> { [GalaxyBudsSettingsCatalog.VoiceDetect] = 1, [GalaxyBudsSettingsCatalog.EqPreset] = 3 });
        Assert.NotNull(Find(held!, GalaxyBudsSettingsCatalog.VoiceDetectTimeout));
        Assert.Equal(1, Find(held!, GalaxyBudsSettingsCatalog.VoiceDetect)!.Raw);
        Assert.Null(Find(held!, GalaxyBudsSettingsCatalog.EqPreset)); // never reported, so still hidden
        Assert.Same(b, BudsView.WithHeld(b, new Dictionary<string, int>()));
        Assert.True(BudsView.ControlsOthers(GalaxyBudsSettingsCatalog.VoiceDetect));
        Assert.False(BudsView.ControlsOthers(GalaxyBudsSettingsCatalog.EqPreset));
    }

    [Fact]
    public void A_change_is_held_for_two_seconds()
    {
        DateTimeOffset t = DateTimeOffset.Now;
        Assert.True(BudsView.HoldSent(t, t.AddMilliseconds(1900)));
        Assert.False(BudsView.HoldSent(t, t.AddSeconds(2)));
    }

    [Theory]
    [InlineData("wearing", "In ear")]
    [InlineData("in case", "In case")]
    [InlineData("case closed", "Case closed")]
    [InlineData("idle", "Out of case")]
    [InlineData("disconnected", "Not connected")]
    [InlineData(null, "")]
    [InlineData("other", "Other")]
    public void Placement_words(string? placement, string expected) => Assert.Equal(expected, BudsView.PlacementText(placement));

    [Fact]
    public void Battery_text_and_lowest_bud()
    {
        Assert.Equal("80%", BudsView.BatteryText(80, false));
        Assert.Equal("80% charging", BudsView.BatteryText(80, true));
        Assert.Equal(BatteryGlyphModel.Dash, BudsView.BatteryText(null, null));
        Assert.Equal((40, true), BudsView.Lowest(new BudsState(true, BatteryLeft: 70, BatteryRight: 40, ChargingRight: true)));
        Assert.Equal((70, false), BudsView.Lowest(new BudsState(true, BatteryLeft: 70)));
        Assert.Equal((null, false), BudsView.Lowest(new BudsState(true, BatteryCase: 90)));
    }

    [Fact]
    public void Glyph_is_empty_when_greyed_or_not_connected()
    {
        var b = new BudsState(true, BatteryLeft: 50, BatteryRight: 30);
        Assert.Equal("30%", BudsView.Glyph(b, false, null, 15).Label);
        Assert.Equal(BatteryGlyphKind.Low, BudsView.Glyph(new BudsState(true, BatteryLeft: 10), false, null, 15).Kind);
        Assert.Equal(BatteryGlyphKind.Unknown, BudsView.Glyph(b, true, "Off", 15).Kind);
        Assert.Equal(BatteryGlyphKind.Unknown, BudsView.Glyph(b with { Connected = false }, false, null, 15).Kind);
    }

    [Fact]
    public void Firmware_line_and_device_request()
    {
        Assert.Equal("SM-R640 · Firmware 1.2", BudsView.FirmwareLine(new BudsState(true, Firmware: "1.2", Model: "SM-R640")));
        Assert.Null(BudsView.FirmwareLine(new BudsState(true)));
        Assert.Equal(new DeviceTarget(null, false, true), DeviceRequest.Target("galaxy-buds4-pro"));
        Assert.Equal("galaxy-buds4-pro", DeviceRequest.Parse(["--device", "galaxy-buds4-pro"]));
    }
}
