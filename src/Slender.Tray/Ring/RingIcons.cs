namespace Slender.Tray.Ring;

/// <summary>Loads a file's shell icon as premultiplied BGRA pixels.</summary>
internal static unsafe class FileIcons
{
    /// <summary>The icon of <paramref name="path"/> at about <paramref name="px"/> pixels square, or null when it has none.</summary>
    public static byte[]? Load(string path, int px)
    {
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
