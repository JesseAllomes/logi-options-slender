using System.Runtime.InteropServices;
using Slender.Tray.Ring;

namespace Slender.Tray;

/// <summary>What one badge frame looks like: how far the S has crossfaded to a bolt (0..1) and the opacity of the fill.</summary>
internal readonly record struct BadgeFrame(double Bolt, double FillAlpha);

/// <summary>
/// Draws the tray icon's badges with the ring's Direct2D canvas and turns each frame into an HICON. The corner battery shows the
/// real level as a static fill; while charging, the Slender S crossfades to a white bolt and back. Nothing is kept between calls:
/// <see cref="RenderFrames"/> creates a canvas, draws one loop and releases the canvas before it returns.
/// Frames are premultiplied BGRA, top-down, size x size; identical frames share one array.
/// </summary>
internal static unsafe class TrayBadgeRenderer
{
    private static readonly RingColor Body = RingColor.Rgb(0x1C1C1E, 0.92f);
    private static readonly RingColor Outline = RingColor.Rgb(0xD8D8DC);
    private static readonly RingColor Green = RingColor.Rgb(0x30D158);
    private static readonly RingColor Red = RingColor.Rgb(0xFF3B30);
    private static readonly RingColor White = RingColor.Rgb(0xFFFFFF);

    /// <summary>Where the battery badge sits for an icon size, in icon pixels.</summary>
    internal readonly record struct Layout(int Size, int X, int Y, int BodyW, int H, int NubW, int NubH, int Stroke, int Pad, int Gap, double Radius)
    {
        /// <summary>Pixel width of the gauge interior: the fill at 100 %.</summary>
        public int FillMax => BodyW - 2 * (Stroke + Pad);
    }

    internal static Layout LayoutFor(int size)
    {
        int w = Math.Max(7, (int)Math.Round(size * 0.46, MidpointRounding.AwayFromZero));
        int h = Math.Max(4, (int)Math.Round(size * 0.27, MidpointRounding.AwayFromZero));
        int nubW = size >= 32 ? 2 : 1;
        int nubH = Math.Min(h, Math.Max(2, (int)Math.Round(h * 0.45, MidpointRounding.AwayFromZero)));
        if ((h - nubH) % 2 != 0) nubH = Math.Min(h, nubH + 1);
        int stroke = Math.Max(1, (int)Math.Round(size / 24.0, MidpointRounding.AwayFromZero));
        int gap = size >= 32 ? 2 : 1;
        return new Layout(size, size - w, size - h, w - nubW, h, nubW, nubH, stroke, size >= 20 ? 1 : 0, gap, h * 0.30);
    }

    /// <summary>Drawn fill width in pixels for a battery percent: rounded to the gauge interior, never under 1 px.</summary>
    internal static int FillWidth(int size, int percent) =>
        Math.Max(1, (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * LayoutFor(size).FillMax, MidpointRounding.AwayFromZero));

    // ---- frame parameters (pure) ----

    private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>The look of frame <paramref name="index"/> of <paramref name="count"/> for a kind; one frame (index 0) is the static look.</summary>
    internal static BadgeFrame Describe(BadgeKind kind, bool animated, int index, int count)
    {
        double phase = count <= 1 ? 0 : (double)index / count;
        switch (kind)
        {
            case BadgeKind.Charging when !animated:
                return new BadgeFrame(1, 1);
            case BadgeKind.Charging:
            {
                int hold = BadgeTiming.ChargeHoldMs, fade = BadgeTiming.ChargeFadeMs;
                double t = phase * BadgeTiming.PeriodMs(BadgeKind.Charging);
                if (t < hold) return new BadgeFrame(0, 1);
                t -= hold;
                if (t < fade) return new BadgeFrame(EaseInOutCubic(t / fade), 1);
                t -= fade;
                if (t < hold) return new BadgeFrame(1, 1);
                t -= hold;
                return new BadgeFrame(1 - EaseInOutCubic(Math.Clamp(t / fade, 0, 1)), 1);
            }
            case BadgeKind.Low when animated:
                // Only the red fill pulses (1.0 -> 0.35 -> 1.0 on a cosine); body and outline stay solid.
                return new BadgeFrame(0, 0.675 + 0.325 * Math.Cos(phase * 2 * Math.PI));
            default:
                return new BadgeFrame(0, 1);
        }
    }

    // ---- rendering ----

    /// <summary>
    /// One loop of frames for a kind over the base icon (premultiplied BGRA, size x size) with the battery fill <paramref name="fillWidth"/> px wide.
    /// The canvas is gone when this returns.
    /// </summary>
    public static byte[][] RenderFrames(BadgeKind kind, bool animated, int size, ReadOnlySpan<byte> basePixels, int fillWidth)
    {
        int count = BadgeTiming.FrameCount(kind, animated);
        if (count == 0 || size < 8 || basePixels.Length != size * size * 4) return [];
        var lay = LayoutFor(size);
        fillWidth = Math.Clamp(fillWidth, 1, lay.FillMax);
        var cut = CutCoverage(lay);
        var color = kind == BadgeKind.Low ? Red : Green;
        using var canvas = new RingCanvas(size, size, 1.0, null);

        byte[]? boltBase = kind == BadgeKind.Charging ? BoltIcon(canvas, basePixels, size) : null;
        var layers = new Dictionary<double, byte[]>();
        var composed = new Dictionary<(double, double), byte[]>();
        var frames = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            var f = Describe(kind, animated, i, count);
            double a = Math.Round(f.FillAlpha, 3), b = Math.Round(f.Bolt, 3);
            if (!composed.TryGetValue((a, b), out var frame))
            {
                if (!layers.TryGetValue(a, out var layer))
                {
                    canvas.Begin();
                    canvas.Clear();
                    DrawBadge(canvas, lay, fillWidth, color.WithAlpha((float)a));
                    canvas.End();
                    layer = canvas.Pixels.ToArray();
                    layers[a] = layer;
                }

                frame = Composite(boltBase is null ? basePixels : Lerp(basePixels, boltBase, b), layer, cut);
                composed[(a, b)] = frame;
            }

            frames[i] = frame;
        }

        return frames;
    }

    private static void DrawBadge(RingCanvas c, Layout l, int fillWidth, RingColor color)
    {
        double x = l.X, y = l.Y, t = l.Stroke;
        c.FillRoundRect(x, y, l.BodyW, l.H, l.Radius, Body);
        // Terminal nub, joined to the outline.
        double nubY = y + (l.H - l.NubH) / 2.0;
        c.FillRoundRect(x + l.BodyW, nubY, l.NubW, l.NubH, l.NubW * 0.5, Outline);
        c.StrokeRoundRect(x + t / 2, y + t / 2, l.BodyW - t, l.H - t, Math.Max(0, l.Radius - t / 2), Outline, t);

        double fx = x + t + l.Pad, fy = y + t + l.Pad, fh = l.H - 2 * (t + l.Pad);
        if (fillWidth >= 1 && color.A > 0)
        {
            double r = fillWidth <= 2 ? 0 : Math.Min(Math.Max(0.5, l.Radius - t - l.Pad), Math.Min(fh / 2, fillWidth / 2.0));
            c.FillRoundRect(fx, fy, fillWidth, fh, r, color);
        }
    }

    // ---- the S and the bolt ----

    // A bolt in a 7 x 10 box; other sizes scale it, snapping to whole pixels up to 24 px.
    private static readonly (double X, double Y)[] BoltUnit = [(4, 0), (7, 0), (5, 4), (7, 4), (2, 10), (3, 6), (0, 6)];

    private static RingPath BoltPath(RingCanvas c, double x, double y, int w, int h, bool snap)
    {
        var path = new RingPath(c);
        for (int i = 0; i < BoltUnit.Length; i++)
        {
            double px = x + BoltUnit[i].X / 7 * w, py = y + BoltUnit[i].Y / 10 * h;
            if (snap) { px = Math.Round(px); py = Math.Round(py); }
            if (i == 0) path.Begin((px, py), filled: true); else path.Line((px, py));
        }

        path.End(closed: true);
        return path;
    }

    /// <summary>The glyph in the base icon: where it is, how big, and the flat colour behind it.</summary>
    internal readonly record struct Glyph(bool Found, byte R, byte G, byte B, int X0, int Y0, int X1, int Y1, double Cx, double Cy, bool[] Mask);

    /// <summary>Finds the icon's flat orange (the most common opaque saturated colour) and the glyph (opaque pixels that differ from it).</summary>
    internal static Glyph FindGlyph(ReadOnlySpan<byte> px, int size)
    {
        var hist = new Dictionary<int, int>();
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] != 255 || px[i + 2] - px[i] < 60) continue;
            int key = px[i] | px[i + 1] << 8 | px[i + 2] << 16;
            hist[key] = hist.GetValueOrDefault(key) + 1;
        }

        var empty = new Glyph(false, 0, 0, 0, 0, 0, -1, -1, 0, 0, new bool[size * size]);
        if (hist.Count == 0) return empty;
        int orange = hist.MaxBy(kv => kv.Value).Key;
        byte ob = (byte)orange, og = (byte)(orange >> 8), or = (byte)(orange >> 16);
        var mask = new bool[size * size];
        int x0 = size, y0 = size, x1 = -1, y1 = -1;
        double sw = 0, sx = 0, sy = 0;
        int margin = Math.Max(2, size / 8);
        for (int y = margin; y < size - margin; y++)
        {
            for (int x = margin; x < size - margin; x++)
            {
                int o = (y * size + x) * 4;
                if (px[o + 3] != 255) continue;
                int d = Math.Abs(px[o] - ob) + Math.Abs(px[o + 1] - og) + Math.Abs(px[o + 2] - or);
                if (d <= 24) continue;
                mask[y * size + x] = true;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                double wgt = Math.Min(1, d / 200.0);
                sw += wgt; sx += wgt * (x + 0.5); sy += wgt * (y + 0.5);
            }
        }

        if (x1 < 0 || sw <= 0) return empty;
        return new Glyph(true, or, og, ob, x0, y0, x1, y1, sx / sw, sy / sw, mask);
    }

    /// <summary>The base icon with the S covered by its flat orange and a white bolt of the same weight drawn where the S sat.</summary>
    private static byte[]? BoltIcon(RingCanvas canvas, ReadOnlySpan<byte> basePx, int size)
    {
        var g = FindGlyph(basePx, size);
        if (!g.Found) return null;
        int gh = g.Y1 - g.Y0 + 1;
        int bh = Math.Max(7, gh + (size <= 24 ? 1 : 0)), bw = Math.Max(5, (int)Math.Round(bh * 0.62, MidpointRounding.AwayFromZero));
        bool snap = size <= 24;
        double bx = g.Cx - bw / 2.0, by = g.Cy - bh / 2.0;
        if (snap) { bx = Math.Round(bx); by = Math.Round(by); }

        canvas.Begin();
        canvas.Clear();
        using (var path = BoltPath(canvas, bx, by, bw, bh, snap)) canvas.Fill(path, White);
        canvas.End();
        var bolt = canvas.Pixels;

        var o = basePx.ToArray();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < size && yy < size && g.Mask[yy * size + xx]) { near = true; break; }
                    }

                int i = (y * size + x) * 4;
                if (near)
                {
                    o[i] = g.B; o[i + 1] = g.G; o[i + 2] = g.R; o[i + 3] = 255;
                }

                int ba = bolt[i + 3];
                if (ba > 0)
                {
                    for (int k = 0; k < 4; k++) o[i + k] = (byte)Math.Min(255, bolt[i + k] + o[i + k] * (255 - ba) / 255);
                }
            }
        }

        return o;
    }

    private static byte[] Lerp(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, double t)
    {
        var o = new byte[a.Length];
        for (int i = 0; i < o.Length; i++) o[i] = (byte)Math.Round(a[i] + (b[i] - a[i]) * t);
        return o;
    }

    /// <summary>Per-pixel coverage (0..1) of the transparent notch around the badge: the badge rect grown by the gap, with rounded corners.</summary>
    private static float[] CutCoverage(Layout l)
    {
        int size = l.Size, g = l.Gap;
        double left = l.X - g, top = l.Y - g, right = size + g, bottom = size + g, r = l.Radius + g;
        double hw = (right - left) / 2, hh = (bottom - top) / 2, cx = (left + right) / 2, cy = (top + bottom) / 2;
        var cov = new float[size * size];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double dx = Math.Abs(px + 0.5 - cx) - (hw - r), dy = Math.Abs(py + 0.5 - cy) - (hh - r);
                double sd = Math.Sqrt(Math.Max(dx, 0) * Math.Max(dx, 0) + Math.Max(dy, 0) * Math.Max(dy, 0)) + Math.Min(Math.Max(dx, dy), 0) - r;
                cov[py * size + px] = (float)Math.Clamp(0.5 - sd, 0, 1);
            }
        }

        return cov;
    }

    /// <summary>badge over (base with the notch cleared), all premultiplied.</summary>
    private static byte[] Composite(ReadOnlySpan<byte> basePx, ReadOnlySpan<byte> badge, float[] cut)
    {
        var o = new byte[basePx.Length];
        for (int p = 0, i = 0; p < cut.Length; p++, i += 4)
        {
            double keep = (1 - cut[p]) * (1 - badge[i + 3] / 255.0);
            for (int k = 0; k < 4; k++) o[i + k] = (byte)Math.Clamp(Math.Round(badge[i + k] + basePx[i + k] * keep), 0, 255);
        }

        return o;
    }

    // ---- icon handles ----

    /// <summary>Renders the frames and wraps each distinct one in an HICON (identical frames share a handle); the caller destroys every distinct handle.</summary>
    public static nint[] RenderIcons(BadgeKind kind, bool animated, int size, int fillWidth, nint baseIcon)
    {
        var basePixels = ReadIconPixels(baseIcon, size);
        if (basePixels is null) return [];
        var frames = RenderFrames(kind, animated, size, basePixels, fillWidth);
        var made = new Dictionary<byte[], nint>(ReferenceEqualityComparer.Instance);
        var icons = new nint[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            if (!made.TryGetValue(frames[i], out nint icon))
            {
                icon = MakeIcon(frames[i], size);
                if (icon == 0)
                {
                    foreach (var m in made.Values) RingNative.DestroyIcon(m);
                    return [];
                }

                made[frames[i]] = icon;
            }

            icons[i] = icon;
        }

        return icons;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public int FIcon; public uint XHotspot, YHotspot; public nint Mask, Color; }

    [DllImport("user32.dll")] private static extern int GetIconInfo(nint icon, IconInfo* info);
    [DllImport("user32.dll")] private static extern nint CreateIconIndirect(IconInfo* info);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int w, int h, uint planes, uint bits, void* data);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, void* bits, RingNative.BITMAPINFOHEADER* info, uint usage);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(nint obj, int size, void* buffer);

    /// <summary>The icon's pixels as premultiplied BGRA at <paramref name="size"/> (nearest-neighbour when the icon is another size); null when unreadable.</summary>
    public static byte[]? ReadIconPixels(nint icon, int size)
    {
        if (icon == 0 || size <= 0) return null;
        IconInfo info;
        if (GetIconInfo(icon, &info) == 0) return null;
        try
        {
            if (info.Color == 0) return null;
            var dims = stackalloc int[8]; // BITMAP: type, width, height, widthBytes, ...
            if (GetObjectW(info.Color, 32, dims) == 0 || dims[1] <= 0 || dims[2] <= 0) return null;
            int sw = dims[1], sh = dims[2];
            var color = ReadBits(info.Color, sw, sh);
            if (color is null) return null;
            bool hasAlpha = false;
            for (int i = 3; i < color.Length; i += 4) if (color[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha)
            {
                // An icon without an alpha channel keeps its shape in the AND mask (0 = opaque).
                var mask = info.Mask == 0 ? null : ReadBits(info.Mask, sw, sh);
                for (int i = 0; i < color.Length; i += 4) color[i + 3] = mask is null || mask[i] == 0 ? (byte)255 : (byte)0;
            }

            var o = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int s = ((y * sh / size) * sw + x * sw / size) * 4, d = (y * size + x) * 4, a = color[s + 3];
                    o[d] = (byte)(color[s] * a / 255);
                    o[d + 1] = (byte)(color[s + 1] * a / 255);
                    o[d + 2] = (byte)(color[s + 2] * a / 255);
                    o[d + 3] = (byte)a;
                }
            }

            return o;
        }
        finally
        {
            RingNative.DeleteObject(info.Color);
            RingNative.DeleteObject(info.Mask);
        }
    }

    private static byte[]? ReadBits(nint bitmap, int w, int h)
    {
        var bmi = new RingNative.BITMAPINFOHEADER { BiSize = (uint)sizeof(RingNative.BITMAPINFOHEADER), BiWidth = w, BiHeight = -h, BiPlanes = 1, BiBitCount = 32 };
        var buf = new byte[w * h * 4];
        nint dc = RingNative.GetDC(0);
        try
        {
            fixed (byte* p = buf) return GetDIBits(dc, bitmap, 0, (uint)h, p, &bmi, 0) == 0 ? null : buf;
        }
        finally { RingNative.ReleaseDC(0, dc); }
    }

    /// <summary>An alpha HICON from premultiplied BGRA pixels (icons carry straight alpha); 0 on failure.</summary>
    public static nint MakeIcon(ReadOnlySpan<byte> premultiplied, int size)
    {
        var bmi = new RingNative.BITMAPINFOHEADER { BiSize = (uint)sizeof(RingNative.BITMAPINFOHEADER), BiWidth = size, BiHeight = -size, BiPlanes = 1, BiBitCount = 32 };
        nint dc = RingNative.GetDC(0);
        void* bits;
        nint color;
        try { color = RingNative.CreateDIBSection(dc, &bmi, 0, &bits, 0, 0); }
        finally { RingNative.ReleaseDC(0, dc); }
        if (color == 0 || bits is null) return 0;
        var dst = new Span<byte>(bits, size * size * 4);
        for (int i = 0; i < dst.Length; i += 4)
        {
            int a = premultiplied[i + 3];
            for (int k = 0; k < 3; k++) dst[i + k] = a == 0 ? (byte)0 : (byte)Math.Min(255, (premultiplied[i + k] * 255 + a / 2) / a);
            dst[i + 3] = (byte)a;
        }

        var maskBits = new byte[((size + 15) / 16) * 2 * size];
        nint mask;
        fixed (byte* m = maskBits) mask = CreateBitmap(size, size, 1, 1, m);
        try
        {
            if (mask == 0) return 0;
            var ii = new IconInfo { FIcon = 1, Mask = mask, Color = color };
            return CreateIconIndirect(&ii);
        }
        finally
        {
            RingNative.DeleteObject(color);
            RingNative.DeleteObject(mask);
        }
    }
}
