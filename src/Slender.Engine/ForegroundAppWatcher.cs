using Slender.Engine.Native;

namespace Slender.Engine;

/// <summary>
/// EVENT_SYSTEM_FOREGROUND via SetWinEventHook (out-of-context, so nothing is injected into other
/// processes) on a message-loop thread. Starts in the constructor.
/// </summary>
public sealed class ForegroundAppWatcher : IForegroundApp, IDisposable
{
    private readonly ILog _log;
    private readonly NativeMethods.WinEventProc _proc;
    private readonly Thread _thread;
    private uint _threadId;
    private nint _hook;
    private volatile string? _processName;
    private nint _window;
    private bool _disposed;

    public ForegroundAppWatcher(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _proc = OnForeground;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Slender.ForegroundWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(2000);
    }

    public string? ProcessName => _processName;
    public nint WindowHandle => Volatile.Read(ref _window);
    public event EventHandler<string?>? Changed;

    private void Loop(ManualResetEventSlim ready)
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                0, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (_hook == 0) _log.Error("SetWinEventHook failed");
            Update(NativeMethods.GetForegroundWindow(), raise: false);
        }
        catch (Exception ex) { _log.Error("foreground watcher init failed", ex); }
        ready.Set();
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
            if (_hook != 0) NativeMethods.UnhookWinEvent(_hook);
            _hook = 0;
        }
    }

    private void OnForeground(nint hHook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        try { Update(hwnd, raise: true); }
        catch (Exception ex) { try { _log.Error("foreground event failed", ex); } catch { } }
    }

    private void Update(nint hwnd, bool raise)
    {
        if (hwnd == 0) return; // transient "no foreground" while windows switch
        var name = NativeMethods.GetProcessName(hwnd);
        Volatile.Write(ref _window, hwnd);
        var changed = !string.Equals(_processName, name, StringComparison.Ordinal);
        _processName = name;
        if (raise && changed) Changed?.Invoke(this, name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Changed = null;
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        _thread.Join(1000);
    }
}
