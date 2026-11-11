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

    private static (byte B, byte G, byte R, byte A) Px(byte[] frame, int size, int x, int y)
    {
        int o = (y * size + x) * 4;
        return (frame[o], frame[o + 1], frame[o + 2], frame[o + 3]);
    }

    private static (int X, int Y) Centre(TrayBadgeRenderer.Layout l) => (l.X + l.Stroke + l.Pad + 1, l.Y + l.H / 2);

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    public void Frames_have_the_right_count_and_size(int size)
    {
        var basePx = Gray(size);
        Assert.Equal(BadgeTiming.FrameCount(BadgeKind.Charging, true), TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, basePx).Length);
        Assert.Equal(BadgeTiming.FrameCount(BadgeKind.Low, true), TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, basePx).Length);
        var full = TrayBadgeRenderer.RenderFrames(BadgeKind.Full, true, size, basePx);
        Assert.Single(full);
        Assert.Equal(size * size * 4, full[0].Length);
        Assert.Empty(TrayBadgeRenderer.RenderFrames(BadgeKind.None, true, size, basePx));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Full_badge_is_solid_green_inside_a_transparent_notch_over_the_base(int size)
    {
        var basePx = Gray(size);
        var f = TrayBadgeRenderer.RenderFrames(BadgeKind.Full, false, size, basePx)[0];
        var l = TrayBadgeRenderer.LayoutFor(size);

        Assert.Equal(((byte)0x80, (byte)0x80, (byte)0x80, (byte)0xFF), Px(f, size, 0, 0)); // base untouched away from the badge
        Assert.Equal(0, Px(f, size, l.X - 1, l.Y + l.H / 2).A);                            // the notch is transparent
        Assert.Equal(0, Px(f, size, l.X + l.BodyW / 2, l.Y - 1).A);
        var (cx, cy) = Centre(l);
        var fill = Px(f, size, cx, cy);
        Assert.Equal(((byte)0x59, (byte)0xC7, (byte)0x34, (byte)0xFF), fill);               // #34C759
        Assert.True(Px(f, size, size - 1 - l.NubW, size - 1).A > 0 || Px(f, size, l.X + 2, size - 1).A > 0); // badge reaches the corner
    }

    [Fact]
    public void Charging_fill_grows_then_holds_and_fades()
    {
        const int size = 24;
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, true, size, Gray(size));
        var l = TrayBadgeRenderer.LayoutFor(size);
        int fy = l.Y + l.H / 2, firstX = l.X + l.Stroke + l.Pad, lastX = l.X + l.BodyW - l.Stroke - l.Pad - 1;
        static bool IsGreen((byte B, byte G, byte R, byte A) p) => p.G > 150 && p.R < 120 && p.A > 200;

        int Width(int i) { int w = 0; for (int x = firstX; x <= lastX; x++) if (Px(frames[i], size, x, fy).G > 100 && Px(frames[i], size, x, fy).R < 120) w++; return w; }
        Assert.InRange(Width(0), 1, 2);                                               // starts as a sliver, not empty
        int count = frames.Length;
        int growEnd = (int)(count * 1200.0 / 1800.0);
        Assert.True(IsGreen(Px(frames[growEnd + 1], size, lastX, fy)));              // full while holding
        Assert.True(Width(count - 1) > 0);                                           // the end fade stays visible
        Assert.True(Px(frames[count - 1], size, firstX, fy).G < Px(frames[growEnd + 1], size, firstX, fy).G);
        int prev = -1;
        for (int i = 0; i <= growEnd; i++)
        {
            int w = 0;
            for (int x = firstX; x <= lastX; x++) if (IsGreen(Px(frames[i], size, x, fy))) w++;
            Assert.True(w >= prev, $"fill shrank at frame {i}");
            prev = w;
        }
    }

    [Fact]
    public void Reduced_motion_charging_is_a_static_two_thirds_fill()
    {
        const int size = 24;
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, false, size, Gray(size));
        Assert.Single(frames);
        var l = TrayBadgeRenderer.LayoutFor(size);
        int fy = l.Y + l.H / 2, x0 = l.X + l.Stroke + l.Pad, x1 = l.X + l.BodyW - l.Stroke - l.Pad - 1;
        int total = x1 - x0 + 1, green = 0;
        for (int x = x0; x <= x1; x++) if (Px(frames[0], size, x, fy).G > 150 && Px(frames[0], size, x, fy).R < 120) green++;
        Assert.InRange(green, total * 0.55, total * 0.75);
    }

    [Fact]
    public void Low_badge_is_a_red_sliver_that_breathes()
    {
        const int size = 16;
        var frames = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, true, size, Gray(size));
        var l = TrayBadgeRenderer.LayoutFor(size);
        int fy = l.Y + l.H / 2, fx = l.X + l.Stroke + l.Pad;
        var p0 = Px(frames[0], size, fx, fy);
        Assert.True(p0.R > 200 && p0.G < 100 && p0.A > 240, "first interior pixel is red");
        var half = Px(frames[frames.Length / 2], size, fx, fy);
        Assert.True(half.R < p0.R * 0.6, $"the red fill dims at mid loop ({half.R} vs {p0.R})");
        var bodyPx = Px(frames[frames.Length / 2], size, l.X + l.BodyW - 2, l.Y + l.H / 2);
        Assert.Equal(Px(frames[0], size, l.X + l.BodyW - 2, l.Y + l.H / 2), bodyPx); // body stays solid
        var stat = TrayBadgeRenderer.RenderFrames(BadgeKind.Low, false, size, Gray(size));
        Assert.Single(stat);
        Assert.True(Px(stat[0], size, fx, fy).R > 200);
    }

    [Fact]
    public void Describe_follows_the_spec()
    {
        Assert.InRange(TrayBadgeRenderer.Describe(BadgeKind.Charging, true, 0, 22).Fill, 0.05, 0.2);
        Assert.Equal(1, TrayBadgeRenderer.Describe(BadgeKind.Charging, true, 17, 22).Fill);
        Assert.InRange(TrayBadgeRenderer.Describe(BadgeKind.Charging, true, 21, 22).FillAlpha, 0.55, 0.99);
        Assert.Equal(1.0, TrayBadgeRenderer.Describe(BadgeKind.Low, true, 0, 20).FillAlpha, 3);
        Assert.Equal(0.35, TrayBadgeRenderer.Describe(BadgeKind.Low, true, 10, 20).FillAlpha, 3);
        Assert.Equal(1.0, TrayBadgeRenderer.Describe(BadgeKind.Low, true, 10, 20).Opacity, 3);
        Assert.Equal(2.0 / 3.0, TrayBadgeRenderer.Describe(BadgeKind.Charging, false, 0, 1).Fill, 3);
        Assert.False(TrayBadgeRenderer.Describe(BadgeKind.Full, true, 0, 1).Bolt);
    }

    [Fact]
    public void Bolt_is_drawn_only_when_the_gauge_is_tall_enough()
    {
        static int WhitePixels(byte[] f, int size) { int n = 0; for (int i = 0; i < f.Length; i += 4) if (f[i] > 248 && f[i + 2] > 248 && f[i + 1] > 248 && f[i + 3] > 248) n++; return n; }
        var small = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, false, 24, Gray(24))[0];
        var big = TrayBadgeRenderer.RenderFrames(BadgeKind.Charging, false, 64, Gray(64))[0];
        Assert.Equal(0, WhitePixels(small, 24));
        Assert.True(WhitePixels(big, 64) > 0);
    }

    [Fact]
    public void Frames_become_alpha_icons_that_read_back()
    {
        const int size = 16;
        var frame = TrayBadgeRenderer.RenderFrames(BadgeKind.Full, false, size, Gray(size))[0];
        nint icon = TrayBadgeRenderer.MakeIcon(frame, size);
        Assert.NotEqual(0, icon);
        try
        {
            var back = TrayBadgeRenderer.ReadIconPixels(icon, size);
            Assert.NotNull(back);
            var l = TrayBadgeRenderer.LayoutFor(size);
            Assert.Equal(0, Px(back!, size, l.X - 1, l.Y + l.H / 2).A);
            var (cx, cy) = Centre(l);
            Assert.True(Px(back!, size, cx, cy).G > 180);
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
        BadgeKind[] kinds = [BadgeKind.Charging, BadgeKind.Low, BadgeKind.Full];
        var bbox = new System.Text.StringBuilder();
        foreach (int size in new[] { 16, 20, 24, 32, 40 })
        {
            var basePx = BaseIcon(ico!, size);
            int x0 = size, y0 = size, x1 = -1, y1 = -1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int o = (y * size + x) * 4;
                    if (basePx[o] > 200 && basePx[o + 1] > 200 && basePx[o + 2] > 200 && basePx[o + 3] > 200) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                }
            var lay = TrayBadgeRenderer.LayoutFor(size);
            bbox.AppendLine($"{size}: glyph x{x0}-{x1} y{y0}-{y1}; badge x{lay.X}.. y{lay.Y}.. gap{lay.Gap}");
            foreach (var kind in kinds)
            {
                foreach (bool animated in new[] { true, false })
                {
                    if (!animated && kind == BadgeKind.Full) continue;
                    var frames = TrayBadgeRenderer.RenderFrames(kind, animated, size, basePx);
                    foreach (var (name, r, g, b) in bgs)
                        WriteSheet(Path.Combine(dir, $"{kind}-{(animated ? "anim" : "static")}-{size}-{name}.png"), frames, size, r, g, b);
                }
            }
        }
        File.WriteAllText(Path.Combine(dir, "glyph-bbox.txt"), bbox.ToString());
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
