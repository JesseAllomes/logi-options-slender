using System.Buffers.Binary;
using System.Text;
using Slender.Engine;
using Slender.Engine.Ringer;
using Slender.RingerHost;

namespace Slender.Tests.Ringer;

public class NativeMessageTests
{
    private static MemoryStream Framed(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
        stream.Write(header);
        stream.Write(body);
        stream.Position = 0;
        return stream;
    }

    private static RingSignal? Read(string json) => NativeMessage.ReadFrame(Framed(json)) is { } body ? NativeMessage.Parse(body) : null;

    [Theory]
    [InlineData("""{"event":"ring-start","source":"hid"}""", true, RingerSignalSource.Headset)]
    [InlineData("""{"event":"ring-stop","source":"hid"}""", false, RingerSignalSource.Headset)]
    [InlineData("""{"source":"notification","event":"ring-start"}""", true, RingerSignalSource.Notification)]
    [InlineData("""{ "event": "ring-stop", "source": "notification" }""", false, RingerSignalSource.Notification)]
    public void A_good_message_becomes_a_signal(string json, bool start, RingerSignalSource source) =>
        Assert.Equal(new RingSignal(start, source), Read(json));

    [Theory]
    [InlineData("""{"event":"ring-start"}""")]
    [InlineData("""{"source":"hid"}""")]
    [InlineData("""{"event":"ring","source":"hid"}""")]
    [InlineData("""{"event":"ring-start","source":"phone"}""")]
    [InlineData("""{"event":"ring-start","source":"hid","title":"Caller"}""")]
    [InlineData("""{"event":"ring-start","event":"ring-stop","source":"hid"}""")]
    [InlineData("""{"event":1,"source":"hid"}""")]
    [InlineData("""["ring-start","hid"]""")]
    [InlineData("""{"event":"ring-start","source":"hid"} x""")]
    [InlineData("not json")]
    [InlineData("{")]
    public void Anything_else_is_refused(string json) => Assert.Null(Read(json));

    [Fact]
    public void An_oversize_or_empty_frame_is_refused_without_reading_the_body()
    {
        int over = NativeMessage.MaxBytes + 1;
        var big = new MemoryStream();
        big.Write([(byte)(over & 0xFF), (byte)(over >> 8), 0, 0]);
        big.Write(new byte[over]);
        big.Position = 0;
        Assert.Null(NativeMessage.ReadFrame(big));
        Assert.Equal(4, big.Position); // only the length was consumed

        Assert.Null(NativeMessage.ReadFrame(new MemoryStream([0, 0, 0, 0])));
        Assert.Null(NativeMessage.ReadFrame(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF])));
    }

    [Fact]
    public void A_frame_at_the_limit_is_read_and_a_short_stream_is_refused()
    {
        string json = """{"event":"ring-start","source":"hid"}""";
        json += new string(' ', NativeMessage.MaxBytes - json.Length);
        Assert.Equal(new RingSignal(true, RingerSignalSource.Headset), Read(json));
        Assert.Null(NativeMessage.ReadFrame(new MemoryStream([10, 0, 0, 0, 1, 2]))); // announces 10 bytes, holds 2
        Assert.Null(NativeMessage.ReadFrame(new MemoryStream([1, 0])));              // not even a whole length
        Assert.Null(NativeMessage.ReadFrame(new MemoryStream()));
    }

    [Theory]
    [InlineData(true, """{"ok":true}""")]
    [InlineData(false, """{"ok":false}""")]
    public void The_reply_is_framed_the_same_way(bool ok, string expected)
    {
        var output = new MemoryStream();
        NativeMessage.WriteReply(output, ok);
        byte[] bytes = output.ToArray();
        Assert.Equal((uint)expected.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4));
    }

    [Theory]
    [InlineData("""{"event":"ring-start","source":"hid"}""")]
    [InlineData("""{"event":"ring-stop","source":"notification"}""")]
    public void What_the_host_forwards_is_a_valid_buzz_pipe_ring_line(string json)
    {
        RingSignal signal = Read(json)!.Value;
        string line = BuzzProtocol.RingLine(signal);
        Assert.True(line.Length <= BuzzProtocol.MaxLineChars);
        Assert.Equal(signal, BuzzProtocol.Parse(line)!.Ring);
    }

    private static byte[] Body(string json) => NativeMessage.ReadFrame(Framed(json))!;

    [Theory]
    [InlineData("""{"event":"poll"}""", true)]
    [InlineData("""{ "event" : "poll" }""", true)]
    [InlineData("""{"event":"poll","source":"hid"}""", false)]
    [InlineData("""{"event":"ring-start"}""", false)]
    [InlineData("""{"event":true}""", false)]
    [InlineData("""{"event":"poll","event":"poll"}""", false)]
    [InlineData("not json", false)]
    public void Only_a_bare_poll_is_a_poll(string json, bool expected) => Assert.Equal(expected, NativeMessage.IsPoll(Body(json)));

    [Theory]
    [InlineData("""{"event":"ring-result","command":"answer","found":true}""", RingCommand.Answer, true)]
    [InlineData("""{"event":"ring-result","command":"reject","found":false}""", RingCommand.Reject, false)]
    [InlineData("""{"found":true,"command":"answer","event":"ring-result"}""", RingCommand.Answer, true)]
    public void A_good_result_report_is_read(string json, RingCommand command, bool found) =>
        Assert.Equal(new RingResult(command, found), NativeMessage.ParseResult(Body(json)));

    [Theory]
    [InlineData("""{"event":"ring-result","command":"answer"}""")]
    [InlineData("""{"event":"ring-result","command":"hangup","found":true}""")]
    [InlineData("""{"event":"ring-result","command":"answer","found":"yes"}""")]
    [InlineData("""{"event":"ring-result","command":"answer","found":true,"title":"Caller"}""")]
    [InlineData("""{"event":"ring-start","command":"answer","found":true}""")]
    [InlineData("""{"event":"ring-result","command":"answer","found":1}""")]
    public void Any_other_result_report_is_refused(string json)
    {
        Assert.Null(NativeMessage.ParseResult(Body(json)));
        Assert.Null(NativeMessage.Parse(Body(json))); // and a result is never taken for a ring signal
    }

    [Fact]
    public void A_poll_reply_carries_the_state()
    {
        var output = new MemoryStream();
        NativeMessage.WriteReply(output, true, "answer");
        string expected = """{"ok":true,"state":"answer"}""";
        byte[] bytes = output.ToArray();
        Assert.Equal((uint)expected.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4));
    }
}
