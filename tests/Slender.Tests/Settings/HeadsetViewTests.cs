using Slender.Engine;
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

    [Fact]
    public void Settings_group_in_the_order_sent_and_empty_when_none()
    {
        Assert.Empty(HeadsetView.Groups(null));
        Assert.Empty(HeadsetView.Groups(new HeadsetState(true, 1, false, false, 1, true, [])));
        var h = new HeadsetState(true, 1, false, false, 1, true,
        [
            new HeadsetSetting("General", "A", "1"), new HeadsetSetting("Sound", "B", "2"), new HeadsetSetting("General", "C", "3"),
        ]);
        var g = HeadsetView.Groups(h);
        Assert.Equal(["General", "Sound"], g.Select(x => x.Group));
        Assert.Equal(["A", "C"], g[0].Items.Select(i => i.Label));
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
