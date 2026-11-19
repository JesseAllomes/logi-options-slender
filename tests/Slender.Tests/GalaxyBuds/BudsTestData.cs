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
