using System.ComponentModel;
using System.Runtime.InteropServices;
using Slender.Engine.Native;

namespace Slender.Engine;

/// <summary>
/// WH_MOUSE_LL on a dedicated thread with its own message loop. The callback does O(1) work and never
/// throws; injected events (including our own SendInput) are ignored to avoid loops.
/// </summary>
public sealed class LowLevelMouseHook : IInputHook
{
    private readonly ILog _log;
    private readonly NativeMethods.LowLevelMouseProc _proc; // field keeps the delegate alive
    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private readonly HookButtonPairing _pairing = new();
    private volatile bool _captureMoves;
    private volatile bool _ringCapture;
    private bool _leftSwallowed, _rightSwallowed; // hook thread only: the press was swallowed, so its release must be too
    private int _anchorX, _anchorY;
    private bool _disposed;

    public LowLevelMouseHook(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _proc = HookProc;
    }

    public event EventHandler<HookWheelEventArgs>? HorizontalWheel;

    public event EventHandler<HookButtonEventArgs>? ButtonChanged;

    public event EventHandler<HookMoveEventArgs>? MouseMoved;

    public event EventHandler<HookRingEventArgs>? RingInput;

    /// <summary>
    /// While on, left/right button presses and the vertical wheel are swallowed and reported through <see cref="RingInput"/>
    /// (the Actions Ring consumes them). A swallowed press always swallows its release, even after capture ended.
    /// </summary>
    public void SetRingCapture(bool on) => _ringCapture = on;

    public void SetMoveCapture(bool on)
    {
        if (on && NativeMethods.GetCursorPos(out var pt))
        {
            _anchorX = pt.X;
            _anchorY = pt.Y;
        }
        _captureMoves = on;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null) return;
            Exception? failure = null;
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                try
                {
                    _threadId = NativeMethods.GetCurrentThreadId();
                    _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc,
                        NativeMethods.GetModuleHandle(null), 0);
                    if (_hook == 0) failure = new Win32Exception(Marshal.GetLastWin32Error());
                }
                catch (Exception ex) { failure = ex; }
                ready.Set();
                if (failure is not null) return;
                try
                {
                    while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
                    {
                        NativeMethods.TranslateMessage(ref msg);
                        NativeMethods.DispatchMessage(ref msg);
                    }
                }
                finally
                {
                    NativeMethods.UnhookWindowsHookEx(_hook);
                    _hook = 0;
                }
            }) { IsBackground = true, Name = "Slender.MouseHook" };
            _thread.Start();
            ready.Wait();
            if (failure is not null)
            {
                _thread = null;
                throw new InvalidOperationException("Could not install the low-level mouse hook.", failure);
            }
        }
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                if (msg == NativeMethods.WM_MOUSEMOVE)
                {
                    // Only while a gesture is held. Swallowed moves leave the cursor at the anchor, so each event's
                    // offset from the anchor is that event's own movement.
                    if (_captureMoves)
                    {
                        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                        var handler = MouseMoved;
                        if (!IsInjected(data) && handler is not null)
                        {
                            var args = new HookMoveEventArgs(data.Pt.X - _anchorX, data.Pt.Y - _anchorY);
                            handler(this, args);
                            if (args.Handled) return 1; // swallow
                        }
                    }
                }
                else if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP
                         or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP or NativeMethods.WM_MOUSEWHEEL)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if (!IsInjected(data) && RingMouse(msg, data)) return 1; // swallow
                }
                else if (msg == NativeMethods.WM_MOUSEHWHEEL)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if (!IsInjected(data))
                    {
                        var delta = (short)(data.MouseData >> 16);
                        var handler = HorizontalWheel;
                        if (handler is not null)
                        {
                            var args = new HookWheelEventArgs(delta);
                            handler(this, args);
                            if (args.Handled) return 1; // swallow
                        }
                    }
                }
                else if (msg is NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP
                         or NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if (!IsInjected(data) && ToButton(msg, data.MouseData) is { } button)
                    {
                        var pressed = msg is NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_XBUTTONDOWN;
                        if (_pairing.Process(button, pressed, ButtonChanged, this)) return 1; // swallow
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Never throw across the hook; fall through to the next hook.
            try { _log.Error("mouse hook handler failed", ex); } catch { }
        }
        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    /// <summary>Ring capture for left/right/wheel. Returns true to swallow.</summary>
    private bool RingMouse(int msg, NativeMethods.MSLLHOOKSTRUCT data)
    {
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONUP when _leftSwallowed:
                _leftSwallowed = false;
                return true;
            case NativeMethods.WM_RBUTTONUP when _rightSwallowed:
                _rightSwallowed = false;
                return true;
        }
        if (!_ringCapture) return false;
        HookRingKind kind;
        var delta = 0;
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN: kind = HookRingKind.LeftDown; _leftSwallowed = true; break;
            case NativeMethods.WM_RBUTTONDOWN: kind = HookRingKind.RightDown; _rightSwallowed = true; break;
            case NativeMethods.WM_MOUSEWHEEL: kind = HookRingKind.Wheel; delta = (short)(data.MouseData >> 16); break;
            default: return false; // an unpaired release passes through
        }
        try { RingInput?.Invoke(this, new HookRingEventArgs(kind, delta)); }
        catch (Exception ex) { try { _log.Error("ring input handler failed", ex); } catch { } }
        return true;
    }

    private static bool IsInjected(NativeMethods.MSLLHOOKSTRUCT d) =>
        (d.Flags & (NativeMethods.LLMHF_INJECTED | NativeMethods.LLMHF_LOWER_IL_INJECTED)) != 0;

    internal static HookButton? ToButton(int message, uint mouseData)
    {
        if (message is NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP) return HookButton.Middle;
        return (mouseData >> 16) switch { 1 => HookButton.Back, 2 => HookButton.Forward, _ => null };
    }

    public void Dispose()
    {
        Thread? t;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            t = _thread;
        }
        HorizontalWheel = null;
        ButtonChanged = null;
        MouseMoved = null;
        RingInput = null;
        _captureMoves = false;
        _ringCapture = false;
        if (t is null) return;
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        t.Join(1000); // the thread unhooks itself on exit
    }
}
