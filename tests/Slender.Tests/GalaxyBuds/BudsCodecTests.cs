using Slender.GalaxyBuds;

namespace Slender.Tests.GalaxyBuds;

public sealed class BudsCodecTests
{
    private static List<BudsFrame> Decode(byte[] bytes)
    {
        var frames = new List<BudsFrame>();
        new BudsFrameReader().Feed(bytes, frames);
        return frames;
    }

    [Fact]
    public void RealStatusFrameDecodes()
    {
        var frames = Decode(BudsTestData.Hex(BudsTestData.EsuFrameHex));
        var f = Assert.Single(frames);
        Assert.Equal(97, f.Id);
        Assert.Equal(BudsTestData.Hex(BudsTestData.EsuPayloadHex), f.Payload);
    }

    [Fact]
    public void CrcMatchesTheRealFrame()
    {
        var real = BudsTestData.Hex(BudsTestData.EsuFrameHex);
        ushort crc = BudsCodec.Crc16(real.AsSpan(3, real.Length - 6));
        Assert.Equal(0x41fc, crc);
    }

    [Fact]
    public void EncodeMatchesARealFrameWhoseHeaderIsLengthOnly()
    {
        Assert.Equal(BudsTestData.Hex(BudsTestData.EsuFrameHex), BudsCodec.Encode(97, BudsTestData.Hex(BudsTestData.EsuPayloadHex)));
        Assert.Equal(BudsTestData.Hex(BudsTestData.NoiseFrameHex), BudsCodec.Encode(119, BudsTestData.Hex("0033010009090202010202")));
    }

    [Fact]
    public void FramesWithFlagBitsInTheHeaderDecode()
    {
        // The real firmware-version frame carries extra flag/sequence bits above the 10-bit length.
        var f = Assert.Single(Decode(BudsTestData.Hex(BudsTestData.VersionFrameHex)));
        Assert.Equal(104, f.Id);
        Assert.Equal(44, f.Payload.Length);
    }

    [Fact]
    public void EncodeRoundTripsAndGettersHaveEmptyPayload()
    {
        var bytes = BudsCodec.Encode(160, []);
        Assert.Equal(0xFD, bytes[0]);
        Assert.Equal(0xDD, bytes[^1]);
        Assert.Equal(3, bytes[1] | (bytes[2] << 8));
        var f = Assert.Single(Decode(bytes));
        Assert.Equal(160, f.Id);
        Assert.Empty(f.Payload);

        var g = Assert.Single(Decode(BudsCodec.Encode(130, [1, 2, 3, 4])));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, g.Payload);
    }

    [Fact]
    public void ReassemblesAcrossEverySplit()
    {
        byte[] stream = [.. BudsTestData.Hex(BudsTestData.EsuFrameHex), .. BudsTestData.Hex(BudsTestData.NoiseFrameHex)];
        for (int cut = 1; cut < stream.Length; cut++)
        {
            var reader = new BudsFrameReader();
            var frames = new List<BudsFrame>();
            reader.Feed(stream.AsSpan(0, cut), frames);
            reader.Feed(stream.AsSpan(cut), frames);
            Assert.Equal([97, 119], frames.Select(f => (int)f.Id));
        }
    }

    [Fact]
    public void ByteByByteDelivery()
    {
        var reader = new BudsFrameReader();
        var frames = new List<BudsFrame>();
        foreach (byte b in BudsTestData.Hex(BudsTestData.EsuFrameHex)) reader.Feed([b], frames);
        Assert.Single(frames);
    }

    [Fact]
    public void BadCrcIsSkippedAndTheNextFrameStillDecodes()
    {
        var bad = BudsTestData.Hex(BudsTestData.NoiseFrameHex);
        bad[^3] ^= 0xFF;
        var reader = new BudsFrameReader();
        var frames = new List<BudsFrame>();
        reader.Feed([.. bad, .. BudsTestData.Hex(BudsTestData.EsuFrameHex)], frames);
        Assert.Equal([97], frames.Select(f => (int)f.Id));
        Assert.True(reader.Dropped >= 1);
    }

    [Fact]
    public void UnknownIdsAndGarbageAreTolerated()
    {
        var reader = new BudsFrameReader();
        var frames = new List<BudsFrame>();
        reader.Feed([0x00, 0x13, .. BudsCodec.Encode(242, [9, 9, 9]), 0x00, .. BudsCodec.Encode(244, [1])], frames);
        Assert.Equal([242, 244], frames.Select(f => (int)f.Id));
    }

    [Fact]
    public void RunawayStartByteDoesNotGrowTheBuffer()
    {
        var reader = new BudsFrameReader();
        var frames = new List<BudsFrame>();
        // A start byte announcing 1000 bytes that never arrive, then the stream keeps flowing.
        reader.Feed([0xFD, 0xE8, 0x03], frames);
        Assert.Equal(1, reader.Dropped); // refused at once: 1000 is longer than any frame, so the reader does not wait for it
        for (int i = 0; i < 100; i++) reader.Feed(new byte[100], frames);
        reader.Feed(BudsCodec.Encode(97, [1]), frames);
        Assert.Contains(frames, f => f.Id == 97);
    }

    [Fact]
    public void TooLongPayloadIsRefused() =>
        Assert.Throws<ArgumentException>(() => BudsCodec.Encode(1, new byte[2000]));
}
