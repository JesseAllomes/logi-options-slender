using Slender.GalaxyBuds;

namespace Slender.Tests.GalaxyBuds;

/// <summary>Frames from a real Buds4 Pro capture (status, noise-control, version); no address or serial bytes are in these.</summary>
internal static class BudsTestData
{
    public const string EsuPayloadHex =
        "040d64640101335f0003ff220000670167010000049900040410000000001102000000010000010000000100000000000101000000ff0101000000000001";

    public const string EsuFrameHex = "fd410061" + EsuPayloadHex + "fc41dd";

    public const string NoiseFrameHex = "fd0e0077003301000909020201020259c0dd";

    public const string VersionFrameHex = "fd2fc86802025236343058585530415a443200000000000000005236343058585530415a443200000000000000000000c874dd";

    // From the 2026-11-19 follow-up captures (research repo captures/gm-*.redacted.jsonl): read requests and their answers, the
    // siren-detect writes and acks, and status frames after the proven writes. None of these carry an address or serial.

    /// <summary>118 request as sent (complete frame) and the buds' 8-byte answer (rev 1, supported, nothing streaming).</summary>
    public const string MultipointRequestFrameHex = "fd030076511edd";
    public const string MultipointPayloadHex = "0101000000010000";
    public const ushort MultipointHeader = 0xd00b;

    /// <summary>206 request as sent and the buds' 16-byte answer: 26200 and 203900 ten-thousandths (2.62 / 20.39 cycles).</summary>
    public const string CyclesRequestFrameHex = "fd0300ce8238dd";
    public const string CyclesPayloadHex = "00000000000066580000000000031c7c";
    public const ushort CyclesHeader = 0x1013;

    /// <summary>222 siren detect on / off as sent, and the acks (id 66) the buds gave.</summary>
    public const string SirenOnFrameHex = "fd0400de010926dd";
    public const string SirenOffFrameHex = "fd0400de002836dd";
    public const string SirenOnAckPayloadHex = "de01";
    public const string SirenOffAckPayloadHex = "de00";

    /// <summary>Status after reconnect with siren detect on (ESU[52] = 1).</summary>
    public const string EsuSirenOnPayloadHex =
        "040d6464010133640003ff220000670167010000049900040410000000001102000000010000010000000100000000000101000001ff0101000000000001";

    /// <summary>Status after reconnect with noise-cancelling strength 3 (ESU[24] = 3), and the baseline with 4.</summary>
    public const string EsuAnc3PayloadHex =
        "040d6464010133640003ff220000670167010000049900040310000000001102000000010000010000000100000000000101000000ff0101000000000001";
    public const string EsuBaselinePayloadHex =
        "040d6464010133640003ff220000670167010000049900040410000000001102000000010000010000000100000000000101000000ff0101000000000001";

    /// <summary>The ids Slender sends empty to read (model, multipoint, battery cycles); the real buds answer with data, not an ack.</summary>
    public static bool IsRead(byte id) => BudsAllowlist.IsReadRequest(id);

    /// <summary>A frame with the header the buds really sent (flags and sequence bits included).</summary>
    public static byte[] FrameWithHeader(ushort header, byte id, byte[] payload)
    {
        var f = BudsCodec.Encode(id, payload);
        f[1] = (byte)(header & 0xFF);
        f[2] = (byte)(header >> 8);
        return f;
    }

    public static byte[] Hex(string s) => Convert.FromHexString(s);

    public static BudsFrame Esu() => new(BudsIds.ExtendedStatusUpdated, Hex(EsuPayloadHex));

    /// <summary>A frame as the buds would send it (header = length, which the real ones also carry in the low bits).</summary>
    public static byte[] FrameBytes(byte id, params byte[] payload) => BudsCodec.Encode(id, payload);

    public static BudsModel ModelFromEsu()
    {
        var m = new BudsModel();
        BudsStatusParser.Apply(m, Esu());
        return m;
    }
}
