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
    private volatile bool _clickWatch;
    private readonly RingButtonPairing _ringPairing = new(); // hook thread only: a swallowed press swallows its release
    private int _anchorX, _anchorY;
    private bool _disposed;

    public LowLevelMouseHook(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _proc = HookProc;
    }

    public event EventHandler<HookWheelEventArgs>? HorizontalWheel;

    public event EventHandler<HookWheelEventArgs>? VerticalWheel;

    public event EventHandler<HookButtonEventArgs>? ButtonChanged;

    public event EventHandler<HookMoveEventArgs>? MouseMoved;

    public event EventHandler<HookRingEventArgs>? RingInput;

    public event EventHandler? AnyButtonPressed;

    public void SetClickWatch(bool on) => _clickWatch = on;

    /// <summary>
    /// While on, left/right button presses and both wheels are swallowed and reported through <see cref="RingInput"/>
    /// (the Actions Ring consumes them). A swallowed press swallows its release, even after capture ended, unless the release
    /// comes more than 5 s later (then it belongs to another click).
    /// </summary>
    public void SetRingCapture(bool on)
    {
        _ringCapture = on;
    }

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

    // Recent pointer positions (the user's own moves only) for PointerTravel: a fixed ring, written on the hook thread in O(1).
    private const int MoveRing = 1024; // over a second of moves even at a 1000 Hz report rate
    private readonly long[] _moveAt = new long[MoveRing];
    private readonly int[] _moveX = new int[MoveRing], _moveY = new int[MoveRing];
    private int _moveNext;
    private readonly object _moveGate = new();

    private void RecordMove(nint lParam)
    {
        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        if (IsInjected(data)) return;
        lock (_moveGate)
        {
            int i = _moveNext;
            _moveAt[i] = Environment.TickCount64;
            _moveX[i] = data.Pt.X;
            _moveY[i] = data.Pt.Y;
            _moveNext = (i + 1) % MoveRing;
        }
    }

    public int PointerTravel(int windowMs)
    {
        if (!NativeMethods.GetCursorPos(out var now)) return 0;
        long since = Environment.TickCount64 - windowMs;
        int travel = 0;
        lock (_moveGate)
        {
            for (int k = 0; k < MoveRing; k++)
            {
                if (_moveAt[k] < since || _moveAt[k] == 0) continue;
                travel = Math.Max(travel, Math.Max(Math.Abs(_moveX[k] - now.X), Math.Abs(_moveY[k] - now.Y)));
            }
        }
        return travel;
    }

    /// <summary>A hook call slower than this stalls every mouse event on the PC; the first one is logged.</summary>
    internal const long SlowHookMs = 100;
    private int _slowReported;

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return HookCore(nCode, wParam, lParam);
        }
        finally
        {
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (ms > SlowHookMs) OnSlowHook(ms, (int)wParam);
        }
    }

    /// <summary>The hook took too long: Windows holds every mouse event on the PC while it runs. Logged once, off the hook thread.</summary>
    private void OnSlowHook(double ms, int msg)
    {
        if (Interlocked.Exchange(ref _slowReported, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { _log.Error($"mouse hook took {ms:F0} ms on message 0x{msg:X4}"); }
            catch { }
        });
    }

    private nint HookCore(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                if (_clickWatch && msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_XBUTTONDOWN)
                    NoteClick(lParam);
                if (msg == NativeMethods.WM_MOUSEMOVE)
                {
                    RecordMove(lParam);
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
                    if (msg == NativeMethods.WM_MOUSEWHEEL && !IsInjected(data) && VerticalWheel is { } vertical)
                    {
                        // Per-app main wheel speed: the engine may swallow this turn and send a scaled one (O(1), never throws).
                        var args = new HookWheelEventArgs((short)(data.MouseData >> 16));
                        vertical(this, args);
                        if (args.Handled) return 1;
                    }
                }
                else if (msg == NativeMethods.WM_MOUSEHWHEEL)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if (!IsInjected(data) && RingMouse(msg, data)) return 1; // ring capture takes the thumb wheel first
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

    /// <summary>Tells the click watch (the 3CX ringer) that the user pressed a button. O(1); the press itself is untouched.</summary>
    private void NoteClick(nint lParam)
    {
        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        if (IsInjected(data)) return;
        try { AnyButtonPressed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { try { _log.Error("click watch handler failed", ex); } catch { } }
    }

    /// <summary>Ring capture for left/right/wheel. Returns true to swallow.</summary>
    private bool RingMouse(int msg, NativeMethods.MSLLHOOKSTRUCT data)
    {
        var decision = _ringPairing.OnMessage(msg, data.Time, _ringCapture);
        if (decision.Kind is not { } kind) return decision.Swallow;
        var delta = kind is HookRingKind.Wheel or HookRingKind.ThumbWheel ? (short)(data.MouseData >> 16) : 0;
        try { RingInput?.Invoke(this, new HookRingEventArgs(kind, delta)); }
        catch (Exception ex) { try { _log.Error("ring input handler failed", ex); } catch { } }
        return decision.Swallow;
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
        VerticalWheel = null;
        ButtonChanged = null;
        MouseMoved = null;
        RingInput = null;
        AnyButtonPressed = null;
        _clickWatch = false;
        _captureMoves = false;
        _ringCapture = false;
        if (t is null) return;
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        t.Join(1000); // the thread unhooks itself on exit
    }
}
