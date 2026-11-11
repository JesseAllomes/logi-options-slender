using System.Runtime.InteropServices;
using Slender.Tray.Ring;

namespace Slender.Tray;

/// <summary>What one badge frame looks like: how full the battery is, how opaque the fill and the whole badge are.</summary>
internal readonly record struct BadgeFrame(double Fill, double FillAlpha, double Opacity, bool Sliver, bool Bolt);

/// <summary>
/// Draws the tray icon's battery badge with the ring's Direct2D canvas and turns each frame into an HICON. Nothing is kept
/// between calls: <see cref="RenderFrames"/> creates a canvas, draws one loop and releases the canvas before it returns.
/// Frames are premultiplied BGRA, top-down, size x size.
/// </summary>
internal static unsafe class TrayBadgeRenderer
{
    private static readonly RingColor Body = RingColor.Rgb(0x1C1C1E, 0.92f);
    private static readonly RingColor Outline = RingColor.Rgb(0xD8D8DC);
    private static readonly RingColor Green = RingColor.Rgb(0x34C759);
    private static readonly RingColor Red = RingColor.Rgb(0xFF3B30);
    private static readonly RingColor White = RingColor.Rgb(0xFFFFFF);

    /// <summary>Where the badge sits for an icon size, in icon pixels.</summary>
    internal readonly record struct Layout(int Size, int X, int Y, int BodyW, int H, int NubW, int NubH, int Stroke, int Pad, int Gap, double Radius);

    internal static Layout LayoutFor(int size)
    {
        int w = Math.Max(7, (int)Math.Round(size * 0.46, MidpointRounding.AwayFromZero));
        int h = Math.Max(4, (int)Math.Round(size * 0.27, MidpointRounding.AwayFromZero));
        int nubW = Math.Max(1, size >= 32 ? 2 : 1);
        int nubH = Math.Min(h, Math.Max(2, (int)Math.Round(h * 0.45, MidpointRounding.AwayFromZero)));
        if ((h - nubH) % 2 != 0) nubH = Math.Min(h, nubH + 1);
        int stroke = Math.Max(1, (int)Math.Round(size / 24.0, MidpointRounding.AwayFromZero));
        int gap = size >= 32 ? 2 : 1;
        return new Layout(size, size - w, size - h, w - nubW, h, nubW, nubH, stroke, size >= 20 ? 1 : 0, gap, h * 0.30);
    }

    // ---- frame parameters (pure) ----

    private const double ChargeFadeFloor = 0.6;

    private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>The look of frame <paramref name="index"/> of <paramref name="count"/> for a kind; one frame (index 0) is the static look.</summary>
    internal static BadgeFrame Describe(BadgeKind kind, bool animated, int index, int count)
    {
        double phase = count <= 1 ? 0 : (double)index / count;
        switch (kind)
        {
            case BadgeKind.Full:
                return new BadgeFrame(1, 1, 1, false, false);
            case BadgeKind.Charging when !animated:
                return new BadgeFrame(2.0 / 3.0, 1, 1, false, true);
            case BadgeKind.Charging:
            {
                double t = phase * BadgeTiming.PeriodMs(BadgeKind.Charging);
                // Starts as a sliver at the faded level of the previous loop's end, so the loop is continuous.
                if (t < BadgeTiming.ChargeGrowMs) return new BadgeFrame(0.12 + 0.88 * EaseInOutCubic(t / BadgeTiming.ChargeGrowMs), Math.Min(1, ChargeFadeFloor + (1 - ChargeFadeFloor) * t / 240), 1, true, true);
                t -= BadgeTiming.ChargeGrowMs;
                if (t < BadgeTiming.ChargeHoldMs) return new BadgeFrame(1, 1, 1, false, true);
                t -= BadgeTiming.ChargeHoldMs;
                return new BadgeFrame(1, 1 - (1 - ChargeFadeFloor) * Math.Clamp(t / BadgeTiming.ChargeFadeMs, 0, 1), 1, false, true);
            }
            case BadgeKind.Low when !animated:
                return new BadgeFrame(0.2, 1, 1, true, false);
            case BadgeKind.Low:
                // Only the red fill pulses (1.0 -> 0.35 -> 1.0 on a cosine); body and outline stay solid.
                return new BadgeFrame(0.2, 0.675 + 0.325 * Math.Cos(phase * 2 * Math.PI), 1, true, false);
            default:
                return new BadgeFrame(0, 1, 1, false, false);
        }
    }

    // ---- rendering ----

    /// <summary>One loop of frames for a kind over the base icon (premultiplied BGRA, size x size). The canvas is gone when this returns.</summary>
    public static byte[][] RenderFrames(BadgeKind kind, bool animated, int size, ReadOnlySpan<byte> basePixels)
    {
        int count = BadgeTiming.FrameCount(kind, animated);
        if (count == 0 || size < 8 || basePixels.Length != size * size * 4) return [];
        var lay = LayoutFor(size);
        var cut = CutCoverage(lay);
        var frames = new byte[count][];
        using var canvas = new RingCanvas(size, size, 1.0, null);
        for (int i = 0; i < count; i++)
        {
            var frame = Describe(kind, animated, i, count);
            canvas.Begin();
            canvas.Clear();
            DrawBadge(canvas, lay, frame, kind == BadgeKind.Low ? Red : Green);
            canvas.End();
            frames[i] = Composite(basePixels, canvas.Pixels, cut, frame.Opacity);
        }

        return frames;
    }

    private static void DrawBadge(RingCanvas c, Layout l, BadgeFrame f, RingColor color)
    {
        double x = l.X, y = l.Y, t = l.Stroke;
        c.FillRoundRect(x, y, l.BodyW, l.H, l.Radius, Body);
        // Terminal nub, joined to the outline.
        double nubY = y + (l.H - l.NubH) / 2.0;
        c.FillRoundRect(x + l.BodyW, nubY, l.NubW, l.NubH, l.NubW * 0.5, Outline);
        c.StrokeRoundRect(x + t / 2, y + t / 2, l.BodyW - t, l.H - t, Math.Max(0, l.Radius - t / 2), Outline, t);

        double fx = x + t + l.Pad, fy = y + t + l.Pad, fw = l.BodyW - 2 * (t + l.Pad), fh = l.H - 2 * (t + l.Pad);
        double width = Math.Round(f.Fill * fw);
        if (f.Sliver) width = Math.Max(1, width);
        if (width >= 1 && f.FillAlpha > 0)
        {
            double r = width <= 2 ? 0 : Math.Min(Math.Max(0.5, l.Radius - t - l.Pad), Math.Min(fh / 2, width / 2));
            c.FillRoundRect(fx, fy, width, fh, r, color.WithAlpha((float)f.FillAlpha));
        }

        if (f.Bolt && fh >= 7) // only where the gauge is tall enough for a crisp bolt
        {
            double cx = fx + fw / 2, cy = fy + fh / 2, s = fh * 1.0;
            ReadOnlySpan<(double, double)> pts = [(0.12, -0.5), (-0.28, 0.1), (-0.04, 0.1), (-0.12, 0.5), (0.28, -0.1), (0.04, -0.1)];
            using var path = new RingPath(c);
            path.Begin((cx + pts[0].Item1 * s, cy + pts[0].Item2 * s), filled: true);
            for (int i = 1; i < pts.Length; i++) path.Line((cx + pts[i].Item1 * s, cy + pts[i].Item2 * s));
            path.End(closed: true);
            c.Fill(path, White);
        }
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

    /// <summary>badge * opacity over (base with the notch cleared), all premultiplied.</summary>
    private static byte[] Composite(ReadOnlySpan<byte> basePx, ReadOnlySpan<byte> badge, float[] cut, double opacity)
    {
        var o = new byte[basePx.Length];
        for (int p = 0, i = 0; p < cut.Length; p++, i += 4)
        {
            double ba = badge[i + 3] / 255.0 * opacity, keep = (1 - cut[p]) * (1 - ba);
            for (int k = 0; k < 4; k++) o[i + k] = (byte)Math.Clamp(Math.Round(badge[i + k] * opacity + basePx[i + k] * keep), 0, 255);
        }

        return o;
    }

    // ---- icon handles ----

    /// <summary>Renders the frames and wraps each in an HICON; the caller destroys every handle.</summary>
    public static nint[] RenderIcons(BadgeKind kind, bool animated, int size, nint baseIcon)
    {
        var basePixels = ReadIconPixels(baseIcon, size);
        if (basePixels is null) return [];
        var frames = RenderFrames(kind, animated, size, basePixels);
        var icons = new List<nint>(frames.Length);
        foreach (var f in frames)
        {
            nint icon = MakeIcon(f, size);
            if (icon == 0)
            {
                foreach (var made in icons) RingNative.DestroyIcon(made);
                return [];
            }

            icons.Add(icon);
        }

        return [.. icons];
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
