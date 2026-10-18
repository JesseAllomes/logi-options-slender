using System.Runtime.InteropServices;
using Slender.Engine;
using Slender.Tray.Native;

namespace Slender.Tray.Ring;

/// <summary>
/// A short label near the cursor (lock keys): a small layered, click-through, non-activating window drawn with
/// the ring's Direct2D canvas. Created on each show, destroyed when its timer fires (nothing stays allocated). UI thread only.
/// </summary>
public sealed unsafe class ThumbWheelToast : IDisposable
{
    private const string WindowClass = "Slender.Toast.Window";
    private const nuint TimerId = 1;
    private const uint ShowMs = 900;
    private const double HeightDip = 40, MinWidthDip = 96, MaxWidthDip = 360, FontDip = 14;

    private static ThumbWheelToast? s_current;
    private static bool s_classRegistered;

    private readonly Func<SlenderConfig> _config;
    private readonly Func<bool> _ringOpen;
    private readonly ILog _log;
    private RingCanvas? _canvas;
    private nint _hwnd;
    private bool _disposed;

    public ThumbWheelToast(Func<SlenderConfig> config, Func<bool> ringOpen, ILog? log = null)
    {
        _config = config;
        _ringOpen = ringOpen;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Shows <paramref name="text"/> near the cursor for about 900 ms, replacing any toast still up. Skipped while the ring is open.</summary>
    public void Show(string text)
    {
        if (_disposed || text.Length == 0 || _ringOpen()) return;
        using var dpi = new RingNative.DpiScope();
        try
        {
            Hide();
            RingNative.POINT cursor;
            RingNative.GetCursorPos(&cursor);
            var monitor = RingNative.MonitorFromPoint(cursor, RingNative.MONITOR_DEFAULTTONEAREST);
            var info = new RingNative.MONITORINFO { CbSize = (uint)sizeof(RingNative.MONITORINFO) };
            RingNative.GetMonitorInfoW(monitor, &info);
            uint dpiX = 96, dpiY = 96;
            if (RingNative.GetDpiForMonitor(monitor, RingNative.MDT_EFFECTIVE_DPI, &dpiX, &dpiY) < 0 || dpiX == 0) dpiX = 96;
            var scale = dpiX / 96.0;

            var widthDip = Math.Clamp(text.Length * 8.6 + 32, MinWidthDip, MaxWidthDip);
            int w = (int)Math.Ceiling(widthDip * scale), h = (int)Math.Ceiling(HeightDip * scale);
            var x = Math.Clamp(cursor.X + (int)(18 * scale), info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - w));
            var y = Math.Clamp(cursor.Y + (int)(22 * scale), info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - h));

            var accent = RingColor.TryParse(_config().Ring.Accent) ?? RingColor.Rgb(0xD9925F);
            _canvas = new RingCanvas(w, h, scale, Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts"));
            _canvas.Begin();
            _canvas.Clear();
            _canvas.FillRoundRect(1, 1, widthDip - 2, HeightDip - 2, 10, RingColor.Rgb(0x1B1F23, .94f));
            _canvas.StrokeRoundRect(1, 1, widthDip - 2, HeightDip - 2, 10, accent, 1.5);
            _canvas.Text(RingSlotText.Fit(text, 40), RingFont.Label, FontDip, widthDip / 2, HeightDip / 2, RingColor.Rgb(0xECEFF1), 0, widthDip - 20);
            _canvas.End();

            CreateWindow(x, y);
            s_current = this;
            _canvas.Present(_hwnd, x, y, 255);
            RingNative.ShowWindow(_hwnd, RingNative.SW_SHOWNOACTIVATE);
            Win32.SetTimer(_hwnd, TimerId, ShowMs, 0);
        }
        catch (Exception ex)
        {
            _log.Error("thumb wheel label failed", ex);
            Hide();
        }
    }

    private void CreateWindow(int x, int y)
    {
        var instance = Win32.GetModuleHandleW(null);
        fixed (char* cls = WindowClass)
        {
            if (!s_classRegistered)
            {
                var wc = new Win32.WNDCLASSW { lpfnWndProc = &ToastWndProc, hInstance = instance, lpszClassName = cls };
                Win32.RegisterClassW(&wc);
                s_classRegistered = true;
            }
            _hwnd = Win32.CreateWindowExW(
                RingNative.WS_EX_LAYERED | RingNative.WS_EX_TOPMOST | RingNative.WS_EX_TOOLWINDOW | RingNative.WS_EX_NOACTIVATE | RingNative.WS_EX_TRANSPARENT,
                cls, null, RingNative.WS_POPUP, x, y, 1, 1, 0, 0, instance, null);
        }
        if (_hwnd == 0) throw new InvalidOperationException("Could not create the thumb wheel label window.");
    }

    /// <summary>Hides and releases the window and canvas. Safe to call when nothing is showing.</summary>
    private void Hide()
    {
        var hwnd = _hwnd;
        _hwnd = 0;
        if (hwnd != 0)
        {
            Win32.KillTimer(hwnd, TimerId);
            RingNative.ShowWindow(hwnd, RingNative.SW_HIDE);
            Win32.DestroyWindow(hwnd);
        }
        _canvas?.Dispose();
        _canvas = null;
        if (ReferenceEquals(s_current, this)) s_current = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide();
        _disposed = true;
    }

    [UnmanagedCallersOnly]
    private static nint ToastWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case RingNative.WM_NCHITTEST:
                    return RingNative.HTTRANSPARENT;
                case RingNative.WM_MOUSEACTIVATE:
                    return RingNative.MA_NOACTIVATE;
                case RingNative.WM_TIMER:
                    s_current?.Hide();
                    return 0;
            }
        }
        catch
        {
            // An exception must never cross an unmanaged boundary.
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
