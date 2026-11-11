using System.Runtime.InteropServices;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed unsafe class TrayBadgeRendererTests
{
    [DllImport("user32.dll")] private static extern int DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint LoadImageW(nint inst, char* name, uint type, int cx, int cy, uint flags);

    private static byte[] Gray(int size)
    {
        var b = new byte[size * size * 4];
        for (int i = 0; i < b.Length; i += 4) { b[i] = b[i + 1] = b[i + 2] = 0x80; b[i + 3] = 0xFF; }
        return b;
    }

    /// <summary>An orange tile with a white block standing in for the S, at the spot the real S sits.</summary>
    private static byte[] Tile(int size, out (int X0, int Y0, int X1, int Y1) glyph)
    {
        var b = new byte[size * size * 4];
        for (int i = 0; i < b.Length; i += 4) { b[i] = 0x33; b[i + 1] = 0x73; b[i + 2] = 0xB8; b[i + 3] = 0xFF; }
        glyph = (size * 3 / 8, size * 3 / 8, size * 3 / 8 + size / 4, size * 3 / 8 + size * 3 / 8);
        for (int y = glyph.Y0; y <= glyph.Y1; y++)
            for (int x = glyph.X0; x <= glyph.X1; x++) { int o = (y * size + x) * 4; b[o] = b[o + 1] = b[o + 2] = 0xFF; }
        return b;
    }

    private static (byte B, byte G, byte R, byte A) Px(byte[] frame, int size, int x, int y)
    {
        int o = (y * size + x) * 4;
        return (frame[o], frame[o + 1], frame[o + 2], frame[o + 3]);
    }

    private static int FillPixels(byte[] frame, int size, bool red)
    {
        var l = TrayBadgeRenderer.LayoutFor(size);
        int y = l.Y + l.H / 2, n = 0;
        for (int x = l.X; x < l.X + l.BodyW; x++)
        {
            var p = Px(frame, size, x, y);
            if (p.A >= 200 && (red ? p.R > p.G + 90 : p.G > p.R + 90)) n++;
        }

        return n;
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    public void Frames_have_the_right_count_and_size(int size)
    {
        var basePx = Tile(size, out _);
        int fw = TrayBadgeRenderer.FillWidth(size, 50);
        var charging = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, basePx, fw);
        Assert.Equal(BadgeTiming.FrameCount(BadgeKind.Charging, true), charging.Length);
        Assert.Equal(40, charging.Length);
        Assert.Equal(BadgeTiming.FrameCount(BadgeKind.Low, true), TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, basePx, fw).Length);
        var full = TrayBadgeRenderer.RenderFrames(BadgeKind.Full, true, size, basePx, 99);
        Assert.Single(full);
        Assert.Equal(size * size * 4, full[0].Length);
        Assert.Empty(TrayBadgeRenderer.RenderFrames(BadgeKind.None, true, size, basePx, fw));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    public void Level_maps_to_a_fill_width_in_whole_pixels(int size)
    {
        int max = TrayBadgeRenderer.LayoutFor(size).FillMax;
        Assert.Equal(1, TrayBadgeRenderer.FillWidth(size, 0));
        Assert.Equal(1, TrayBadgeRenderer.FillWidth(size, 1));
        Assert.Equal(max, TrayBadgeRenderer.FillWidth(size, 100));
        Assert.Equal(max, TrayBadgeRenderer.FillWidth(size, 250));
        Assert.Equal((int)Math.Round(max / 2.0, MidpointRounding.AwayFromZero), TrayBadgeRenderer.FillWidth(size, 50));
        int prev = 0;
        for (int p = 0; p <= 100; p++)
        {
            int w = TrayBadgeRenderer.FillWidth(size, p);
            Assert.InRange(w, prev, max);
            prev = w;
        }
    }

    [Theory]
    [InlineData(16, 15)]
    [InlineData(16, 85)]
    [InlineData(24, 50)]
    [InlineData(32, 85)]
    public void Drawn_green_fill_has_exactly_the_level_width(int size, int percent)
    {
        int fw = TrayBadgeRenderer.FillWidth(size, percent);
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, false, size, Gray(size), fw);
        Assert.Single(frames);
        Assert.Equal(fw, FillPixels(frames[0], size, red: false));
    }

    [Fact]
    public void Full_badge_is_a_full_solid_green_gauge_in_a_transparent_notch()
    {
        const int size = 24;
        var l = TrayBadgeRenderer.LayoutFor(size);
        var f = TrayBadgeRenderer.RenderFrames(BadgeKind.Full, true, size, Gray(size), l.FillMax)[0];
        Assert.Equal(l.FillMax, FillPixels(f, size, red: false));
        Assert.Equal(((byte)0x80, (byte)0x80, (byte)0x80, (byte)0xFF), Px(f, size, 0, 0));
        Assert.Equal(0, Px(f, size, l.X - 1, l.Y + l.H / 2).A);
        Assert.Equal(0, Px(f, size, l.X + l.BodyW / 2, l.Y - 1).A);
        Assert.Equal(((byte)0x58, (byte)0xD1, (byte)0x30, (byte)0xFF), Px(f, size, l.X + l.Stroke + l.Pad + 1, l.Y + l.H / 2)); // #30D158
    }

    [Fact]
    public void Charging_fill_does_not_move_between_frames()
    {
        const int size = 16;
        int fw = TrayBadgeRenderer.FillWidth(size, 50);
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, Gray(size), fw);
        Assert.All(frames, f => Assert.Equal(fw, FillPixels(f, size, red: false)));
    }

    [Fact]
    public void Crossfade_holds_the_S_then_eases_to_the_bolt_and_back()
    {
        int n = BadgeTiming.FrameCount(BadgeKind.Charging, true);
        double B(int i) => TrayBadgeRenderer.Describe(BadgeKind.Charging, true, i, n).Bolt;
        for (int i = 0; i < 15; i++) Assert.Equal(0, B(i));              // S holds 1.2 s (15 frames)
        for (int i = 20; i <= 34; i++) Assert.Equal(1, B(i));            // bolt holds from 1.6 s to 2.8 s
        for (int i = 16; i <= 20; i++) Assert.True(B(i) >= B(i - 1));    // fades in over 0.4 s
        for (int i = 36; i < n; i++) Assert.True(B(i) <= B(i - 1));      // and back out
        Assert.InRange(B(17), 0.01, 0.99);
        Assert.InRange(B(n - 1), 0.0, 0.5);
        Assert.Equal(1, TrayBadgeRenderer.Describe(BadgeKind.Charging, false, 0, 1).Bolt); // reduced motion: static bolt
        Assert.Equal(0, TrayBadgeRenderer.Describe(BadgeKind.Full, true, 0, 1).Bolt);
        Assert.Equal(0, TrayBadgeRenderer.Describe(BadgeKind.Low, true, 5, 20).Bolt);
    }

    [Fact]
    public void Glyph_finder_reads_the_flat_orange_and_the_S_box()
    {
        var tile = Tile(24, out var g);
        var found = TrayBadgeRenderer.FindGlyph(tile, 24);
        Assert.True(found.Found);
        Assert.Equal((0xB8, 0x73, 0x33), ((int)found.R, (int)found.G, (int)found.B));
        Assert.Equal(g, (found.X0, found.Y0, found.X1, found.Y1));
        Assert.False(TrayBadgeRenderer.FindGlyph(Gray(24), 24).Found);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Charging_swaps_the_S_for_a_white_bolt_on_flat_orange(int size)
    {
        var tile = Tile(size, out var g);
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, tile, TrayBadgeRenderer.FillWidth(size, 50));
        Assert.Same(frames[0], frames[10]);                // identical hold frames share one array
        Assert.Same(frames[22], frames[30]);
        Assert.NotSame(frames[0], frames[22]);
        static int White(byte[] f, int s, (int X0, int Y0, int X1, int Y1) r)
        {
            int n = 0;
            for (int y = r.Y0 - 1; y <= r.Y1 + 1; y++)
                for (int x = r.X0 - 1; x <= r.X1 + 1; x++) { int o = (y * s + x) * 4; if (f[o] > 235 && f[o + 1] > 235 && f[o + 2] > 235) n++; }
            return n;
        }

        int s0 = White(frames[0], size, g), bolt = White(frames[22], size, g);
        Assert.True(bolt > 0, "the bolt is white");
        Assert.NotEqual(s0, bolt);
        Assert.Equal(((byte)0x33, (byte)0x73, (byte)0xB8, (byte)0xFF), Px(frames[22], size, 2, 2)); // outside the S box the orange is untouched
    }

    [Fact]
    public void Low_keeps_the_S_and_pulses_only_the_red_fill_at_the_level()
    {
        const int size = 16;
        var tile = Tile(size, out var g);
        int fw = TrayBadgeRenderer.FillWidth(size, 5);
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, tile, fw);
        Assert.Equal(20, frames.Length);
        var l = TrayBadgeRenderer.LayoutFor(size);
        int fy = l.Y + l.H / 2, fx = l.X + l.Stroke + l.Pad;
        var bright = Px(frames[0], size, fx, fy);
        var dim = Px(frames[10], size, fx, fy);
        Assert.True(bright.R > 200 && bright.G < 100);
        Assert.True(dim.R < bright.R * 0.6, "the red fill dims");
        Assert.Equal(Px(frames[0], size, l.X + l.BodyW - 2, fy), Px(frames[10], size, l.X + l.BodyW - 2, fy)); // body stays solid
        Assert.Equal(Px(tile, size, g.X0, g.Y0), Px(frames[10], size, g.X0, g.Y0));                              // the S stays
        var stat = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, false, size, tile, fw);
        Assert.Single(stat);
        Assert.True(Px(stat[0], size, fx, fy).R > 200);
    }

    [Fact]
    public void Frames_become_alpha_icons_that_read_back()
    {
        const int size = 16;
        var l = TrayBadgeRenderer.LayoutFor(size);
        var frame = TrayBadgeRenderer.RenderFrames(BadgeKind.Full, false, size, Gray(size), l.FillMax)[0];
        nint icon = TrayBadgeRenderer.MakeIcon(frame, size);
        Assert.NotEqual(0, icon);
        try
        {
            var back = TrayBadgeRenderer.ReadIconPixels(icon, size);
            Assert.NotNull(back);
            Assert.Equal(0, Px(back!, size, l.X - 1, l.Y + l.H / 2).A);
            Assert.True(Px(back!, size, l.X + l.Stroke + 1, l.Y + l.H / 2).G > 180);
            Assert.Equal(0x80, Px(back!, size, 0, 0).R);
        }
        finally { DestroyIcon(icon); }
    }

    // ---- opt-in preview: SLENDER_BADGE_PNG_DIR=<dir> writes contact sheets of every frame ----

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
            void Sheet(string file, byte[][] frames)
            {
                foreach (var (name, r, g, b) in bgs) WriteSheet(Path.Combine(dir, file.Replace("{bg}", name)), frames, size, r, g, b);
            }

            foreach (int level in new[] { 15, 50, 85 })
                Sheet($"Charging-anim-{size}-{{bg}}-L{level}.png", TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, basePx, TrayBadgeRenderer.FillWidth(size, level)));
            Sheet($"Charging-static-{size}-{{bg}}-L50.png", TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, false, size, basePx, TrayBadgeRenderer.FillWidth(size, 50)));
            foreach (int level in new[] { 5, 15 })
                Sheet($"Low-anim-{size}-{{bg}}-L{level}.png", TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, basePx, TrayBadgeRenderer.FillWidth(size, level)));
            Sheet($"Low-static-{size}-{{bg}}-L15.png", TrayBadgeRenderer.RenderFrames(BadgeKind.Low, false, size, basePx, TrayBadgeRenderer.FillWidth(size, 15)));
            Sheet($"Full-static-{size}-{{bg}}.png", TrayBadgeRenderer.RenderFrames(BadgeKind.Full, true, size, basePx, 100));
        }
    }

    private static void WriteSheet(string path, byte[][] frames, int size, byte br, byte bg, byte bb)
    {
        const int scale = 8, pad = 8, cols = 6;
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
