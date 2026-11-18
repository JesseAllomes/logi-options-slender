using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class ReleaseListenerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ticket_is_a_live_web_pubsub_socket()
    {
        Uri? uri = ReleaseListener.ParseTicket("""{"url":"wss://slender-notify.webpubsub.azure.com/client/hubs/slender?access_token=x","expires":"2027-12-23T05:25:39Z"}""", Now);
        Assert.Equal("slender-notify.webpubsub.azure.com", uri?.Host);
    }

    [Theory]
    [InlineData("""{"url":"wss://x.webpubsub.azure.com/client/hubs/h?access_token=x","expires":"2026-11-01T00:00:00Z"}""")] // expired
    [InlineData("""{"url":"https://x.webpubsub.azure.com/client/hubs/h"}""")] // not a socket
    [InlineData("""{"url":"wss://evil.example.com/hubs/h"}""")] // not Web PubSub
    [InlineData("""{"url":"wss://x.webpubsub.azure.com.evil.com/h"}""")]
    [InlineData("""["wss://x.webpubsub.azure.com/h"]""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Bad_tickets_are_refused(string json) => Assert.Null(ReleaseListener.ParseTicket(json, Now));

    [Fact]
    public void Release_notice_gives_its_version()
    {
        Assert.Equal("1.1.60", ReleaseListener.ParseRelease("""{"type":"release","app":"slender","version":"1.1.60"}""", "slender"));
        Assert.Equal("1.1.60", ReleaseListener.ParseRelease("""{"type":"release","version":"1.1.60"}""", "slender"));
    }

    [Theory]
    [InlineData("""{"type":"release","app":"other-app","version":"1.1.60"}""")]
    [InlineData("""{"type":"hello","version":"1.1.60"}""")]
    [InlineData("""{"type":"release","version":"../../x"}""")]
    [InlineData("""{"type":"release","version":"v1"}""")]
    [InlineData("""{"type":"release","version":19}""")]
    [InlineData("""{"type":"release"}""")]
    [InlineData("1.1.60")]
    [InlineData("")]
    public void Other_messages_are_ignored(string text) => Assert.Null(ReleaseListener.ParseRelease(text, "slender"));

    [Fact]
    public void Reconnect_waits_double_from_five_seconds_to_five_minutes()
    {
        Assert.InRange(ReleaseListener.Backoff(0).TotalSeconds, 5, 6);
        Assert.InRange(ReleaseListener.Backoff(2).TotalSeconds, 20, 24);
        Assert.InRange(ReleaseListener.Backoff(50).TotalSeconds, 300, 360);
        Assert.InRange(ReleaseListener.Backoff(-3).TotalSeconds, 5, 6);
    }
}
