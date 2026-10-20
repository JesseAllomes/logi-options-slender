using System.Runtime.InteropServices;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// Notification haptics (m45). Windows toasts raise an accessibility alert event (EVENT_SYSTEM_ALERT, the one screen readers
/// use) from the shell's toast window; one out-of-context WinEvent hook on the tray's message-loop thread hears it. The hook
/// callback only filters and queues; the app name is read on a thread-pool thread so a slow shell never stalls the tray.
/// The hook exists only while the feature is on: <see cref="SetEnabled"/>(false) removes it and nothing else of this class
/// is alive (no thread, timer or COM load). Call SetEnabled on the UI thread only.
/// </summary>
internal sealed unsafe class NotifyListener : IDisposable
{
    private const uint EVENT_SYSTEM_ALERT = 0x0002, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    private const string ToastWindowClass = "Windows.UI.Core.CoreWindow";

    private static NotifyListener? s_current;
    private readonly SlenderEngine _engine;
    private readonly SeenAppsStore _seen;
    private readonly ILog _log;
    private nint _hook;
    private long _lastAccepted;

    public NotifyListener(SlenderEngine engine, SeenAppsStore seen, ILog? log = null)
    {
        _engine = engine;
        _seen = seen;
        _log = log ?? NullLog.Instance;
    }

    public bool IsEnabled => _hook != 0;

    public void SetEnabled(bool on)
    {
        if (on == IsEnabled) return;
        if (on)
        {
            s_current = this;
            _hook = SetWinEventHook(EVENT_SYSTEM_ALERT, EVENT_SYSTEM_ALERT, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (_hook == 0) { s_current = null; _log.Info("notification haptics: hook failed"); }
        }
        else
        {
            UnhookWinEvent(_hook);
            _hook = 0;
            s_current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { s_current?.Heard(hwnd); } catch { }
    }

    private void Heard(nint hwnd)
    {
        if (hwnd == 0 || !IsToastWindow(hwnd)) return;
        long now = Environment.TickCount64;
        if (now - _lastAccepted < 750) return; // one toast raises several alerts
        _lastAccepted = now;
        ThreadPool.UnsafeQueueUserWorkItem(_ => Handle(hwnd), null);
    }

    private void Handle(nint hwnd)
    {
        string app;
        try { app = ToastNames.AppFromAccessibleName(ReadAccessibleName(hwnd)); }
        catch (Exception ex) { _log.Error("notification name read failed", ex); app = ToastNames.Unknown; }
        _seen.Record(app, DateTimeOffset.UtcNow);
        _engine.PlayNotification(app);
    }

    private static bool IsToastWindow(nint hwnd)
    {
        char* buffer = stackalloc char[64];
        int n = GetClassNameW(hwnd, buffer, 64);
        return n == ToastWindowClass.Length && new ReadOnlySpan<char>(buffer, n).SequenceEqual(ToastWindowClass);
    }

    /// <summary>The window's MSAA name through IAccessible::get_accName (vtable slot 10); null when it cannot be read.</summary>
    private static string? ReadAccessibleName(nint hwnd)
    {
        int init = CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */);
        try
        {
            Guid iid = new(0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
            nint acc;
            if (AccessibleObjectFromWindow(hwnd, 0xFFFFFFFC /* OBJID_CLIENT */, &iid, &acc) < 0 || acc == 0) return null;
            try
            {
                var vtbl = *(nint**)acc;
                Variant self = default;
                self.vt = 3; // VT_I4
                self.value = 0; // CHILDID_SELF
                nint bstr = 0;
                var getName = (delegate* unmanaged[Stdcall]<nint, Variant, nint*, int>)vtbl[10];
                if (getName(acc, self, &bstr) < 0 || bstr == 0) return null;
                try { return Marshal.PtrToStringBSTR(bstr); }
                finally { SysFreeString(bstr); }
            }
            finally { ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)acc)[2])(acc); } // Release
        }
        finally { if (init >= 0) CoUninitialize(); }
    }

    public void Dispose() => SetEnabled(false);

    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort vt, r1, r2, r3;
        public long value, pad;
    }

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint mod, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern int UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern int GetClassNameW(nint hwnd, char* name, int max);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint hwnd, uint objectId, Guid* iid, nint* result);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("oleaut32.dll")] private static extern void SysFreeString(nint bstr);
}
