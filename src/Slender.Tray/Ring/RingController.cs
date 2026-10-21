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
    private const nuint TimerId = 1, AnimTimerId = 2; // 1: pointer and focus polling while open; 2: frames of the closing animation
    private const uint TimerMs = 16;

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
    private bool _disposed;

    // Opening/closing animation: frames are driven by the 16 ms timers only while _animating, and drawn from the cached _frame.
    private readonly RingPose _pose = new();
    private RingFrame? _frame;
    private RingOpening _motion;
    private bool _animating, _closing, _inputHeld;
    private long _animStart;
    private double _animMs;

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

    /// <summary>Raised (UI thread) when the ring opens (true) or is torn down (false); the engine diverts the thumb wheel while it is open.</summary>
    public event Action<bool>? OpenChanged;

    private bool _announcedOpen;

    private void AnnounceOpen(bool open)
    {
        if (_announcedOpen == open) return;
        _announcedOpen = open;
        // While open the ring holds Esc and the arrow keys system-wide (RegisterHotKey); the line shows when they were taken and given back.
        _log.Info(open ? "actions ring open: arrow keys and Esc held" : "actions ring closed: arrow keys and Esc released");
        try { OpenChanged?.Invoke(open); }
        catch (Exception ex) { _log.Error("ring open handler failed", ex); }
    }

    /// <summary>Thumb wheel rotation from HID++ (the wheel is diverted, so the hook sees none). Any thread; 1/120 notch units.</summary>
    public void OnDivertedThumbWheel(int delta)
    {
        if (_disposed) return;
        _post(() => HandleInput(HookRingKind.ThumbWheel, delta));
    }

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
        ReleaseWindow(); // a closing animation still on screen is cancelled; the open starts from scratch
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
        StartAnimation(closing: false);
        Render();
        RingNative.ShowWindow(_hwnd, RingNative.SW_SHOWNOACTIVATE);

        foreach (var (id, vk, _) in Hotkeys)
            if (RingNative.RegisterHotKey(_hwnd, id, 0, vk) == 0) _log.Info($"ring: hotkey {id} unavailable (taken by another app)");
        _inputHeld = true;
        _hook.RingInput += OnHookInput;
        _hook.SetRingCapture(true);
        Win32.SetTimer(_hwnd, TimerId, TimerMs, 0);
        SamplePointer(); // baseline selection (the ring opens under the cursor, or at the monitor centre)
        AnnounceOpen(true);
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

    private void Render()
    {
        if (_canvas is null || _session is null || _hwnd == 0) return;
        var cells = RingRenderer.Cells(_session.Slots, _icons, _iconPx);
        var accent = RingColor.TryParse(_appearance.Accent) ?? RingColor.Rgb(0xD9925F);
        _frame = new RingFrame(_appearance.Style, _metrics, accent, _appearance.Labels, cells, _session.Selected, _session.FolderName);
        DrawFrame();
    }

    /// <summary>Draws the cached frame at the animation's current progress and presents it. Allocates nothing.</summary>
    private void DrawFrame()
    {
        if (_canvas is null || _frame is null || _hwnd == 0) return;
        RingPose? pose = null;
        if (_animating)
        {
            var t = RingMotion.Progress(Now - _animStart, _animMs);
            RingMotion.Evaluate(_motion, _closing, t, _metrics.Scale, _pose);
            if (t < 1 || _closing) pose = _pose;
            else _animating = false; // opened: this last frame is the settled ring, then the frames stop
        }
        RingRenderer.Draw(_canvas, _frame, pose);
        _canvas.Present(_hwnd, _left, _top, _alpha);
    }

    /// <summary>Starts the open or close animation; Off and Windows' "show animations" switched off both mean a 90 ms fade.</summary>
    private void StartAnimation(bool closing)
    {
        _motion = RingMotion.Effective(_appearance.Opening, RingNative.ClientAreaAnimationEnabled());
        _closing = closing;
        _animMs = closing ? RingMotion.CloseMs(_motion, _appearance.AnimationSpeed) : RingMotion.OpenMs(_motion, _appearance.AnimationSpeed);
        _animStart = Now;
        _animating = true;
    }

    // ---- events ----

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        try { Render(); }
        catch (Exception ex) { _log.Error("ring draw failed", ex); }
    }

    /// <summary>
    /// The session is over: input, hotkeys and capture are released at once (and the chosen action runs right after this
    /// returns), while the window stays up for the closing animation, which is drawn from the last frame.
    /// </summary>
    private void OnSessionClosed(object? sender, RingCloseReason reason)
    {
        if (reason == RingCloseReason.Shutdown || _canvas is null || _frame is null || _hwnd == 0)
        {
            Teardown();
            return;
        }
        try
        {
            ReleaseInput();
            StartAnimation(closing: true);
            Win32.SetTimer(_hwnd, AnimTimerId, TimerMs, 0);
            DrawFrame();
        }
        catch (Exception ex)
        {
            _log.Error("ring close animation failed", ex);
            Teardown();
        }
    }

    private void OnAnimTimer()
    {
        try
        {
            if (_animating && Now - _animStart < _animMs) DrawFrame();
            else ReleaseWindow();
        }
        catch (Exception ex)
        {
            _log.Error("ring close animation failed", ex);
            Teardown();
        }
    }

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
            if (session.IsOpen && _animating) DrawFrame();
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
        ReleaseInput();
        ReleaseWindow();
    }

    /// <summary>Gives back the mouse capture, hotkeys and polling timer, and tells the engine the ring is closed. The window may live on.</summary>
    private void ReleaseInput()
    {
        try { _hook.SetRingCapture(false); } catch { }
        _hook.RingInput -= OnHookInput;
        if (_hwnd != 0 && _inputHeld)
        {
            Win32.KillTimer(_hwnd, TimerId);
            foreach (var (id, _, _) in Hotkeys) RingNative.UnregisterHotKey(_hwnd, id);
        }
        _inputHeld = false;
        _session = null;
        AnnounceOpen(false);
    }

    /// <summary>Destroys the window, canvas and icons (also ends a closing animation).</summary>
    private void ReleaseWindow()
    {
        var hwnd = _hwnd;
        _hwnd = 0;
        if (hwnd != 0)
        {
            Win32.KillTimer(hwnd, AnimTimerId);
            RingNative.ShowWindow(hwnd, RingNative.SW_HIDE);
            Win32.DestroyWindow(hwnd);
        }
        _icons?.Dispose();
        _icons = null;
        _canvas?.Dispose();
        _canvas = null;
        _frame = null;
        _animating = false;
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
                    if ((nuint)wParam == AnimTimerId) ring?.OnAnimTimer();
                    else ring?.OnTimer();
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
