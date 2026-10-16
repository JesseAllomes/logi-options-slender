using System.Runtime.InteropServices;

namespace Slender.Engine.Native;

/// <summary>
/// Whether Slender may inject input into the window at a screen point. Windows blocks SendInput into a process with a higher
/// integrity level (an elevated app), silently (UIPI), so a click Slender held back could not be replayed there. Answers are
/// cached per process for a minute. Unknown (access denied, no window) counts as "no": the click is then never held back.
/// </summary>
internal static class WindowIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const long CacheMs = 60_000;

    private static readonly object Gate = new();
    private static readonly Dictionary<uint, (bool Ok, long At)> Cache = new();
    private static int _ownRid = -1;

    public static bool CanInjectAt(int x, int y)
    {
        try
        {
            // Never WindowFromPoint here: it sends WM_NCHITTEST to the window and waits, and this runs inside the mouse hook.
            // A busy app (Excel right after a burst of sheet switches) then stalled every click on the PC (2026-10-16).
            // ChildWindowFromPointEx on the desktop is geometry only: the top-level window at the point, no messages.
            var hwnd = ChildWindowFromPointEx(GetDesktopWindow(), new NativeMethods.POINT { X = x, Y = y },
                CwpSkipInvisible | CwpSkipDisabled | CwpSkipTransparent);
            if (hwnd == 0 || hwnd == GetDesktopWindow()) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            var now = Environment.TickCount64;
            lock (Gate)
            {
                if (Cache.TryGetValue(pid, out var hit) && now - hit.At < CacheMs) return hit.Ok;
            }

            if (_ownRid < 0) _ownRid = IntegrityRid(GetCurrentProcess());
            var rid = Rid(pid);
            var ok = rid >= 0 && _ownRid >= 0 && rid <= _ownRid;
            lock (Gate)
            {
                if (Cache.Count > 256) Cache.Clear();
                Cache[pid] = (ok, now);
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    private static int Rid(uint pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0) return -1;
        try { return IntegrityRid(process); }
        finally { CloseHandle(process); }
    }

    /// <summary>The integrity level RID of a process (0x2000 medium, 0x3000 high, 0x4000 system); -1 when unreadable.</summary>
    private static int IntegrityRid(nint process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token)) return -1;
        try
        {
            GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var size);
            if (size == 0) return -1;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return -1;
                var sid = Marshal.ReadIntPtr(buffer); // TOKEN_MANDATORY_LABEL.Label.Sid
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count == 0 ? -1 : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    private const uint CwpSkipInvisible = 0x0001, CwpSkipDisabled = 0x0002, CwpSkipTransparent = 0x0004;

    [DllImport("user32.dll")]
    private static extern nint ChildWindowFromPointEx(nint parent, NativeMethods.POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int infoClass, nint info, uint length, out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
}
