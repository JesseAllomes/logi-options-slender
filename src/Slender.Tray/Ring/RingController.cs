using System.Runtime.InteropServices;
using Slender.Engine;
using Slender.Engine.Ring;
using Slender.Tray.Native;

namespace Slender.Tray.Ring;

/// <summary>
/// Opens, drives and closes the Actions Ring overlay on the tray's UI thread. The <see cref="RingSession"/> holds all the
/// behaviour; this class owns what needs windows: the layered no-activate window, the Direct2D canvas, the hotkeys and the
/// mouse-hook capture, all created when the ring opens and released when it closes (nothing stays captured). Every entry
/// point must be called on the UI thread (post from the engine and hook threads).
/// </summary>
public sealed unsafe class RingController : IDisposable
{
    private const string WindowClass = "Slender.Ring.Window";
    private const nuint TimerId = 1;
    private const uint TimerMs = 16, FadeMs = 120;

    // (hotkey id, virtual key, ring key): registered only while the ring is open so the keys never reach the app below.
    private static readonly (int Id, uint Vk, RingKey Key)[] Hotkeys =
    [
        (1, 0x1B, RingKey.Escape), (2, 0x26, RingKey.Up), (3, 0x28, RingKey.Down), (4, 0x25, RingKey.Left),
        (5, 0x27, RingKey.Right), (6, 0x0D, RingKey.Enter), (7, 0x08, RingKey.Back),
    ];

    private static RingController? s_current; // the window procedure is a static callback; one ring at a time
    private static bool s_classRegistered;

    private readonly Func<SlenderConfig> _config;
    private readonly IForegroundApp _foreground;
    private readonly IInputHook _hook;
    private readonly Action<ActionSpec> _run;
    private readonly Action<RingHaptic> _haptic;
    private readonly Action<Action> _post;
    private readonly ILog _log;

    private RingSession? _session;
    private RingCanvas? _canvas;
    private RingIconCache? _icons;
    private RingAppearance _appearance = new();
    private RingMetrics _metrics = RingLayout.Metrics(RingStyle.Bezel, RingSize.Medium);
    private nint _hwnd, _foregroundAtOpen;
    private int _centreX, _centreY, _left, _top, _iconPx;
    private double _scale = 1;
    private byte _alpha = 255;
    private long _fadeStart; // 0 = no fade
    private bool _disposed;

    /// <param name="config">The live config, read each time the ring opens.</param>
    /// <param name="post">Runs an action on the UI thread; used by the hook callback (hook thread).</param>
    public RingController(Func<SlenderConfig> config, IForegroundApp foreground, IInputHook hook,
        Action<ActionSpec> run, Action<RingHaptic> haptic, Action<Action> post, ILog? log = null)
    {
        _config = config;
        _foreground = foreground;
        _hook = hook;
        _run = run;
        _haptic = haptic;
        _post = post;
        _log = log ?? NullLog.Instance;
    }

    public bool IsOpen => _session?.IsOpen == true;

    /// <summary>The ring trigger went down or up. A press while closed opens the ring; while open it goes to the state machine.</summary>
    public void OnTrigger(bool pressed, bool holdOnly = false)
    {
        if (_disposed) return;
        using var dpi = new RingNative.DpiScope();
        try
        {
            if (_session is { IsOpen: true } open)
            {
                open.OnTrigger(pressed, Now);
                return;
            }
            if (pressed) Open(holdOnly);
        }
        catch (Exception ex)
        {
            _log.Error("actions ring failed", ex);
            Teardown();
        }
    }

    /// <summary>Closes the ring from outside (device disconnected, shutdown). No-op when closed.</summary>
    public void Close(RingCloseReason reason)
    {
        try { _session?.Close(reason); }
        catch (Exception ex) { _log.Error("actions ring close failed", ex); Teardown(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close(RingCloseReason.Shutdown);
        Teardown();
        _disposed = true;
    }

    private static long Now => Environment.TickCount64;

    // ---- open ----

    private void Open(bool holdOnly)
    {
        var cfg = _config();
        _appearance = cfg.Ring;
        var source = RingSource.Resolve(cfg, _foreground.ProcessName);
        _metrics = RingLayout.Metrics(_appearance.Style, _appearance.Size);

        RingNative.POINT cursor;
        RingNative.GetCursorPos(&cursor);
        var monitor = RingNative.MonitorFromPoint(cursor, RingNative.MONITOR_DEFAULTTONEAREST);
        var info = new RingNative.MONITORINFO { CbSize = (uint)sizeof(RingNative.MONITORINFO) };
        RingNative.GetMonitorInfoW(monitor, &info);
        uint dpiX = 96, dpiY = 96;
        if (RingNative.GetDpiForMonitor(monitor, RingNative.MDT_EFFECTIVE_DPI, &dpiX, &dpiY) < 0 || dpiX == 0) dpiX = 96;
        _scale = dpiX / 96.0;

        (_centreX, _centreY) = RingLayout.Place(_appearance.Position, cursor.X, cursor.Y,
            info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, _metrics, _scale);
        var extentPx = (int)Math.Ceiling(_metrics.Extent * _scale);
        _left = _centreX - extentPx;
        _top = _centreY - extentPx;
        _iconPx = Math.Max(16, (int)Math.Round(26 * _metrics.Scale * _scale));
        _alpha = (byte)Math.Round(Math.Clamp(_appearance.OpacityPercent, 10, 100) * 255 / 100.0);

        var session = new RingSession(source.Slots, _metrics, Now, holdOnly);
        _session = session;
        session.Changed += OnSessionChanged;
        session.Haptic += (_, kind) => _haptic(kind);
        session.Closed += OnSessionClosed;
        session.ActionRequested += (_, action) => _run(action); // after Closed: the window is gone before the action runs
        session.Adjust += (_, action) => _run(action);
        s_current = this;

        _foregroundAtOpen = RingNative.GetForegroundWindow();
        _canvas = new RingCanvas(extentPx * 2, extentPx * 2, _scale, Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts"));
        _icons = new RingIconCache(_canvas);
        CreateWindow();
        Render(initial: true);
        RingNative.ShowWindow(_hwnd, RingNative.SW_SHOWNOACTIVATE);

        foreach (var (id, vk, _) in Hotkeys)
            if (RingNative.RegisterHotKey(_hwnd, id, 0, vk) == 0) _log.Info($"ring: hotkey {id} unavailable (taken by another app)");
        _hook.RingInput += OnHookInput;
        _hook.SetRingCapture(true);
        Win32.SetTimer(_hwnd, TimerId, TimerMs, 0);
        SamplePointer(); // baseline selection (the ring opens under the cursor, or at the monitor centre)
    }

    private void CreateWindow()
    {
        var instance = Win32.GetModuleHandleW(null);
        fixed (char* cls = WindowClass)
        {
            if (!s_classRegistered)
            {
                var wc = new Win32.WNDCLASSW { lpfnWndProc = &RingWndProc, hInstance = instance, lpszClassName = cls };
                Win32.RegisterClassW(&wc);
                s_classRegistered = true;
            }
            _hwnd = Win32.CreateWindowExW(
                RingNative.WS_EX_LAYERED | RingNative.WS_EX_TOPMOST | RingNative.WS_EX_TOOLWINDOW | RingNative.WS_EX_NOACTIVATE | RingNative.WS_EX_TRANSPARENT,
                cls, null, RingNative.WS_POPUP, _left, _top, 1, 1, 0, 0, instance, null);
        }
        if (_hwnd == 0) throw new InvalidOperationException("Could not create the ring window.");
    }

    // ---- drawing ----

    private void Render(bool initial = false)
    {
        if (_canvas is null || _session is null || _hwnd == 0) return;
        var cells = RingRenderer.Cells(_session.Slots, _icons, _iconPx);
        var accent = RingColor.TryParse(_appearance.Accent) ?? RingColor.Rgb(0xD9925F);
        RingRenderer.Draw(_canvas, new RingFrame(_appearance.Style, _metrics, accent, _appearance.Labels, cells, _session.Selected, _session.FolderName));
        if (initial && RingNative.ClientAreaAnimationEnabled())
        {
            _fadeStart = Now; // a short fade in; with Windows animations off the ring appears at once
            _canvas.Present(_hwnd, _left, _top, 0);
        }
        else _canvas.Present(_hwnd, _left, _top, CurrentAlpha());
    }

    private byte CurrentAlpha()
    {
        if (_fadeStart == 0) return _alpha;
        var t = Now - _fadeStart;
        if (t >= FadeMs) { _fadeStart = 0; return _alpha; }
        return (byte)(_alpha * t / FadeMs);
    }

    // ---- events ----

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        try { Render(); }
        catch (Exception ex) { _log.Error("ring draw failed", ex); }
    }

    private void OnSessionClosed(object? sender, RingCloseReason reason) => Teardown();

    /// <summary>Hook thread: hand the consumed input to the UI thread.</summary>
    private void OnHookInput(object? sender, HookRingEventArgs e) => _post(() => HandleInput(e.Kind, e.Delta));

    private void HandleInput(HookRingKind kind, int delta)
    {
        if (_session is not { IsOpen: true } session) return;
        using var dpi = new RingNative.DpiScope();
        try
        {
            RingNative.POINT p;
            RingNative.GetCursorPos(&p);
            double dx = (p.X - _centreX) / _scale, dy = (p.Y - _centreY) / _scale;
            switch (kind)
            {
                case HookRingKind.LeftDown: session.OnClick(true, dx, dy, Now); break;
                case HookRingKind.RightDown: session.OnClick(false, dx, dy, Now); break;
                case HookRingKind.Wheel:
                    session.OnPointer(dx, dy, Now);
                    session.OnWheel(delta, Now);
                    break;
                case HookRingKind.ThumbWheel:
                    session.OnPointer(dx, dy, Now);
                    session.OnThumbWheel(delta, Now);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error("ring input failed", ex);
            Teardown();
        }
    }

    private void SamplePointer()
    {
        if (_session is not { IsOpen: true } session) return;
        RingNative.POINT p;
        RingNative.GetCursorPos(&p);
        session.OnPointer((p.X - _centreX) / _scale, (p.Y - _centreY) / _scale, Now);
    }

    private void OnTimer()
    {
        if (_session is not { IsOpen: true } session) return;
        try
        {
            if (RingNative.GetForegroundWindow() != _foregroundAtOpen) { session.Close(RingCloseReason.FocusLost); return; }
            SamplePointer();
            if (!session.IsOpen) return;
            session.Tick(Now);
            if (session.IsOpen && _fadeStart != 0 && _canvas is not null) _canvas.Present(_hwnd, _left, _top, CurrentAlpha());
        }
        catch (Exception ex)
        {
            _log.Error("ring timer failed", ex);
            Teardown();
        }
    }

    private void OnHotkey(int id)
    {
        if (_session is not { IsOpen: true } session) return;
        foreach (var (hid, _, key) in Hotkeys)
        {
            if (hid != id) continue;
            try { session.OnKey(key, Now); }
            catch (Exception ex) { _log.Error("ring key failed", ex); Teardown(); }
            return;
        }
    }

    // ---- close ----

    /// <summary>Releases everything the open ring held: mouse capture, hotkeys, timer, window, canvas. Safe to call twice.</summary>
    private void Teardown()
    {
        try { _hook.SetRingCapture(false); } catch { }
        _hook.RingInput -= OnHookInput;
        var hwnd = _hwnd;
        _hwnd = 0;
        if (hwnd != 0)
        {
            Win32.KillTimer(hwnd, TimerId);
            foreach (var (id, _, _) in Hotkeys) RingNative.UnregisterHotKey(hwnd, id);
            RingNative.ShowWindow(hwnd, RingNative.SW_HIDE);
            Win32.DestroyWindow(hwnd);
        }
        _icons?.Dispose();
        _icons = null;
        _canvas?.Dispose();
        _canvas = null;
        _session = null;
        _fadeStart = 0;
        if (ReferenceEquals(s_current, this)) s_current = null;
    }

    [UnmanagedCallersOnly]
    private static nint RingWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            var ring = s_current;
            switch (msg)
            {
                case RingNative.WM_NCHITTEST:
                    return RingNative.HTTRANSPARENT;
                case RingNative.WM_MOUSEACTIVATE:
                    return RingNative.MA_NOACTIVATE;
                case RingNative.WM_HOTKEY:
                    ring?.OnHotkey((int)wParam);
                    return 0;
                case RingNative.WM_TIMER:
                    ring?.OnTimer();
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
