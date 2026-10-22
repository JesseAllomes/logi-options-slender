using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Settings.Logic;
using Slender.Tray;

namespace Slender.Tests.Settings;

public class HeadsetViewTests
{
    [Fact]
    public void Status_rows_say_only_what_the_base_reported()
    {
        Assert.Empty(HeadsetView.StatusRows(null));
        Assert.Empty(HeadsetView.StatusRows(new HeadsetState(null, null, null, null, null, null)));
        Assert.Equal([("Battery", "85%"), ("Charging", "Yes"), ("Docked", "Yes"), ("Signal", "Good"), ("Range", "In range")],
            HeadsetView.StatusRows(new HeadsetState(true, 85, true, true, 7, true)));
        Assert.Equal([("Battery", "40%"), ("Charging", "No"), ("Docked", "No"), ("Range", "Out of range")],
            HeadsetView.StatusRows(new HeadsetState(true, 40, false, false, null, false)));
    }

    [Fact]
    public void An_unpaired_headset_has_one_row()
    {
        Assert.Equal([("Headset", "Not paired with the base")], HeadsetView.StatusRows(new HeadsetState(false, null, null, null, null, null)));
    }

    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "Weak")]
    [InlineData(3, "Fair")]
    [InlineData(5, "Good")]
    [InlineData(7, "Good")]
    public void Signal_words(int signal, string word) => Assert.Equal(word, HeadsetView.SignalWord(signal));

    private static HeadsetSetting Reported(string key, string raw, string text = "x")
    {
        YealinkSettingDef def = YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf(key)];
        return new HeadsetSetting(def.Group, def.Label, text, key, raw);
    }

    private static HeadsetState With(params HeadsetSetting[] settings) => new(true, 1, false, false, 1, true, settings);

    [Fact]
    public void Settings_are_empty_when_none_were_sent()
    {
        Assert.Empty(HeadsetView.Groups(null, YealinkTarget.Headset));
        Assert.Empty(HeadsetView.Groups(With(), YealinkTarget.Base));
    }

    [Fact]
    public void Settings_split_by_target_and_group_in_catalog_order()
    {
        var h = With(Reported("wireless.range", "1"), Reported("keypad_tone.enable", "1"), Reported("auto_sleep.time", "0"), Reported("call.priority", "2"));
        var headset = HeadsetView.Groups(h, YealinkTarget.Headset);
        Assert.Equal(["Sound", "Bluetooth and sleep"], headset.Select(g => g.Group));
        Assert.Equal(["keypad_tone.enable"], headset[0].Items.Select(i => i.Def.Key));
        Assert.Equal(["auto_sleep.time"], headset[1].Items.Select(i => i.Def.Key));

        var baseGroups = HeadsetView.Groups(h, YealinkTarget.Base);
        Assert.Equal(["Calls", "Radio"], baseGroups.Select(g => g.Group));
        Assert.Equal(["wireless.range"], baseGroups[1].Items.Select(i => i.Def.Key));
        Assert.Equal("2", baseGroups[0].Items[0].Raw);
        Assert.Equal(YealinkSettingsCatalog.IndexOf("call.priority"), baseGroups[0].Items[0].CatalogIndex);
    }

    [Fact]
    public void A_setting_with_the_same_label_in_two_groups_matches_by_key()
    {
        var h = With(Reported("phone_ringtone.enable", "0"));
        Assert.Empty(HeadsetView.Groups(h, YealinkTarget.Base));
        var g = Assert.Single(HeadsetView.Groups(h, YealinkTarget.Headset));
        Assert.Equal("Ringing", g.Group);
        Assert.Equal("Ring in headset: desk phone calls", Assert.Single(g.Items).Def.Label);
    }

    [Fact]
    public void A_dependent_setting_hides_until_its_parent_allows_it()
    {
        var off = With(Reported("voice_announcements.enable", "0"), Reported("language.prompt", "0"));
        Assert.DoesNotContain(HeadsetView.Groups(off, YealinkTarget.Headset).SelectMany(g => g.Items), i => i.Def.Key == "language.prompt");

        var on = With(Reported("voice_announcements.enable", "1"), Reported("language.prompt", "0"));
        Assert.Contains(HeadsetView.Groups(on, YealinkTarget.Headset).SelectMany(g => g.Items), i => i.Def.Key == "language.prompt");

        var unknownParent = With(Reported("language.prompt", "0"));
        Assert.Contains(HeadsetView.Groups(unknownParent, YealinkTarget.Headset).SelectMany(g => g.Items), i => i.Def.Key == "language.prompt");

        var teams = With(Reported("platform.mode", "Teams"), Reported("multiple_call_handling.mode", "1"));
        Assert.DoesNotContain(HeadsetView.Groups(teams, YealinkTarget.Headset).SelectMany(g => g.Items), i => i.Def.Key == "multiple_call_handling.mode");
    }

    [Fact]
    public void Plug_in_busylight_and_pc_audio_sit_on_the_right_pages()
    {
        var h = With(Reported("phone_setting.busy_light_custom_type", "0"), Reported("base_audio.play", "0"), Reported("features.music_mode", "1"));
        Assert.Equal(["phone_setting.busy_light_custom_type"], HeadsetView.Groups(h, YealinkTarget.Base).SelectMany(g => g.Items).Select(i => i.Def.Key));
        Assert.Equal(["features.music_mode", "base_audio.play"], HeadsetView.Groups(h, YealinkTarget.Headset).SelectMany(g => g.Items).Select(i => i.Def.Key));
    }

    [Fact]
    public void Settings_from_an_older_tray_match_by_label_and_stay_read_only()
    {
        var h = With(new HeadsetSetting("Sound", "Keypad tone", "On"), new HeadsetSetting("Sound", "Not in the catalog", "?"));
        var g = Assert.Single(HeadsetView.Groups(h, YealinkTarget.Headset));
        HeadsetSettingItem item = Assert.Single(g.Items);
        Assert.Equal("keypad_tone.enable", item.Def.Key);
        Assert.Null(item.Raw);
        Assert.False(item.Adjustable);
        Assert.Equal("On", item.Text);
    }

    [Fact]
    public void Only_settable_settings_with_a_known_raw_value_are_adjustable()
    {
        var h = With(Reported("keypad_tone.enable", "1"), Reported("features.bluetooth_adapter_name", "Name"), new HeadsetSetting("Sound", "Sidetone level", "3", "sidetone.level", null));
        var items = HeadsetView.Groups(h, YealinkTarget.Headset).SelectMany(g => g.Items).ToDictionary(i => i.Def.Key);
        Assert.True(items["keypad_tone.enable"].Adjustable);
        Assert.False(items["features.bluetooth_adapter_name"].Adjustable);
        Assert.False(items["sidetone.level"].Adjustable);
    }

    [Fact]
    public void On_off_choices_are_detected()
    {
        Assert.True(HeadsetView.IsOnOff(YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("keypad_tone.enable")]));
        Assert.False(HeadsetView.IsOnOff(YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("platform.mode")]));
        Assert.False(HeadsetView.IsOnOff(YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("sidetone.level")]));
        Assert.False(HeadsetView.IsOnOff(YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("wireless.range")]));
    }

    [Fact]
    public void Tray_detail_line_shows_battery_and_docked()
    {
        Assert.Null(TrayText.Headset(null));
        Assert.Null(TrayText.Headset(new HeadsetState(null, null, null, null, null, null)));
        Assert.Null(TrayText.Headset(new HeadsetState(false, null, null, null, null, null)));
        Assert.Equal("Battery 85% charging · Docked", TrayText.Headset(new HeadsetState(true, 85, true, true, 7, true)));
        Assert.Equal("Battery 40% · Off the base · Out of range", TrayText.Headset(new HeadsetState(true, 40, false, false, 0, false)));
    }
}
