using Slender.Engine;

namespace Slender.Tests.Ringer;

public class RingProtocolTests
{
    [Theory]
    [InlineData(true, RingerSignalSource.Headset, "!ring start hid")]
    [InlineData(false, RingerSignalSource.Headset, "!ring stop hid")]
    [InlineData(true, RingerSignalSource.Notification, "!ring start notification")]
    [InlineData(false, RingerSignalSource.Notification, "!ring stop notification")]
    public void Ring_lines_round_trip(bool start, RingerSignalSource source, string line)
    {
        var signal = new RingSignal(start, source);
        Assert.Equal(line, BuzzProtocol.RingLine(signal));
        Assert.Equal(new BuzzRequest(null, null, signal), BuzzProtocol.Parse(line));
        Assert.Equal(signal, BuzzProtocol.Parse(" " + line + "\r")!.Ring);
    }

    [Theory]
    [InlineData("!ring")]
    [InlineData("!ring start")]
    [InlineData("!ring pause hid")]
    [InlineData("!ring start phone")]
    [InlineData("!ring start hid now")]
    [InlineData("!ringstart hid")]
    public void A_malformed_ring_line_is_ignored(string line) => Assert.Null(BuzzProtocol.Parse(line));

    [Fact]
    public void Older_lines_parse_as_before()
    {
        Assert.Equal(new BuzzRequest("claude", null), BuzzProtocol.Parse("claude"));
        Assert.Equal(new BuzzRequest(null, null), BuzzProtocol.Parse(""));
        Assert.Null(BuzzProtocol.Parse("!ring-start hid"));
        Assert.Null(BuzzProtocol.Parse("!unknown"));
        Assert.Null(BuzzProtocol.Parse("!test 5 1 150 -")!.Ring);
    }
}
