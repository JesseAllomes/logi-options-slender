using Slender.Engine;
using Slender.Engine.Ring;
using Slender.Tray.Ring;

namespace Slender.Tests.Tray;

public class RingSlotTextTests
{
    private static RingSlot S(ActionSpec a, string? name = null, string? icon = null) => new(a) { Name = name, Icon = icon };

    [Theory]
    [InlineData("Ctrl+X", "Cut", "\uE8C6")]
    [InlineData("Ctrl+C", "Copy", "\uE8C8")]
    [InlineData("Ctrl+V", "Paste", "\uE77F")]
    [InlineData("Ctrl+Z", "Undo", "\uE7A7")]
    [InlineData("Ctrl+Y", "Redo", "\uE7A6")]
    [InlineData("VolumeUp|VolumeDown", "Volume", "\uE767")]
    [InlineData("Ctrl+Alt+K", "Ctrl+Alt+K", RingSlotText.KeyboardGlyph)]
    public void KeysNameAndGlyph(string chord, string name, string glyph)
    {
        var slot = S(new ActionSpec.Keys(chord));
        Assert.Equal(name, RingSlotText.Name(slot));
        Assert.Equal(glyph, RingSlotText.Glyph(slot));
    }

    [Fact]
    public void ExplicitNameAndGlyphIconWin()
    {
        var slot = S(new ActionSpec.Keys("Ctrl+C"), "Mine", "glyph:E8D2");
        Assert.Equal("Mine", RingSlotText.Name(slot));
        Assert.Equal("\uE8D2", RingSlotText.Glyph(slot));
        Assert.Equal("\uE8C8", RingSlotText.Glyph(S(new ActionSpec.Keys("Ctrl+C"), null, "glyph:zz"))); // bad code falls back
    }

    [Fact]
    public void ActionTypeFallbacks()
    {
        Assert.Equal(RingSlotText.GlobeGlyph, RingSlotText.Glyph(S(new ActionSpec.Launch("https://example.com"))));
        Assert.Equal("example.com", RingSlotText.Name(S(new ActionSpec.Launch("https://www.example.com/x"))));
        Assert.Equal(RingSlotText.TextGlyph, RingSlotText.Glyph(S(new ActionSpec.Text("hello"))));
        Assert.Equal(RingSlotText.BrightnessGlyph, RingSlotText.Glyph(S(new ActionSpec.Brightness(10))));
        Assert.Equal(RingSlotText.FolderGlyph, RingSlotText.Glyph(RingDefaults.EmptyFolder("X")));
        Assert.Equal("Folder", RingSlotText.Name(new RingSlot(new ActionSpec.None()) { Folder = [] }));
        Assert.Equal("Mouse fwd", RingSlotText.Shortcut(S(new ActionSpec.MouseButton("Forward"))));
        Assert.Equal("Ctrl X", RingSlotText.Shortcut(S(new ActionSpec.Keys("Ctrl+X"))));
    }

    [Fact]
    public void IconFileFromExplicitIconOrLaunchExe()
    {
        Assert.Equal(@"C:\a\b.exe", RingSlotText.IconFile(S(new ActionSpec.Launch(@"C:\x\y.exe"), null, @"file:C:\a\b.exe")));
        Assert.Equal(@"C:\x\y.exe", RingSlotText.IconFile(S(new ActionSpec.Launch(@"C:\x\y.exe"))));
        Assert.Equal(@"C:\x\y.lnk", RingSlotText.IconFile(S(new ActionSpec.Launch(@"C:\x\y.lnk"))));
        Assert.Null(RingSlotText.IconFile(S(new ActionSpec.Launch("https://a.b/c.exe"))));
        Assert.Null(RingSlotText.IconFile(S(new ActionSpec.Launch(@"C:\x\y.exe"), null, "glyph:E8D2")));
        Assert.Null(RingSlotText.IconFile(S(new ActionSpec.Keys("F1"))));
    }

    [Fact]
    public void FitAddsEllipsis()
    {
        Assert.Equal("abc", RingSlotText.Fit("abc", 5));
        Assert.Equal("abcd\u2026", RingSlotText.Fit("abcdefgh", 5));
    }
}

/// <summary>
/// Draws every style into an offscreen DIB (no window is created). Set SLENDER_RING_PNG_DIR to also write PNGs for a look.
/// </summary>
public class RingRenderTests
{
    private static RingCell[] Cells(IReadOnlyList<RingSlot> slots) => RingRenderer.Cells(slots, null, 24);

    [Theory]
    [InlineData(RingStyle.Bezel, RingSize.Medium, RingLabels.Always, 1.0, null)]
    [InlineData(RingStyle.Bezel, RingSize.Large, RingLabels.SelectedOnly, 1.5, 1)]
    [InlineData(RingStyle.Compass, RingSize.Medium, RingLabels.Always, 1.0, 1)]
    [InlineData(RingStyle.Compass, RingSize.Small, RingLabels.SelectedOnly, 1.25, 7)]
    [InlineData(RingStyle.Keypad, RingSize.Medium, RingLabels.Always, 1.0, 2)]
    [InlineData(RingStyle.Keypad, RingSize.Medium, RingLabels.Always, 1.0, null)]
    public void StylesDrawSomething(RingStyle style, RingSize size, RingLabels labels, double dpi, int? selected)
    {
        var m = RingLayout.Metrics(style, size);
        var px = (int)Math.Ceiling(m.Extent * dpi) * 2;
        var fonts = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        using var canvas = new RingCanvas(px, px, dpi, fonts);
        var slots = RingDefaults.Slots().ToArray();
        slots[7] = slots[7] with { Name = null };
        RingRenderer.Draw(canvas, new RingFrame(style, m, RingColor.Rgb(0xD9925F), labels, Cells(slots), selected, selected is null ? "Work" : null));

        var pixels = canvas.Pixels;
        var opaque = 0;
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) opaque++;
        Assert.True(opaque > px * px / 20, $"only {opaque} drawn pixels");
        Assert.Equal(0, pixels[3]); // the corner stays transparent

        if (Environment.GetEnvironmentVariable("SLENDER_RING_PNG_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            PngWriter.Write(Path.Combine(dir, $"{style}-{size}-{labels}-{dpi}-{selected}.png"), px, px, pixels);
        }
    }

    [Fact]
    public void BundledFontsLoadWhenPresent()
    {
        var fonts = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        if (!File.Exists(Path.Combine(fonts, "Sora-SemiBold.ttf"))) return;
        using var canvas = new RingCanvas(64, 64, 1, fonts);
        Assert.True(canvas.UsingBundledFonts);
    }

    [Fact]
    public void AccentParses()
    {
        Assert.Equal(RingColor.Rgb(0x112233), RingColor.TryParse("#112233"));
        Assert.Null(RingColor.TryParse("112233"));
        Assert.Null(RingColor.TryParse("#12"));
    }
}

internal static class PngWriter
{
    /// <summary>Premultiplied BGRA to a straight RGBA PNG (test viewing only).</summary>
    public static void Write(string path, int w, int h, ReadOnlySpan<byte> bgra)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                int a = bgra[o + 3];
                byte Un(int v) => a == 0 ? (byte)0 : (byte)Math.Min(255, v * 255 / a);
                raw.WriteByte(Un(bgra[o + 2]));
                raw.WriteByte(Un(bgra[o + 1]));
                raw.WriteByte(Un(bgra[o]));
                raw.WriteByte((byte)a);
            }
        }
        using var z = new MemoryStream();
        using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Fastest, true)) zs.Write(raw.ToArray());
        using var f = File.Create(path);
        f.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(f, "IHDR", ihdr);
        Chunk(f, "IDAT", z.ToArray());
        Chunk(f, "IEND", []);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in t.Concat(data))
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        crc ^= 0xFFFFFFFF;
        Span<byte> c = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }
}
