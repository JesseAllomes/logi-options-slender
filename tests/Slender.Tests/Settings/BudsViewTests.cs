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
    public void Redraw_keys_ignore_a_repeat_or_an_echo_of_the_users_change()
    {
        Dictionary<string, int> Raws(BudsState r, IReadOnlyDictionary<string, int>? held = null) =>
            BudsView.SettingRaws(BudsView.WithHeld(r, held ?? new Dictionary<string, int>()));

        BudsState before = State(true, S(GalaxyBudsSettingsCatalog.SirenDetect, 0)) with { BatteryLeft = 80 };
        BudsState repeat = State(true, S(GalaxyBudsSettingsCatalog.SirenDetect, 0)) with { BatteryLeft = 80 };
        BudsState after = State(true, S(GalaxyBudsSettingsCatalog.SirenDetect, 1)) with { BatteryLeft = 80 };
        var flipped = new Dictionary<string, int> { [GalaxyBudsSettingsCatalog.SirenDetect] = 1 };

        Assert.True(BudsView.SameRaws(Raws(before), Raws(repeat))); // new list, same values: no redraw
        Assert.True(BudsView.SameRaws(Raws(before, flipped), Raws(after, flipped))); // the buds confirm the flip while it is held
        Assert.True(BudsView.SameRaws(Raws(before, flipped), Raws(after))); // and after the hold ends
        Assert.False(BudsView.SameRaws(Raws(before), Raws(after))); // a change made elsewhere still redraws
        Assert.False(BudsView.SameRaws(Raws(before, flipped), Raws(before))); // the buds did not take it: the switch goes back

        // The tray writes some values in its own words (gestures); only the raw value counts.
        BudsState worded = before with { Settings = [before.Settings![0] with { Value = "something else" }] };
        Assert.True(BudsView.SameRaws(Raws(before), Raws(worded)));

        string Key(BudsState? r, bool connected = true) => BudsView.StateKey(r, connected, true, "Connected");
        Assert.Equal(Key(before), Key(after)); // setting values are not part of the state key
        Assert.NotEqual(Key(before), Key(before with { BatteryLeft = 79 }));
        Assert.NotEqual(Key(before), Key(before with { WriteProblem = "No" }));
        Assert.NotEqual(Key(before), Key(before with { FindActive = true }));
        Assert.NotEqual(Key(before), Key(before, connected: false));
        Assert.NotEqual(Key(null), Key(before));
    }

    [Fact]
    public void Rows_key_changes_only_when_rows_appear_or_go()
    {
        BudsState off = State(true, S(GalaxyBudsSettingsCatalog.VoiceDetect, 0), S(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 1), S(GalaxyBudsSettingsCatalog.SirenDetect, 0));
        BudsState on = State(true, S(GalaxyBudsSettingsCatalog.VoiceDetect, 1), S(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 1), S(GalaxyBudsSettingsCatalog.SirenDetect, 0));
        BudsState siren = State(true, S(GalaxyBudsSettingsCatalog.VoiceDetect, 0), S(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 1), S(GalaxyBudsSettingsCatalog.SirenDetect, 1));
        Assert.NotEqual(BudsView.RowsKey(off), BudsView.RowsKey(on)); // the timeout row appears
        Assert.Equal(BudsView.RowsKey(off), BudsView.RowsKey(siren)); // a plain switch adds nothing
        Assert.Equal(string.Empty, BudsView.RowsKey(null));
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

    [Fact]
    public void Battery_wear_line_shows_each_known_bud_with_one_decimal()
    {
        Assert.Null(BudsView.CyclesLine(null));
        Assert.Null(BudsView.CyclesLine(new BudsState(true)));
        Assert.Equal("Battery wear: left 2.6, right 20.4 full charge cycles",
            BudsView.CyclesLine(new BudsState(true, BatteryCyclesLeft: 2.62, BatteryCyclesRight: 20.39)));
        Assert.Equal("Battery wear: right 0.0 full charge cycles", BudsView.CyclesLine(new BudsState(false, BatteryCyclesRight: 0)));
    }

    [Fact]
    public void Multipoint_line_is_one_quiet_line()
    {
        Assert.Null(BudsView.MultipointLine(null));
        Assert.Null(BudsView.MultipointLine(new BudsState(true)));
        Assert.Equal("Multipoint not supported", BudsView.MultipointLine(new BudsState(true, MultipointSupported: false)));
        Assert.Equal("Multipoint available · nothing streaming", BudsView.MultipointLine(new BudsState(true, MultipointSupported: true, MultipointStreaming: 0)));
        Assert.Equal("Multipoint available · streaming media", BudsView.MultipointLine(new BudsState(true, MultipointSupported: true, MultipointStreaming: 1)));
        Assert.Equal("streaming media and call audio", BudsView.StreamingText(3));
        Assert.Equal("streaming LE audio", BudsView.StreamingText(8));
        Assert.Equal("streaming", BudsView.StreamingText(0x40));
    }

    [Fact]
    public void Noise_cancelling_strength_shows_read_only_in_the_noise_control_group()
    {
        BudsState b = State(true, S(GalaxyBudsSettingsCatalog.SirenDetect, 0), S(GalaxyBudsSettingsCatalog.Seamless, 1),
            S(GalaxyBudsSettingsCatalog.NoiseCancellingStrength, 4));
        var groups = BudsView.Groups(b);
        BudsGroup noise = groups.Single(g => g.Name == GalaxyBudsSettingsCatalog.GroupNoise);
        Assert.Equal(GalaxyBudsSettingsCatalog.GroupNoise, groups[0].Name);
        BudsRow row = noise.Rows.Single(r => r.Def.Key == GalaxyBudsSettingsCatalog.NoiseCancellingStrength);
        Assert.Equal(BudsControlKind.Text, row.Kind);
        Assert.False(row.Enabled);
        Assert.Equal("4", row.Text);
        Assert.Equal(GalaxyBudsSettingsCatalog.All.Count - 1, row.CatalogIndex);
    }
}
