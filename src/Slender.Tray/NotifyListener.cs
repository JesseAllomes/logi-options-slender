using System.Runtime.InteropServices;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// Notification haptics (m45). Windows toasts raise an accessibility alert event (EVENT_SYSTEM_ALERT, the one screen readers
/// use) from the shell's toast window; newer builds (Windows 11 26300, 2026-10-22) raise none and only uncloak the toast window
/// (EVENT_OBJECT_UNCLOAKED), so both are heard. Out-of-context WinEvent hooks on the tray's message-loop thread hear them. The hook
/// callback only filters and queues; the app name is read on a thread-pool thread so a slow shell never stalls the tray.
/// The hook exists only while the feature is on: <see cref="SetEnabled"/>(false) removes it and nothing else of this class
/// is alive (no thread, timer or COM load). Call SetEnabled on the UI thread only.
/// </summary>
internal sealed unsafe class NotifyListener : IDisposable
{
    private const uint EVENT_SYSTEM_ALERT = 0x0002, EVENT_OBJECT_UNCLOAKED = 0x8018, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    private const string ToastWindowClass = "Windows.UI.Core.CoreWindow";
    private const string ToastHostExe = "ShellExperienceHost.exe"; // other apps use the same window class; only the shell's toasts count

    private static NotifyListener? s_current;
    private readonly SlenderEngine _engine;
    private readonly SeenAppsStore _seen;
    private readonly ILog _log;
    private nint _hook, _hookShown;
    private long _lastAccepted;
    private readonly HashSet<string> _ignoredHosts = new(StringComparer.OrdinalIgnoreCase); // logged once each

    public NotifyListener(SlenderEngine engine, SeenAppsStore seen, ILog? log = null)
    {
        _engine = engine;
        _seen = seen;
        _log = log ?? NullLog.Instance;
    }

    public bool IsEnabled => _hook != 0 || _hookShown != 0;

    public void SetEnabled(bool on)
    {
        if (on == IsEnabled) return;
        if (on)
        {
            s_current = this;
            _hook = SetWinEventHook(EVENT_SYSTEM_ALERT, EVENT_SYSTEM_ALERT, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            _hookShown = SetWinEventHook(EVENT_OBJECT_UNCLOAKED, EVENT_OBJECT_UNCLOAKED, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (_hook == 0 && _hookShown == 0) { s_current = null; _log.Info("notification haptics: hook failed"); }
        }
        else
        {
            if (_hook != 0) UnhookWinEvent(_hook);
            if (_hookShown != 0) UnhookWinEvent(_hookShown);
            _hook = 0;
            _hookShown = 0;
            s_current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { if (ev == EVENT_SYSTEM_ALERT || idObject == 0 /* OBJID_WINDOW */) s_current?.Heard(hwnd); } catch { }
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
        string? host = OwnerExeName(hwnd);
        if (!string.Equals(host, ToastHostExe, StringComparison.OrdinalIgnoreCase))
        {
            lock (_ignoredHosts) if (!_ignoredHosts.Add(host ?? "?")) return;
            _log.Info($"notification haptics: alert from {host ?? "an unknown process"} ignored (not the toast host)");
            return;
        }
        string app;
        try { app = ReadToastApp(hwnd); }
        catch (Exception ex) { _log.Error("notification name read failed", ex); app = ToastNames.Unknown; }
        _seen.Record(app, DateTimeOffset.UtcNow);
        _engine.PlayNotification(app);
    }

    /// <summary>The file name of the process that owns the window; null when it cannot be read.</summary>
    private static string? OwnerExeName(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        nint process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, 0, pid);
        if (process == 0) return null;
        try
        {
            char* path = stackalloc char[520];
            uint size = 520;
            return QueryFullProcessImageNameW(process, 0, path, &size) == 0 ? null : Path.GetFileName(new string(path, 0, (int)size));
        }
        finally { CloseHandle(process); }
    }

    private static bool IsToastWindow(nint hwnd)
    {
        char* buffer = stackalloc char[64];
        int n = GetClassNameW(hwnd, buffer, 64);
        return n == ToastWindowClass.Length && new ReadOnlySpan<char>(buffer, n).SequenceEqual(ToastWindowClass);
    }

    private const int MaxToastParts = 32, MaxToastDepth = 4;
    private const ushort VT_I4 = 3, VT_DISPATCH = 9;
    private static readonly Guid IidAccessible = new(0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary>The app that sent the toast: its accessible tree read breadth first (see <see cref="ToastNames.AppFromParts"/>).</summary>
    private static string ReadToastApp(nint hwnd)
    {
        int init = CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */);
        try
        {
            Guid iid = IidAccessible;
            nint acc;
            if (AccessibleObjectFromWindow(hwnd, 0xFFFFFFFC /* OBJID_CLIENT */, &iid, &acc) < 0 || acc == 0) return ToastNames.Unknown;
            string? clientName = Name(acc, 0);
            var parts = new List<(int, string?)>();
            var queue = new Queue<(nint, int)>();
            queue.Enqueue((acc, 0));
            try
            {
                while (queue.Count > 0 && parts.Count < MaxToastParts)
                {
                    var (parent, depth) = queue.Dequeue();
                    try { ReadChildren(parent, depth, parts, queue); }
                    finally { Release(parent); }
                }
            }
            finally { while (queue.Count > 0) Release(queue.Dequeue().Item1); }
            return ToastNames.AppFromParts(clientName, parts);
        }
        finally { if (init >= 0) CoUninitialize(); }
    }

    /// <summary>Adds each child's role and name; child objects go on the queue, which owns (and releases) them.</summary>
    private static void ReadChildren(nint parent, int depth, List<(int, string?)> parts, Queue<(nint, int)> queue)
    {
        int count = 0;
        if (((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)parent)[8])(parent, &count) < 0 || count <= 0) return; // get_accChildCount
        count = Math.Min(count, MaxToastParts);
        Variant* kids = stackalloc Variant[count];
        int got = 0;
        if (AccessibleChildren(parent, 0, count, kids, &got) < 0) return;
        for (int i = 0; i < got; i++)
        {
            Variant k = kids[i];
            if (k.vt == VT_I4)
            {
                if (parts.Count < MaxToastParts) parts.Add((Role(parent, (int)k.value), Name(parent, (int)k.value)));
            }
            else if (k.vt == VT_DISPATCH && k.value != 0)
            {
                nint dispatch = (nint)k.value, child = 0;
                Guid iid = IidAccessible;
                int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)dispatch)[0])(dispatch, &iid, &child); // QueryInterface
                Release(dispatch);
                if (hr < 0 || child == 0) continue;
                if (parts.Count < MaxToastParts) parts.Add((Role(child, 0), Name(child, 0)));
                if (depth + 1 < MaxToastDepth) queue.Enqueue((child, depth + 1));
                else Release(child);
            }
        }
    }

    /// <summary>IAccessible::get_accName (vtable slot 10) for the object itself (0) or a simple child; null when unreadable.</summary>
    private static string? Name(nint acc, int child)
    {
        Variant id = default;
        id.vt = VT_I4;
        id.value = child;
        nint bstr = 0;
        if (((delegate* unmanaged[Stdcall]<nint, Variant, nint*, int>)(*(nint**)acc)[10])(acc, id, &bstr) < 0 || bstr == 0) return null;
        try { return Marshal.PtrToStringBSTR(bstr); }
        finally { SysFreeString(bstr); }
    }

    /// <summary>IAccessible::get_accRole (vtable slot 13); 0 when it is not a plain role number.</summary>
    private static int Role(nint acc, int child)
    {
        Variant id = default;
        id.vt = VT_I4;
        id.value = child;
        Variant role = default;
        if (((delegate* unmanaged[Stdcall]<nint, Variant, Variant*, int>)(*(nint**)acc)[13])(acc, id, &role) < 0) return 0;
        return role.vt == VT_I4 ? (int)role.value : 0;
    }

    private static void Release(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[2])(unknown);

    public void Dispose() => SetEnabled(false);

    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort vt, r1, r2, r3;
        public long value, pad;
    }

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint mod, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern int UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, int inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern int QueryFullProcessImageNameW(nint process, uint flags, char* path, uint* size);
    [DllImport("kernel32.dll")] private static extern int CloseHandle(nint handle);
    [DllImport("user32.dll")] private static extern int GetClassNameW(nint hwnd, char* name, int max);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint hwnd, uint objectId, Guid* iid, nint* result);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(nint container, int start, int count, Variant* children, int* obtained);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("oleaut32.dll")] private static extern void SysFreeString(nint bstr);
}
