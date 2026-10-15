using System.Runtime.InteropServices;

namespace Slender.Tray.Ring;

/// <summary>Win32 and GDI calls for the ring overlay. Blittable signatures only (NativeAOT safe).</summary>
internal static unsafe class RingNative
{
    public const uint WM_HOTKEY = 0x0312, WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021, WM_TIMER = 0x0113;
    public const nint HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
    public const int SW_SHOWNOACTIVATE = 4, SW_HIDE = 0;
    public const uint ULW_ALPHA = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    public const uint DI_NORMAL = 3;
    public const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public uint CbSize; public RECT Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint BiSize;
        public int BiWidth, BiHeight;
        public ushort BiPlanes, BiBitCount;
        public uint BiCompression, BiSizeImage;
        public int BiXPelsPerMeter, BiYPelsPerMeter;
        public uint BiClrUsed, BiClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFOW
    {
        public nint HIcon;
        public int IIcon;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [DllImport("user32.dll")] public static extern int UpdateLayeredWindow(nint hwnd, nint hdcDst, POINT* pptDst, SIZE* size, nint hdcSrc, POINT* pptSrc, uint crKey, BLENDFUNCTION* blend, uint flags);
    [DllImport("user32.dll")] public static extern int ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern int RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern int UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern int GetMonitorInfoW(nint monitor, MONITORINFO* info);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("user32.dll")] public static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] public static extern int SystemParametersInfoW(uint action, uint param, int* value, uint winIni);
    [DllImport("user32.dll")] public static extern int DrawIconEx(nint hdc, int x, int y, nint icon, int cx, int cy, uint step, nint brush, uint flags);
    [DllImport("user32.dll")] public static extern int GetCursorPos(POINT* pt);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint monitor, int type, uint* dpiX, uint* dpiY);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* bmi, uint usage, void** bits, nint section, uint offset);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] public static extern int DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern int DeleteDC(nint hdc);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern nint SHGetFileInfoW(char* path, uint attrs, SHFILEINFOW* info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHDefExtractIconW(char* file, int index, uint flags, nint* large, nint* small, uint size);
    [DllImport("user32.dll")] public static extern int DestroyIcon(nint icon);

    /// <summary>Makes the current thread per-monitor DPI aware (v2) until disposed. The process itself stays DPI-unaware, so the hook and gesture maths keep their old units.</summary>
    public readonly struct DpiScope : IDisposable
    {
        private readonly nint _previous;

        public DpiScope() => _previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        public void Dispose()
        {
            if (_previous != 0) SetThreadDpiAwarenessContext(_previous);
        }
    }

    /// <summary>True when "Show animations in Windows" is on (the reduced-motion switch is off).</summary>
    public static bool ClientAreaAnimationEnabled()
    {
        int on = 1;
        return SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, &on, 0) == 0 || on != 0;
    }
}
