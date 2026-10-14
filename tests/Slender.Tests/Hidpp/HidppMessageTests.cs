using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class HidppMessageTests
{
    [Fact]
    public void Encode_builds_20_byte_long_report()
    {
        var b = HidppMessage.Encode(0xFF, 0x05, 0x3, 0x0B, new byte[] { 1, 2, 3 });
        Assert.Equal(20, b.Length);
        Assert.Equal(new byte[] { 0x11, 0xFF, 0x05, 0x3B, 1, 2, 3 }, b[..7]);
        Assert.All(b[7..], x => Assert.Equal(0, x));
    }

    [Fact]
    public void Encode_rejects_oversize_params()
        => Assert.Throws<ArgumentException>(() => HidppMessage.Encode(1, 0, 0, 1, new byte[17]));

    [Fact]
    public void Parse_with_report_id()
    {
        var raw = HidppMessage.Encode(0x01, 0x07, 2, 0x0B, new byte[] { 0xAA, 0xBB });
        Assert.True(HidppMessage.TryParse(raw, out var m));
        Assert.Equal(0x11, m.ReportId);
        Assert.Equal(0x01, m.DeviceIndex);
        Assert.Equal(0x07, m.FeatureIndex);
        Assert.Equal(2, m.Function);
        Assert.Equal(0x0B, m.SwId);
        Assert.Equal(0xAA, m.Params[0]);
        Assert.Equal(16, m.Params.Length);
    }

    [Fact]
    public void Parse_without_report_id()
    {
        var raw = HidppMessage.Encode(0xFF, 0x07, 1, 0x00, new byte[] { 0x12 });
        Assert.True(HidppMessage.TryParse(raw.AsSpan(1), out var m));
        Assert.Equal(0xFF, m.DeviceIndex);
        Assert.Equal(0x07, m.FeatureIndex);
        Assert.Equal(1, m.Function);
        Assert.Equal(0x12, m.Params[0]);
    }

    [Fact]
    public void Parse_rejects_too_short()
    {
        Assert.False(HidppMessage.TryParse(new byte[] { 0x11, 0x01 }, out _));
        Assert.False(HidppMessage.TryParse([], out _));
    }

    [Fact]
    public void Hidpp2_error_is_detected()
    {
        // [0]=11 [1]=dev [2]=FF [3]=failed feature idx [4]=failed func|sw [5]=code
        var raw = HidppMessage.Encode(0xFF, 0xFF, 0, 0, new byte[] { 0x31, 0x08 });
        raw[3] = 0x05;
        Assert.True(HidppMessage.TryParse(raw, out var m));
        Assert.True(m.IsError);
        Assert.Equal(0x05, m.ErrorFeatureIndex);
        Assert.Equal(3, m.ErrorFunction);
        Assert.Equal(1, m.ErrorSwId);
        Assert.Equal(0x08, m.ErrorCode);
    }

    [Fact]
    public void Hidpp1_error_8F_is_detected()
    {
        var raw = HidppMessage.Encode(0xFF, 0x8F, 0, 0, new byte[] { 0x00, 0x09 });
        Assert.True(HidppMessage.TryParse(raw, out var m));
        Assert.True(m.IsError);
        Assert.Equal(0x09, m.ErrorCode);
    }

    [Fact]
    public void Normal_message_is_not_an_error()
    {
        Assert.True(HidppMessage.TryParse(HidppMessage.Encode(1, 0x04, 0, 0, []), out var m));
        Assert.False(m.IsError);
    }
}
