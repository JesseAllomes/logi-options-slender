using System.Globalization;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class RingerTextTests
{
    [Fact]
    public void Status_says_not_set_up_waiting_or_the_last_signal_in_local_time()
    {
        Assert.Equal("Not set up", RingerText.Status(false, null, null));
        Assert.Equal("Not set up", RingerText.Status(false, DateTimeOffset.UtcNow, "headset")); // removed since: no stale signal
        Assert.Equal("Set up — waiting for the first call", RingerText.Status(true, null, null));

        var at = new DateTimeOffset(2026, 10, 22, 3, 5, 0, TimeSpan.Zero);
        string local = at.ToLocalTime().ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture);
        Assert.Equal($"Last ring signal: {local} (headset)", RingerText.Status(true, at, "headset"));
        Assert.Equal($"Last ring signal: {local} (notification)", RingerText.Status(true, at, "notification"));
        Assert.Equal($"Last ring signal: {local} (headset base)", RingerText.Status(true, at, "base"));
        Assert.Equal($"Last ring signal: {local} (headset base)", RingerText.Status(false, at, "base")); // the base needs no extension
    }

    [Theory]
    [InlineData("https://pbx.example.com", false)]
    [InlineData(" https://pbx.example.com:8443/ ", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("http://pbx.example.com", true)]
    [InlineData("https://pbx.example.com/web", true)]
    public void The_address_box_explains_a_bad_address_in_plain_words(string? text, bool problem)
    {
        string? message = RingerText.AddressProblem(text);
        Assert.Equal(problem, message is not null);
        if (message is not null) Assert.DoesNotContain("Normalize", message);
    }

    [Fact]
    public void The_steps_name_the_three_things_to_do_in_edge()
    {
        Assert.Contains("edge://extensions", RingerText.Steps);
        Assert.Contains("Developer mode", RingerText.Steps);
        Assert.Contains("Load unpacked", RingerText.Steps);
        Assert.StartsWith("1.", RingerText.Steps);
    }

    [Fact]
    public void The_ring_button_pickers_list_off_then_the_mouse_buttons_and_map_both_ways()
    {
        Assert.Equal(["Off", "Middle click", "Forward", "Back"], RingerText.RingButtonChoices());
        Assert.Equal<ushort>(0, RingerText.RingButtonAt(0));
        Assert.Equal<ushort>(0, RingerText.RingButtonAt(-1));
        Assert.Equal<ushort>(0, RingerText.RingButtonAt(9));
        for (int i = 0; i < 4; i++) Assert.Equal(i, RingerText.RingButtonIndex(RingerText.RingButtonAt(i)));
        Assert.Equal(0, RingerText.RingButtonIndex(0xC3)); // a stored id that is not a choice shows as Off
    }
}
