using System.Runtime.InteropServices;

namespace Slender.Tray.Ring;

/// <summary>Loads a file's shell icon as premultiplied BGRA pixels.</summary>
internal static unsafe class FileIcons
{
    /// <summary>The icon of <paramref name="path"/> at about <paramref name="px"/> pixels square, or null when it has none.</summary>
    public static byte[]? Load(string path, int px)
    {
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) return PngImages.Load(path, px);
        nint icon = 0;
        try
        {
            icon = Extract(path, px);
            if (icon == 0) return null;
            return Rasterise(icon, px);
        }
        catch { return null; }
        finally
        {
            if (icon != 0) RingNative.DestroyIcon(icon);
        }
    }

    private static nint Extract(string path, int px)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            nint large = 0;
            fixed (char* p = path)
            {
                if (RingNative.SHDefExtractIconW(p, 0, 0, &large, null, (uint)px) >= 0 && large != 0) return large;
            }
        }
        RingNative.SHFILEINFOW info = default;
        fixed (char* p = path)
        {
            return RingNative.SHGetFileInfoW(p, 0, &info, (uint)sizeof(RingNative.SHFILEINFOW), RingNative.SHGFI_ICON | RingNative.SHGFI_LARGEICON) != 0 ? info.HIcon : 0;
        }
    }

    /// <summary>
    /// Draws the icon on opaque black and on opaque white 32-bit surfaces; the difference gives the exact alpha for any icon
    /// format (alpha, mask or none), and the black render is already premultiplied.
    /// </summary>
    private static byte[]? Rasterise(nint icon, int px)
    {
        var onBlack = Draw(icon, px, white: false);
        var onWhite = Draw(icon, px, white: true);
        if (onBlack is null || onWhite is null) return null;
        var result = new byte[px * px * 4];
        var any = false;
        for (var i = 0; i < px * px; i++)
        {
            var o = i * 4;
            var alpha = 255 - Math.Max(onWhite[o] - onBlack[o], Math.Max(onWhite[o + 1] - onBlack[o + 1], onWhite[o + 2] - onBlack[o + 2]));
            alpha = Math.Clamp(alpha, 0, 255);
            result[o] = (byte)Math.Min(onBlack[o], alpha);
            result[o + 1] = (byte)Math.Min(onBlack[o + 1], alpha);
            result[o + 2] = (byte)Math.Min(onBlack[o + 2], alpha);
            result[o + 3] = (byte)alpha;
            if (alpha != 0) any = true;
        }
        return any ? result : null;
    }

    private static byte[]? Draw(nint icon, int px, bool white)
    {
        var bmi = new RingNative.BITMAPINFOHEADER { BiSize = (uint)sizeof(RingNative.BITMAPINFOHEADER), BiWidth = px, BiHeight = -px, BiPlanes = 1, BiBitCount = 32 };
        var screen = RingNative.GetDC(0);
        nint dc = 0, dib = 0, old = 0;
        try
        {
            dc = RingNative.CreateCompatibleDC(screen);
            void* bits;
            dib = RingNative.CreateDIBSection(screen, &bmi, 0, &bits, 0, 0);
            if (dc == 0 || dib == 0 || bits is null) return null;
            old = RingNative.SelectObject(dc, dib);
            new Span<byte>(bits, px * px * 4).Fill(white ? (byte)255 : (byte)0);
            if (RingNative.DrawIconEx(dc, 0, 0, icon, px, px, 0, 0, RingNative.DI_NORMAL) == 0) return null;
            return new Span<byte>(bits, px * px * 4).ToArray();
        }
        finally
        {
            if (old != 0) RingNative.SelectObject(dc, old);
            if (dib != 0) RingNative.DeleteObject(dib);
            if (dc != 0) RingNative.DeleteDC(dc);
            RingNative.ReleaseDC(0, screen);
        }
    }
}

/// <summary>
/// Loads a PNG (an imported action image) scaled to fit <c>px</c> square, as premultiplied BGRA, through the GDI+ flat API
/// (plain blittable P/Invoke, so NativeAOT safe). GDI+ is started once and left running for the life of the tray.
/// </summary>
internal static unsafe class PngImages
{
    private const int PixelFormat32bppPArgb = 0x000E200B, ImageLockModeRead = 1, HighQualityBicubic = 7;
    private static bool _started;
    private static bool _failed;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput { public uint Version; public nint DebugCallback; public int SuppressBackgroundThread, SuppressExternalCodecs; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapData { public uint Width, Height; public int Stride, PixelFormat; public void* Scan0; public nuint Reserved; }

    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(nuint* token, StartupInput* input, void* output);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromFile(char* file, nint* bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, void* scan0, nint* bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, uint* width);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, uint* height);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, nint* graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint color);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int width, int height);
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapLockBits(nint bitmap, Rect* rect, int flags, int format, BitmapData* data);
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapUnlockBits(nint bitmap, BitmapData* data);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);

    public static byte[]? Load(string path, int px)
    {
        nint source = 0, target = 0, g = 0;
        try
        {
            if (!Start()) return null;
            fixed (char* p = path)
            {
                if (GdipCreateBitmapFromFile(p, &source) != 0 || source == 0) return null;
            }

            uint w = 0, h = 0;
            if (GdipGetImageWidth(source, &w) != 0 || GdipGetImageHeight(source, &h) != 0 || w == 0 || h == 0) return null;
            if (GdipCreateBitmapFromScan0(px, px, 0, PixelFormat32bppPArgb, null, &target) != 0 || target == 0) return null;
            if (GdipGetImageGraphicsContext(target, &g) != 0 || g == 0) return null;
            GdipGraphicsClear(g, 0);
            GdipSetInterpolationMode(g, HighQualityBicubic);
            var scale = Math.Min((double)px / w, (double)px / h);
            int dw = Math.Max(1, (int)Math.Round(w * scale)), dh = Math.Max(1, (int)Math.Round(h * scale));
            if (GdipDrawImageRectI(g, source, (px - dw) / 2, (px - dh) / 2, dw, dh) != 0) return null;

            var rect = new Rect { Width = px, Height = px };
            BitmapData data = default;
            if (GdipBitmapLockBits(target, &rect, ImageLockModeRead, PixelFormat32bppPArgb, &data) != 0) return null;
            try
            {
                var result = new byte[px * px * 4];
                for (var y = 0; y < px; y++)
                    new ReadOnlySpan<byte>((byte*)data.Scan0 + (long)y * data.Stride, px * 4).CopyTo(result.AsSpan(y * px * 4));
                return result;
            }
            finally
            {
                GdipBitmapUnlockBits(target, &data);
            }
        }
        catch { return null; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (target != 0) GdipDisposeImage(target);
            if (source != 0) GdipDisposeImage(source);
        }
    }

    private static bool Start()
    {
        if (_started) return true;
        if (_failed) return false;
        nuint token;
        var input = new StartupInput { Version = 1 };
        if (GdiplusStartup(&token, &input, null) != 0) { _failed = true; return false; }
        _started = true;
        return true;
    }
}

/// <summary>File-icon bitmaps for one open ring, keyed by path; released with the ring.</summary>
internal sealed class RingIconCache : IDisposable
{
    private readonly RingCanvas _canvas;
    private readonly Dictionary<string, nint> _icons = new(StringComparer.OrdinalIgnoreCase);

    public RingIconCache(RingCanvas canvas) => _canvas = canvas;

    /// <summary>The uploaded bitmap for the file's icon, or 0 when it has none.</summary>
    public nint Get(string path, int px)
    {
        if (_icons.TryGetValue(path, out var cached)) return cached;
        nint bitmap = 0;
        if (FileIcons.Load(path, px) is { } pixels) bitmap = _canvas.CreateBitmap(px, px, pixels);
        _icons[path] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var b in _icons.Values) RingCanvas.ReleaseBitmap(b);
        _icons.Clear();
    }
}
