using Slender.Engine;
using Slender.Tray.Ring;

namespace Slender.Tests.Tray;

public class RingIconFileTests
{
    [Theory]
    [InlineData(@"file:\\server\share\app.exe", null, null)]
    [InlineData(null, @"\\server\tools\x.exe", null)]
    [InlineData(null, @"C:\Tools\x.exe", @"C:\Tools\x.exe")]
    [InlineData(@"file:C:\img\a.png", null, @"C:\img\a.png")]
    [InlineData(null, @"relative\x.exe", null)]
    [InlineData(@"file://host/share/a.exe", null, null)]
    [InlineData(null, "//10.0.0.9/s/a.exe", null)]
    [InlineData(null, @"\/host/share/a.exe", null)]
    public void IconFile_never_returns_network_or_relative_paths(string? icon, string? launch, string? expected)
    {
        RingSlot slot = launch is null
            ? new RingSlot(new ActionSpec.None()) { Icon = icon }
            : new RingSlot(new ActionSpec.Launch(launch));
        Assert.Equal(expected, RingSlotText.IconFile(slot));
    }

    [Fact]
    public void Fit_never_splits_a_surrogate_pair()
    {
        var fitted = RingSlotText.Fit("123456789\U0001F600abc", 11);
        for (var i = 0; i < fitted.Length; i++)
            if (char.IsHighSurrogate(fitted[i]))
                Assert.True(i + 1 < fitted.Length && char.IsLowSurrogate(fitted[i + 1]));
        Assert.EndsWith("…", fitted);
        Assert.Equal("short", RingSlotText.Fit("short", 11));
    }

    [Fact]
    public void Missing_icon_file_is_probed_once_and_cached_as_none()
    {
        // The missing-file branch returns before the canvas is used, so none is needed (and nothing to dispose).
        var cache = new RingIconCache(null!);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
        Assert.Equal(0, cache.Get(path, 32));
        Assert.Equal(0, cache.Get(path, 32));
    }

    [Fact]
    public void Png_icon_loads_as_square_premultiplied_pixels()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, BuildRedPng());
            var pixels = FileIcons.Load(path, 16);
            Assert.NotNull(pixels);
            Assert.Equal(16 * 16 * 4, pixels!.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>A valid 2x2 opaque red 8-bit RGB PNG (stored zlib block, CRC32 and Adler-32 computed here).</summary>
    private static byte[] BuildRedPng()
    {
        var raw = new List<byte>();
        for (var row = 0; row < 2; row++)
        {
            raw.Add(0); // filter none
            for (var px = 0; px < 2; px++) raw.AddRange(new byte[] { 255, 0, 0 });
        }
        uint a = 1, b = 0;
        foreach (var v in raw) { a = (a + v) % 65521; b = (b + a) % 65521; }
        var zlib = new List<byte> { 0x78, 0x01, 0x01, (byte)raw.Count, 0, (byte)~raw.Count, 0xFF };
        zlib.AddRange(raw);
        uint adler = (b << 16) | a;
        zlib.AddRange(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler });

        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        AddChunk(png, "IHDR", new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 2, 0, 0, 0 });
        AddChunk(png, "IDAT", zlib.ToArray());
        AddChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void AddChunk(List<byte> png, string type, byte[] data)
    {
        png.AddRange(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
        var body = new List<byte>(System.Text.Encoding.ASCII.GetBytes(type));
        body.AddRange(data);
        uint crc = 0xFFFFFFFF;
        foreach (var v in body)
        {
            crc ^= v;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        crc ^= 0xFFFFFFFF;
        png.AddRange(body);
        png.AddRange(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
    }
}
