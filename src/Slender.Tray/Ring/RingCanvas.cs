using System.Runtime.InteropServices;

namespace Slender.Tray.Ring;

/// <summary>Which font a piece of ring text uses.</summary>
internal enum RingFont { Label, Mono, Icons, Ui }

/// <summary>An RGBA colour, 0..1 per channel, straight (non-premultiplied) alpha.</summary>
internal readonly record struct RingColor(float R, float G, float B, float A = 1f)
{
    public static RingColor Rgb(int rgb, float a = 1f) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public RingColor WithAlpha(float a) => this with { A = a };

    /// <summary>Parses "#RRGGBB"; null when malformed.</summary>
    public static RingColor? TryParse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return Rgb(v);
    }
}

/// <summary>
/// Direct2D + DirectWrite drawing surface for the ring: a software DC render target bound to a 32-bit premultiplied DIB, so
/// the result goes to UpdateLayeredWindow without touching the GPU. Raw COM through vtable slots (no reflection, NativeAOT safe).
/// Coordinates are device-independent pixels (the render target carries the DPI). Everything is released by Dispose.
/// </summary>
internal sealed unsafe class RingCanvas : IDisposable
{
    // ---- COM structs (d2d1.h) ----
    [StructLayout(LayoutKind.Sequential)] private struct Color { public float R, G, B, A; }
    [StructLayout(LayoutKind.Sequential)] private struct Pt { public float X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SizeF { public float W, H; }
    [StructLayout(LayoutKind.Sequential)] private struct SizeU { public uint W, H; }
    [StructLayout(LayoutKind.Sequential)] private struct RectF { public float L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct Ellipse { public Pt Center; public float Rx, Ry; }
    [StructLayout(LayoutKind.Sequential)] private struct RoundedRect { public RectF Rect; public float Rx, Ry; }
    [StructLayout(LayoutKind.Sequential)] private struct ArcSegment { public Pt Point; public SizeF Size; public float Rotation; public int Sweep, ArcSize; }
    [StructLayout(LayoutKind.Sequential)] private struct Matrix { public float M11, M12, M21, M22, Dx, Dy; }
    [StructLayout(LayoutKind.Sequential)] private struct RenderTargetProps { public int Type; public uint Format; public int AlphaMode; public float DpiX, DpiY; public int Usage, MinLevel; }
    [StructLayout(LayoutKind.Sequential)] private struct StrokeProps { public int StartCap, EndCap, DashCap, LineJoin; public float MiterLimit; public int DashStyle; public float DashOffset; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapProps { public uint Format; public int AlphaMode; public float DpiX, DpiY; }

    private const uint DxgiB8G8R8A8 = 87;
    private static Guid IidD2DFactory = new(0x06152247, 0x6f50, 0x465a, 0x92, 0x45, 0x11, 0x8b, 0xfd, 0x3b, 0x60, 0x07);
    private static Guid IidDWriteFactory5 = new(0x958DB99A, 0xBE2A, 0x4F09, 0xAF, 0x7D, 0x65, 0x18, 0x98, 0x03, 0xD1, 0xD3);
    private static Guid IidDWriteFactory = new(0xb859ee5a, 0xd838, 0x4b5b, 0xa2, 0xe8, 0x1a, 0xdc, 0x7d, 0x93, 0xdb, 0x48);

    [DllImport("d2d1.dll")] private static extern int D2D1CreateFactory(int type, Guid* iid, void* options, nint* factory);
    [DllImport("dwrite.dll")] private static extern int DWriteCreateFactory(int type, Guid* iid, nint* factory);

    private readonly int _width, _height;
    private nint _d2d, _dwrite, _rt, _brush, _dashStyle, _roundStyle, _fontCollection, _memDc, _dib, _oldBitmap;
    private void* _bits;
    private readonly Dictionary<(RingFont, int, int), nint> _formats = new();
    private bool _disposed;

    /// <summary>Surface of widthPx x heightPx device pixels drawn in DIPs at <paramref name="dpiScale"/> (1.0 = 96 DPI).</summary>
    public RingCanvas(int widthPx, int heightPx, double dpiScale, string? fontDirectory)
    {
        _width = widthPx;
        _height = heightPx;
        try
        {
            CreateDevice(dpiScale);
            CreateFonts(fontDirectory);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width => _width;
    public int Height => _height;

    /// <summary>The premultiplied BGRA pixels (top-down, Width x Height x 4 bytes) while the canvas lives.</summary>
    public ReadOnlySpan<byte> Pixels => new(_bits, _width * _height * 4);

    public nint MemoryDc => _memDc;

    /// <summary>True when Sora / JetBrains Mono came from the bundled files (false = system fallbacks).</summary>
    public bool UsingBundledFonts { get; private set; }

    // ---- setup ----

    private void CreateDevice(double dpiScale)
    {
        var iid = IidD2DFactory;
        nint f;
        Check(D2D1CreateFactory(0, &iid, null, &f), "D2D1CreateFactory");
        _d2d = f;

        var bmi = new RingNative.BITMAPINFOHEADER
        {
            BiSize = (uint)sizeof(RingNative.BITMAPINFOHEADER), BiWidth = _width, BiHeight = -_height, BiPlanes = 1, BiBitCount = 32,
        };
        var screen = RingNative.GetDC(0);
        try
        {
            _memDc = RingNative.CreateCompatibleDC(screen);
            void* bits;
            _dib = RingNative.CreateDIBSection(screen, &bmi, 0, &bits, 0, 0);
            _bits = bits;
        }
        finally { RingNative.ReleaseDC(0, screen); }
        if (_memDc == 0 || _dib == 0 || _bits is null) throw new InvalidOperationException("Could not create the ring bitmap.");
        _oldBitmap = RingNative.SelectObject(_memDc, _dib);

        var props = new RenderTargetProps
        {
            Type = 1, // software: no GPU resources for a short-lived overlay
            Format = DxgiB8G8R8A8, AlphaMode = 1, // premultiplied
            DpiX = (float)(96 * dpiScale), DpiY = (float)(96 * dpiScale),
            Usage = 2, // GDI compatible
        };
        nint rt;
        Check(((delegate* unmanaged<nint, RenderTargetProps*, nint*, int>)Slot(_d2d, 16))(_d2d, &props, &rt), "CreateDCRenderTarget");
        _rt = rt;
        var rect = new RingNative.RECT { Right = _width, Bottom = _height };
        Check(((delegate* unmanaged<nint, nint, RingNative.RECT*, int>)Slot(_rt, 57))(_rt, _memDc, &rect), "BindDC");
        ((delegate* unmanaged<nint, int, void>)Slot(_rt, 34))(_rt, 2); // SetTextAntialiasMode: grayscale (ClearType needs an opaque target)

        var white = new Color { R = 1, G = 1, B = 1, A = 1 };
        nint brush;
        Check(((delegate* unmanaged<nint, Color*, void*, nint*, int>)Slot(_rt, 8))(_rt, &white, null, &brush), "CreateSolidColorBrush");
        _brush = brush;

        var dashes = stackalloc float[] { 4f, 4f };
        var dash = new StrokeProps { StartCap = 2, EndCap = 2, DashCap = 0, LineJoin = 2, MiterLimit = 10, DashStyle = 5 }; // custom dashes, round caps/joins
        nint style;
        Check(((delegate* unmanaged<nint, StrokeProps*, float*, uint, nint*, int>)Slot(_d2d, 11))(_d2d, &dash, dashes, 2, &style), "CreateStrokeStyle");
        _dashStyle = style;
        var round = new StrokeProps { StartCap = 2, EndCap = 2, DashCap = 2, LineJoin = 2, MiterLimit = 10, DashStyle = 0 };
        Check(((delegate* unmanaged<nint, StrokeProps*, float*, uint, nint*, int>)Slot(_d2d, 11))(_d2d, &round, null, 0, &style), "CreateStrokeStyle");
        _roundStyle = style;
    }

    private void CreateFonts(string? fontDirectory)
    {
        var iid5 = IidDWriteFactory5;
        nint f;
        var hr = DWriteCreateFactory(0, &iid5, &f);
        if (hr < 0)
        {
            var iid = IidDWriteFactory;
            Check(DWriteCreateFactory(0, &iid, &f), "DWriteCreateFactory");
            _dwrite = f;
            return; // older Windows: system fonts only
        }
        _dwrite = f;
        if (fontDirectory is null) return;
        try { _fontCollection = BuildCollection(fontDirectory); }
        catch { _fontCollection = 0; }
        UsingBundledFonts = _fontCollection != 0;
    }

    /// <summary>A font collection of the bundled Sora and JetBrains Mono files (IDWriteFactory5 font set, no loader callbacks).</summary>
    private nint BuildCollection(string dir)
    {
        string[] files = ["Sora-SemiBold.ttf", "JetBrainsMono-Medium.ttf"];
        nint builder;
        Check(((delegate* unmanaged<nint, nint*, int>)Slot(_dwrite, 43))(_dwrite, &builder), "CreateFontSetBuilder");
        nint set = 0, collection = 0;
        try
        {
            var added = 0;
            foreach (var file in files)
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) continue;
                nint fontFile;
                int hr;
                fixed (char* p = path) hr = ((delegate* unmanaged<nint, char*, void*, nint*, int>)Slot(_dwrite, 7))(_dwrite, p, null, &fontFile);
                if (hr < 0) continue;
                try { hr = ((delegate* unmanaged<nint, nint, int>)Slot(builder, 7))(builder, fontFile); }
                finally { Release(fontFile); }
                if (hr >= 0) added++;
            }
            if (added == 0) return 0;
            Check(((delegate* unmanaged<nint, nint*, int>)Slot(builder, 6))(builder, &set), "CreateFontSet");
            Check(((delegate* unmanaged<nint, nint, nint*, int>)Slot(_dwrite, 37))(_dwrite, set, &collection), "CreateFontCollectionFromFontSet");
            return collection;
        }
        finally
        {
            Release(set);
            Release(builder);
        }
    }

    // ---- frame ----

    public void Begin() => ((delegate* unmanaged<nint, void>)Slot(_rt, 48))(_rt);

    public void End()
    {
        ulong t1, t2;
        ((delegate* unmanaged<nint, ulong*, ulong*, int>)Slot(_rt, 49))(_rt, &t1, &t2);
    }

    public void Clear() => Clear(new RingColor(0, 0, 0, 0));

    public void Clear(RingColor c)
    {
        var col = ToColor(c);
        ((delegate* unmanaged<nint, Color*, void>)Slot(_rt, 47))(_rt, &col);
    }

    // ---- shapes ----

    public void FillCircle(double cx, double cy, double r, RingColor c)
    {
        var e = new Ellipse { Center = P(cx, cy), Rx = (float)r, Ry = (float)r };
        SetBrush(c);
        ((delegate* unmanaged<nint, Ellipse*, nint, void>)Slot(_rt, 21))(_rt, &e, _brush);
    }

    public void StrokeCircle(double cx, double cy, double r, RingColor c, double width, bool dashed = false)
    {
        var e = new Ellipse { Center = P(cx, cy), Rx = (float)r, Ry = (float)r };
        SetBrush(c);
        ((delegate* unmanaged<nint, Ellipse*, nint, float, nint, void>)Slot(_rt, 20))(_rt, &e, _brush, (float)width, dashed ? _dashStyle : 0);
    }

    public void Line(double x1, double y1, double x2, double y2, RingColor c, double width, bool dashed = false)
    {
        SetBrush(c);
        ((delegate* unmanaged<nint, Pt, Pt, nint, float, nint, void>)Slot(_rt, 15))(_rt, P(x1, y1), P(x2, y2), _brush, (float)width, dashed ? _dashStyle : _roundStyle);
    }

    public void FillRoundRect(double x, double y, double w, double h, double radius, RingColor c)
    {
        var rr = Rr(x, y, w, h, radius);
        SetBrush(c);
        ((delegate* unmanaged<nint, RoundedRect*, nint, void>)Slot(_rt, 19))(_rt, &rr, _brush);
    }

    public void StrokeRoundRect(double x, double y, double w, double h, double radius, RingColor c, double width, bool dashed = false)
    {
        var rr = Rr(x, y, w, h, radius);
        SetBrush(c);
        ((delegate* unmanaged<nint, RoundedRect*, nint, float, nint, void>)Slot(_rt, 18))(_rt, &rr, _brush, (float)width, dashed ? _dashStyle : 0);
    }

    /// <summary>A donut sector between radii r1 and r2 from angle a1 to a2 (degrees, 0 = right, clockwise on screen) around (cx, cy).</summary>
    public RingPath Wedge(double cx, double cy, double r1, double r2, double a1, double a2)
    {
        var path = new RingPath(this);
        path.Begin(Polar(cx, cy, r2, a1), filled: true);
        path.Arc(Polar(cx, cy, r2, a2), r2, clockwise: true);
        path.Line(Polar(cx, cy, r1, a2));
        path.Arc(Polar(cx, cy, r1, a1), r1, clockwise: false);
        path.End(closed: true);
        return path;
    }

    /// <summary>An open arc of radius r from angle a1 to a2.</summary>
    public RingPath ArcPath(double cx, double cy, double r, double a1, double a2)
    {
        var path = new RingPath(this);
        path.Begin(Polar(cx, cy, r, a1), filled: false);
        path.Arc(Polar(cx, cy, r, a2), r, clockwise: true);
        path.End(closed: false);
        return path;
    }

    public void Fill(RingPath path, RingColor c)
    {
        SetBrush(c);
        ((delegate* unmanaged<nint, nint, nint, nint, void>)Slot(_rt, 23))(_rt, path.Geometry, _brush, 0);
    }

    public void Stroke(RingPath path, RingColor c, double width, bool dashed = false)
    {
        SetBrush(c);
        ((delegate* unmanaged<nint, nint, nint, float, nint, void>)Slot(_rt, 22))(_rt, path.Geometry, _brush, (float)width, dashed ? _dashStyle : _roundStyle);
    }

    public static (double X, double Y) Polar(double cx, double cy, double r, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }

    // ---- bitmaps ----

    /// <summary>Uploads premultiplied BGRA pixels; the caller releases the handle with <see cref="ReleaseBitmap"/> (or Dispose of the canvas does not: keep it in <see cref="RingIconCache"/>).</summary>
    public nint CreateBitmap(int width, int height, byte[] premultipliedBgra)
    {
        var props = new BitmapProps { Format = DxgiB8G8R8A8, AlphaMode = 1, DpiX = 96, DpiY = 96 };
        nint bmp;
        fixed (byte* data = premultipliedBgra)
        {
            var hr = ((delegate* unmanaged<nint, SizeU, void*, uint, BitmapProps*, nint*, int>)Slot(_rt, 4))(_rt, new SizeU { W = (uint)width, H = (uint)height }, data, (uint)(width * 4), &props, &bmp);
            return hr < 0 ? 0 : bmp;
        }
    }

    public static void ReleaseBitmap(nint bitmap) => Release(bitmap);

    public void DrawBitmap(nint bitmap, double x, double y, double w, double h, double opacity = 1)
    {
        if (bitmap == 0) return;
        var dest = new RectF { L = (float)x, T = (float)y, R = (float)(x + w), B = (float)(y + h) };
        ((delegate* unmanaged<nint, nint, RectF*, float, int, RectF*, void>)Slot(_rt, 26))(_rt, bitmap, &dest, (float)opacity, 1, null);
    }

    // ---- text ----

    /// <summary>Draws one line of text vertically centred on cy; align -1 = x is the left edge, 0 = x is the centre, 1 = x is the right edge.</summary>
    public void Text(string text, RingFont font, double size, double x, double cy, RingColor color, int align = 0, double maxWidth = 240)
    {
        if (text.Length == 0) return;
        var format = Format(font, size, align);
        if (format == 0) return;
        var h = size * 2;
        var rect = align switch
        {
            < 0 => new RectF { L = (float)x, T = (float)(cy - h / 2), R = (float)(x + maxWidth), B = (float)(cy + h / 2) },
            > 0 => new RectF { L = (float)(x - maxWidth), T = (float)(cy - h / 2), R = (float)x, B = (float)(cy + h / 2) },
            _ => new RectF { L = (float)(x - maxWidth / 2), T = (float)(cy - h / 2), R = (float)(x + maxWidth / 2), B = (float)(cy + h / 2) },
        };
        SetBrush(color);
        fixed (char* s = text) ((delegate* unmanaged<nint, char*, uint, nint, RectF*, nint, int, int, void>)Slot(_rt, 27))(_rt, s, (uint)text.Length, format, &rect, _brush, 0, 0);
    }

    /// <summary>Text with a dark outline so it stays readable over any window (the compass labels).</summary>
    public void HaloText(string text, RingFont font, double size, double x, double cy, RingColor color, RingColor halo, int align = 0, double maxWidth = 240)
    {
        ReadOnlySpan<(double, double)> offsets = [(-1.5, 0), (1.5, 0), (0, -1.5), (0, 1.5), (-1.1, -1.1), (1.1, -1.1), (-1.1, 1.1), (1.1, 1.1)];
        foreach (var (dx, dy) in offsets) Text(text, font, size, x + dx, cy + dy, halo, align, maxWidth);
        Text(text, font, size, x, cy, color, align, maxWidth);
    }

    private nint Format(RingFont font, double size, int align)
    {
        var key = (font, (int)Math.Round(size * 10), align);
        if (_formats.TryGetValue(key, out var cached)) return cached;
        var (family, weight, custom) = font switch
        {
            RingFont.Label => UsingBundledFonts ? ("Sora", 600, true) : ("Segoe UI Variable Text", 600, false),
            RingFont.Mono => UsingBundledFonts ? ("JetBrains Mono", 500, true) : ("Consolas", 400, false),
            RingFont.Icons => ("Segoe Fluent Icons", 400, false),
            _ => ("Segoe UI Variable Text", 400, false),
        };
        nint format = 0;
        fixed (char* fam = family)
        fixed (char* loc = "en-us")
        {
            var hr = ((delegate* unmanaged<nint, char*, nint, int, int, int, float, char*, nint*, int>)Slot(_dwrite, 15))(
                _dwrite, fam, custom ? _fontCollection : 0, weight, 0, 5, (float)size, loc, &format);
            if (hr < 0) format = 0;
        }
        if (format != 0)
        {
            ((delegate* unmanaged<nint, int, int>)Slot(format, 3))(format, align < 0 ? 0 : align > 0 ? 1 : 2); // text alignment
            ((delegate* unmanaged<nint, int, int>)Slot(format, 4))(format, 2); // paragraph: centred vertically
            ((delegate* unmanaged<nint, int, int>)Slot(format, 5))(format, 1); // no wrap
        }
        _formats[key] = format;
        return format;
    }

    // ---- output ----

    /// <summary>Pushes the frame to a layered window (premultiplied alpha). Constant alpha 0..255 scales the whole ring.</summary>
    public bool Present(nint hwnd, int x, int y, byte constantAlpha)
    {
        var dst = new RingNative.POINT { X = x, Y = y };
        var src = new RingNative.POINT();
        var size = new RingNative.SIZE { Cx = _width, Cy = _height };
        var blend = new RingNative.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = constantAlpha, AlphaFormat = 1 };
        return RingNative.UpdateLayeredWindow(hwnd, 0, &dst, &size, _memDc, &src, 0, &blend, RingNative.ULW_ALPHA) != 0;
    }

    // ---- plumbing ----

    private void SetBrush(RingColor c)
    {
        var col = ToColor(c);
        ((delegate* unmanaged<nint, Color*, void>)Slot(_brush, 8))(_brush, &col);
    }

    private static Color ToColor(RingColor c) => new() { R = c.R, G = c.G, B = c.B, A = c.A };
    private static Pt P(double x, double y) => new() { X = (float)x, Y = (float)y };
    private static Pt P((double X, double Y) p) => P(p.X, p.Y);

    private static RoundedRect Rr(double x, double y, double w, double h, double r) => new()
    {
        Rect = new RectF { L = (float)x, T = (float)y, R = (float)(x + w), B = (float)(y + h) }, Rx = (float)r, Ry = (float)r,
    };

    internal static nint Slot(nint obj, int index) => ((nint*)*(nint*)obj)[index];

    internal static void Release(nint obj)
    {
        if (obj != 0) ((delegate* unmanaged<nint, uint>)Slot(obj, 2))(obj);
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what} failed (0x{hr:X8})");
    }

    internal nint CreatePathGeometry(out nint sink)
    {
        nint g, s;
        Check(((delegate* unmanaged<nint, nint*, int>)Slot(_d2d, 10))(_d2d, &g), "CreatePathGeometry");
        Check(((delegate* unmanaged<nint, nint*, int>)Slot(g, 17))(g, &s), "PathGeometry.Open");
        sink = s;
        return g;
    }

    internal static void SinkBegin(nint sink, (double X, double Y) start, bool filled) =>
        ((delegate* unmanaged<nint, Pt, int, void>)Slot(sink, 5))(sink, P(start), filled ? 0 : 1);

    internal static void SinkLine(nint sink, (double X, double Y) to) =>
        ((delegate* unmanaged<nint, Pt, void>)Slot(sink, 10))(sink, P(to));

    internal static void SinkArc(nint sink, (double X, double Y) to, double radius, bool clockwise)
    {
        var arc = new ArcSegment { Point = P(to), Size = new SizeF { W = (float)radius, H = (float)radius }, Rotation = 0, Sweep = clockwise ? 1 : 0, ArcSize = 0 };
        ((delegate* unmanaged<nint, ArcSegment*, void>)Slot(sink, 14))(sink, &arc);
    }

    internal static void SinkEnd(nint sink, bool closed) =>
        ((delegate* unmanaged<nint, int, void>)Slot(sink, 8))(sink, closed ? 1 : 0);

    internal static int SinkClose(nint sink) => ((delegate* unmanaged<nint, int>)Slot(sink, 9))(sink);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var f in _formats.Values) Release(f);
        _formats.Clear();
        Release(_fontCollection);
        Release(_dwrite);
        Release(_dashStyle);
        Release(_roundStyle);
        Release(_brush);
        Release(_rt);
        Release(_d2d);
        _fontCollection = _dwrite = _dashStyle = _roundStyle = _brush = _rt = _d2d = 0;
        if (_memDc != 0)
        {
            if (_oldBitmap != 0) RingNative.SelectObject(_memDc, _oldBitmap);
            RingNative.DeleteDC(_memDc);
            _memDc = 0;
        }
        if (_dib != 0)
        {
            RingNative.DeleteObject(_dib);
            _dib = 0;
        }
        _bits = null;
    }
}

/// <summary>A path geometry under construction; Dispose releases it. Figures: Begin, Line/Arc..., End.</summary>
internal sealed class RingPath : IDisposable
{
    private nint _geometry, _sink;

    public RingPath(RingCanvas canvas) => _geometry = canvas.CreatePathGeometry(out _sink);

    public nint Geometry => _geometry;

    public void Begin((double X, double Y) start, bool filled) => RingCanvas.SinkBegin(_sink, start, filled);

    public void Line((double X, double Y) to) => RingCanvas.SinkLine(_sink, to);

    public void Arc((double X, double Y) to, double radius, bool clockwise) => RingCanvas.SinkArc(_sink, to, radius, clockwise);

    public void End(bool closed)
    {
        RingCanvas.SinkEnd(_sink, closed);
        RingCanvas.SinkClose(_sink);
        RingCanvas.Release(_sink);
        _sink = 0;
    }

    public void Dispose()
    {
        RingCanvas.Release(_sink);
        RingCanvas.Release(_geometry);
        _sink = _geometry = 0;
    }
}
