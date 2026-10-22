using System.Runtime.InteropServices;

namespace Slender.Tray.Native;

/// <summary>The handful of user32/shell32/kernel32 calls the raw Win32 tray needs. Blittable signatures only (NativeAOT safe).</summary>
internal static unsafe class Win32
{
    public const uint WM_DESTROY = 0x0002, WM_NULL = 0x0000, WM_TIMER = 0x0113, WM_COMMAND = 0x0111, WM_CONTEXTMENU = 0x007B;
    public const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_APP = 0x8000;
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    public const uint NIIF_INFO = 1, NIIF_WARNING = 2;
    public const uint NIN_BALLOONUSERCLICK = 0x405, WM_CLOSE = 0x0010;
    public const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100;
    public const uint IMAGE_ICON = 1, LR_DEFAULTCOLOR = 0;
    public const int SM_CXSMICON = 49, SM_CYSMICON = 50;
    public const nint IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSW
    {
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(char* name);
    [DllImport("kernel32.dll")] public static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] public static extern int GetProcessMemoryInfo(nint process, PROCESS_MEMORY_COUNTERS_EX* counters, uint cb);
    [DllImport("user32.dll")] public static extern ushort RegisterClassW(WNDCLASSW* wc);
    [DllImport("user32.dll")] public static extern int UnregisterClassW(char* className, nint instance);
    [DllImport("user32.dll")] public static extern nint CreateWindowExW(uint ex, char* cls, char* name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, void* param);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern int TranslateMessage(MSG* msg);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(MSG* msg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern int PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern int DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] public static extern nuint SetTimer(nint hwnd, nuint id, uint elapseMs, nint proc);
    [DllImport("user32.dll")] public static extern int KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(char* name);
    [DllImport("user32.dll")] public static extern nint LoadImageW(nint inst, nint name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern nint LoadIconW(nint inst, nint name);
    [DllImport("user32.dll")] public static extern int DestroyIcon(nint icon);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] public static extern int AppendMenuW(nint menu, uint flags, nuint id, char* text);
    [DllImport("user32.dll")] public static extern int DestroyMenu(nint menu);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] public static extern int GetCursorPos(POINT* pt);
    [DllImport("user32.dll")] public static extern int SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern int EnumWindows(delegate* unmanaged<nint, nint, int> proc, nint lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, uint* processId);
    [DllImport("shell32.dll")] public static extern int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);
}
