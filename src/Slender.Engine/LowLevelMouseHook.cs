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
    private bool _disposed;

    public LowLevelMouseHook(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _proc = HookProc;
    }

    public event EventHandler<HookWheelEventArgs>? HorizontalWheel;

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
            if (nCode >= 0 && wParam == NativeMethods.WM_MOUSEHWHEEL)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var injected = (data.Flags & (NativeMethods.LLMHF_INJECTED | NativeMethods.LLMHF_LOWER_IL_INJECTED)) != 0;
                if (!injected)
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
        }
        catch (Exception ex)
        {
            // Never throw across the hook; fall through to the next hook.
            try { _log.Error("mouse hook handler failed", ex); } catch { }
        }
        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
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
        if (t is null) return;
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        t.Join(1000); // the thread unhooks itself on exit
    }
}
