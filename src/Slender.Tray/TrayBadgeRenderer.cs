using System.Runtime.InteropServices;
using Slender.Tray.Ring;

namespace Slender.Tray;

/// <summary>
/// Recolours the tray icon for the pulse. The icon's orange rounded square is remapped pixel by pixel toward a target colour (red for
/// low, green for charging); the white S, each pixel's alpha and the antialiased edges are kept. Pure CPU work on the base icon's
/// pixels, no canvas: one loop is rendered when a state starts and turned into alpha HICONs. Frames are premultiplied BGRA, top-down,
/// size x size; identical frames share one array.
/// </summary>
internal static unsafe class TrayBadgeRenderer
{
    /// <summary>Pulse peak colours (0xRRGGBB).</summary>
    internal const int RedRgb = 0xE0352B, GreenRgb = 0x2DBE60;

    internal static int TargetRgb(BadgeKind kind) => kind == BadgeKind.Low ? RedRgb : GreenRgb;

    /// <summary>How far toward the target the frame is (0 = the icon's own orange, 1 = the target): a sine over the loop, starting and ending at 0.</summary>
    internal static double Blend(bool animated, int index, int count) =>
        !animated || count <= 1 ? 1 : (1 - Math.Cos(2 * Math.PI * index / count)) / 2;

    /// <summary>The icon's flat orange (the most common opaque saturated colour) as R, G, B; null when there is none.</summary>
    internal static (int R, int G, int B)? FindOrange(ReadOnlySpan<byte> px)
    {
        var hist = new Dictionary<int, int>();
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] != 255 || px[i + 2] - px[i] < 60) continue;
            int key = px[i] | px[i + 1] << 8 | px[i + 2] << 16;
            hist[key] = hist.GetValueOrDefault(key) + 1;
        }

        if (hist.Count == 0) return null;
        int o = hist.MaxBy(kv => kv.Value).Key;
        return (o >> 16 & 255, o >> 8 & 255, o & 255);
    }

    private static double Lum(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

    /// <summary>
    /// The base icon with its orange moved <paramref name="t"/> (0..1) of the way to <paramref name="targetRgb"/>. Each pixel is split into an
    /// orange part and a white part (the S and its antialiasing); only the orange part moves, keeping its own shading, so the S stays white.
    /// </summary>
    internal static byte[] Recolor(ReadOnlySpan<byte> basePx, (int R, int G, int B) orange, int targetRgb, double t)
    {
        var o = basePx.ToArray();
        if (t <= 0) return o;
        double tr = targetRgb >> 16 & 255, tg = targetRgb >> 8 & 255, tb = targetRgb & 255;
        double ol = Math.Max(1, Lum(orange.R, orange.G, orange.B));
        double span = Math.Max(1, 255 - orange.B);
        for (int i = 0; i < o.Length; i += 4)
        {
            int a = basePx[i + 3];
            if (a == 0) continue;
            double b = basePx[i] * 255.0 / a, g = basePx[i + 1] * 255.0 / a, r = basePx[i + 2] * 255.0 / a; // straight colour
            double s = Math.Clamp((b - orange.B) / span, 0, 1);       // white share, read from blue (orange is low in blue)
            double u = 1 - s;                                          // orange share
            if (u < 0.02) continue;
            // How well the pixel is "orange mixed with white": a pixel of some other colour is left alone.
            double err = Math.Abs(r - (orange.R * u + 255 * s)) + Math.Abs(g - (orange.G * u + 255 * s)) + Math.Abs(b - (orange.B * u + 255 * s));
            double fit = Math.Clamp(1 - err / 120, 0, 1);
            if (fit <= 0) continue;
            double pr = (r - 255 * s) / u, pg = (g - 255 * s) / u, pb = (b - 255 * s) / u;   // the orange part, with its shading
            double k = Math.Clamp(Lum(pr, pg, pb) / ol, 0.4, 1.6);
            double w = t * u * fit;                                    // how much of this pixel moves
            double nr = Math.Clamp(tr * k, 0, 255), ng = Math.Clamp(tg * k, 0, 255), nb = Math.Clamp(tb * k, 0, 255);
            double outR = r + (nr - pr) * w, outG = g + (ng - pg) * w, outB = b + (nb - pb) * w;
            o[i] = (byte)Math.Clamp(Math.Round(outB * a / 255.0), 0, 255);
            o[i + 1] = (byte)Math.Clamp(Math.Round(outG * a / 255.0), 0, 255);
            o[i + 2] = (byte)Math.Clamp(Math.Round(outR * a / 255.0), 0, 255);
        }

        return o;
    }

    /// <summary>
    /// One loop of frames for a kind over the base icon (premultiplied BGRA, size x size); a single peak-colour frame when static. Frames
    /// whose blend is the same share one array. Empty for no pulse or an icon with no orange to move.
    /// </summary>
    public static byte[][] RenderFrames(BadgeKind kind, bool animated, int size, ReadOnlySpan<byte> basePixels)
    {
        int count = BadgeTiming.FrameCount(kind, animated);
        if (count == 0 || size < 8 || basePixels.Length != size * size * 4 || FindOrange(basePixels) is not { } orange) return [];
        var made = new Dictionary<int, byte[]>();
        var frames = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            double t = Blend(animated, i, count);
            int key = (int)Math.Round(Math.Round(t, 6) * 255);
            if (!made.TryGetValue(key, out var frame)) made[key] = frame = Recolor(basePixels, orange, TargetRgb(kind), key / 255.0);
            frames[i] = frame;
        }

        return frames;
    }

    // ---- icon handles ----

    /// <summary>Renders the frames and wraps each distinct one in an HICON (identical frames share a handle); the caller destroys every distinct handle.</summary>
    public static nint[] RenderIcons(BadgeKind kind, bool animated, int size, nint baseIcon)
    {
        var basePixels = ReadIconPixels(baseIcon, size);
        if (basePixels is null) return [];
        var frames = RenderFrames(kind, animated, size, basePixels);
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
