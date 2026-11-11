using System.Runtime.InteropServices;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed unsafe class TrayBadgeRendererTests
{
    [DllImport("user32.dll")] private static extern int DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint LoadImageW(nint inst, char* name, uint type, int cx, int cy, uint flags);

    private static (byte B, byte G, byte R, byte A) Px(byte[] frame, int size, int x, int y)
    {
        int o = (y * size + x) * 4;
        return (frame[o], frame[o + 1], frame[o + 2], frame[o + 3]);
    }

    private static byte[] Tile(int size) => TileWith(size, out _);

    /// <summary>A straight-orange tile (transparent first row and column, half-alpha second column, white S block with a half-white fringe).</summary>
    private static byte[] TileWith(int size, out (int X0, int Y0, int X1, int Y1) s)
    {
        var b = new byte[size * size * 4];
        void Set(int x, int y, int r, int g, int bl, int a)
        {
            int o = (y * size + x) * 4;
            b[o] = (byte)(bl * a / 255); b[o + 1] = (byte)(g * a / 255); b[o + 2] = (byte)(r * a / 255); b[o + 3] = (byte)a;
        }

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) Set(x, y, 0xF5, 0x82, 0x20, x == 0 || y == 0 ? 0 : x == 1 ? 128 : 255);
        s = (size * 3 / 8, size * 3 / 8, size * 3 / 8 + size / 4, size * 3 / 8 + size * 3 / 8);
        for (int y = s.Y0; y <= s.Y1; y++)
            for (int x = s.X0; x <= s.X1; x++) Set(x, y, 255, 255, 255, 255);
        for (int y = s.Y0; y <= s.Y1; y++) Set(s.X0 - 1, y, 250, 194, 144, 255); // half white, half orange
        return b;
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(40)]
    public void Frames_have_the_right_count_and_size(int size)
    {
        var basePx = Tile(size);
        var low = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, basePx);
        Assert.Equal(BadgeTiming.FrameCount(BadgeKind.Low, true), low.Length);
        Assert.Equal(20, low.Length);
        Assert.All(low, f => Assert.Equal(size * size * 4, f.Length));
        Assert.Equal(BadgeTiming.FrameCount(BadgeKind.Charging, true), TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, basePx).Length);
        Assert.Single(TrayBadgeRenderer.RenderFrames(BadgeKind.Low, false, size, basePx));
        Assert.Empty(TrayBadgeRenderer.RenderFrames(BadgeKind.None, true, size, basePx));
        Assert.Empty(TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, new byte[size * size * 4])); // nothing orange to move
    }

    [Fact]
    public void Pulse_starts_at_the_plain_icon_peaks_midway_and_mirrors_so_frames_are_shared()
    {
        const int size = 16;
        var basePx = Tile(size);
        var f = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, basePx);
        Assert.Equal(basePx, f[0]);
        Assert.NotEqual(f[0], f[10]);
        for (int i = 1; i < 10; i++) Assert.Same(f[i], f[20 - i]);       // a sine is symmetric
        Assert.True(f.Distinct().Count() <= 11);
        Assert.Equal(1.0, TrayBadgeRenderer.Blend(true, 10, 20), 6);
        Assert.Equal(0.0, TrayBadgeRenderer.Blend(true, 0, 20), 6);
        Assert.Equal(1.0, TrayBadgeRenderer.Blend(false, 0, 1));
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Charging")]
    public void Orange_moves_toward_the_target_white_S_and_alpha_stay(string name)
    {
        const int size = 24;
        var kind = Enum.Parse<BadgeKind>(name);
        int target = TrayBadgeRenderer.TargetRgb(kind);
        var tile = TileWith(size, out var s);
        var f = TrayBadgeRenderer.RenderFrames(kind, true, size, tile);
        var mid = f[5]; var peak = f[10];
        int x = 14, y = 4; // plain orange
        var (b0, g0, r0, _) = Px(tile, size, x, y);
        var (bm, gm, rm, _) = Px(mid, size, x, y);
        var (bp, gp, rp, _) = Px(peak, size, x, y);
        double D(int r, int g, int b) => Math.Abs(r - (target >> 16 & 255)) + Math.Abs(g - (target >> 8 & 255)) + Math.Abs(b - (target & 255));
        Assert.True(D(rm, gm, bm) < D(r0, g0, b0));
        Assert.True(D(rp, gp, bp) < D(rm, gm, bm));
        Assert.InRange(D(rp, gp, bp), 0, 6);
        // The S is untouched at every frame, and so is every alpha.
        foreach (var fr in f)
        {
            for (int yy = s.Y0; yy <= s.Y1; yy++)
                for (int xx = s.X0; xx <= s.X1; xx++) Assert.Equal(Px(tile, size, xx, yy), Px(fr, size, xx, yy));
            for (int i = 3; i < fr.Length; i += 4) Assert.Equal(tile[i], fr[i]);
        }

        // Antialiased edge: the half-alpha orange pixel keeps its alpha and reaches the target in straight colour; the half-white
        // fringe moves only its orange half, so it stays lighter than the flat target.
        var edge = Px(peak, size, 1, 10);
        Assert.Equal(128, edge.A);
        Assert.InRange(edge.R * 255 / 128, (target >> 16 & 255) - 4, (target >> 16 & 255) + 4);
        var fringe = Px(peak, size, s.X0 - 1, 10);
        Assert.True(fringe.R + fringe.G + fringe.B > rp + gp + bp + 100);
        Assert.Equal(0, Px(peak, size, 0, 0).A);
    }

    [Fact]
    public void Red_and_green_targets_read_as_red_and_green()
    {
        var tile = Tile(16);
        var red = Px(TrayBadgeRenderer.RenderFrames(BadgeKind.Low, false, 16, tile)[0], 16, 8, 3);
        var green = Px(TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, false, 16, tile)[0], 16, 8, 3);
        Assert.True(red.R > 180 && red.G < 90);
        Assert.True(green.G > 150 && green.R < 90);
    }

    [Fact]
    public void Frames_become_alpha_icons_that_read_back()
    {
        const int size = 16;
        var frame = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, false, size, Tile(size))[0];
        nint icon = TrayBadgeRenderer.MakeIcon(frame, size);
        Assert.NotEqual(0, icon);
        try
        {
            var back = TrayBadgeRenderer.ReadIconPixels(icon, size);
            Assert.NotNull(back);
            Assert.Equal(0, Px(back!, size, 0, 0).A);
            Assert.True(Px(back!, size, 8, 3).R > 180);
        }
        finally { DestroyIcon(icon); }
    }

    // ---- opt-in preview: SLENDER_BADGE_PNG_DIR=<dir> writes contact sheets of every frame, named {Low|Charging}-{anim|static}-{size}-{dark|light}.png ----

    private static string? FindIco()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "src", "Slender.Tray", "slender.ico");
            if (File.Exists(p)) return p;
        }

        return null;
    }

    private static byte[] BaseIcon(string ico, int size)
    {
        fixed (char* p = ico)
        {
            nint icon = LoadImageW(0, p, 1, size, size, 0x10 /* LR_LOADFROMFILE */);
            Assert.NotEqual(0, icon);
            try { return TrayBadgeRenderer.ReadIconPixels(icon, size)!; }
            finally { DestroyIcon(icon); }
        }
    }

    [Fact]
    public void Preview_sheets()
    {
        if (Environment.GetEnvironmentVariable("SLENDER_BADGE_PNG_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        var ico = FindIco();
        Assert.NotNull(ico);
        (string Name, byte R, byte G, byte B)[] bgs = [("dark", 0x20, 0x20, 0x20), ("light", 0xF3, 0xF3, 0xF3)];
        foreach (int size in new[] { 16, 20, 24, 32, 40 })
        {
            var basePx = BaseIcon(ico!, size);
            foreach (var (kind, kn) in new[] { (BadgeKind.Low, "Low"), (BadgeKind.Charging, "Charging") })
                foreach (bool anim in new[] { true, false })
                    foreach (var (name, r, g, b) in bgs)
                        WriteSheet(Path.Combine(dir, $"{kn}-{(anim ? "anim" : "static")}-{size}-{name}.png"), TrayBadgeRenderer.RenderFrames(kind, anim, size, basePx), size, r, g, b);
        }
    }

    private static void WriteSheet(string path, byte[][] frames, int size, byte br, byte bg, byte bb)
    {
        const int scale = 8, pad = 8, cols = 5;
        int cell = size * scale + pad;
        int rows8 = (frames.Length + cols - 1) / cols;
        int stripW = frames.Length * (size + 4) + pad;
        int w = Math.Max(cols * cell + pad, stripW), h = pad + size + pad + rows8 * cell + pad;
        var sheet = new byte[w * h * 4];
        for (int i = 0; i < sheet.Length; i += 4) { sheet[i] = bb; sheet[i + 1] = bg; sheet[i + 2] = br; sheet[i + 3] = 255; }

        void Put(byte[] f, int ox, int oy, int s)
        {
            for (int y = 0; y < size * s; y++)
                for (int x = 0; x < size * s; x++)
                {
                    int src = ((y / s) * size + x / s) * 4, dst = ((oy + y) * w + ox + x) * 4, a = f[src + 3];
                    sheet[dst] = (byte)(f[src] + bb * (255 - a) / 255);
                    sheet[dst + 1] = (byte)(f[src + 1] + bg * (255 - a) / 255);
                    sheet[dst + 2] = (byte)(f[src + 2] + br * (255 - a) / 255);
                }
        }

        for (int i = 0; i < frames.Length; i++)
        {
            Put(frames[i], pad + i * (size + 4), pad, 1);
            Put(frames[i], pad + (i % cols) * cell, pad + size + pad + (i / cols) * cell, scale);
        }

        PngWriter.Write(path, w, h, sheet);
    }
}
