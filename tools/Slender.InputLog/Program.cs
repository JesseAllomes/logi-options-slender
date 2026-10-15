// Diagnostic: logs raw input (keyboard, consumer control, system control, HID++ vendor collection) from ONE device whose
// path contains the given filter (default the ERGO K860, "pid&b359"), for a fixed number of seconds, then exits.
// Used to see what a 0x1C00-remapped key actually sends. Nothing is logged from other devices.
using System.Runtime.InteropServices;
using System.Text;

var filter = (args.FirstOrDefault(a => a.StartsWith("--filter=", StringComparison.Ordinal))?["--filter=".Length..] ?? "pid&b359").ToLowerInvariant();
var seconds = args.FirstOrDefault(a => a.StartsWith("--seconds=", StringComparison.Ordinal)) is { } s && int.TryParse(s["--seconds=".Length..], out var n) ? n : 30;
Console.WriteLine($"Logging raw input from devices matching '{filter}' for {seconds} s...");

var names = new Dictionary<nint, string>();
string NameOf(nint h)
{
    if (names.TryGetValue(h, out var cached)) return cached;
    uint size = 0;
    Native.GetRawInputDeviceInfoW(h, Native.RIDI_DEVICENAME, IntPtr.Zero, ref size);
    var buf = Marshal.AllocHGlobal((int)size * 2);
    try
    {
        Native.GetRawInputDeviceInfoW(h, Native.RIDI_DEVICENAME, buf, ref size);
        return names[h] = (Marshal.PtrToStringUni(buf) ?? "").ToLowerInvariant();
    }
    finally { Marshal.FreeHGlobal(buf); }
}

var start = Environment.TickCount64;
Native.WndProc proc = (hwnd, msg, wParam, lParam) =>
{
    if (msg == Native.WM_INPUT)
    {
        uint size = 0;
        Native.GetRawInputData(lParam, Native.RID_INPUT, IntPtr.Zero, ref size, 24);
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            Native.GetRawInputData(lParam, Native.RID_INPUT, buf, ref size, 24);
            uint type = (uint)Marshal.ReadInt32(buf, 0);
            nint dev = Marshal.ReadIntPtr(buf, 8);
            var name = NameOf(dev);
            if (name.Contains(filter))
            {
                var t = (Environment.TickCount64 - start) / 1000.0;
                var col = name.Contains("&col") ? name[name.IndexOf("&col", StringComparison.Ordinal)..][..6] : "";
                if (type == 1) // keyboard
                {
                    ushort make = (ushort)Marshal.ReadInt16(buf, 24), flags = (ushort)Marshal.ReadInt16(buf, 26), vkey = (ushort)Marshal.ReadInt16(buf, 30);
                    uint wm = (uint)Marshal.ReadInt32(buf, 32);
                    Console.WriteLine($"{t,7:F2}s KEYBOARD {col} vk=0x{vkey:X2} make=0x{make:X2} flags=0x{flags:X} {(wm is 0x100 or 0x104 ? "down" : "up")}");
                }
                else if (type == 2) // hid
                {
                    int each = Marshal.ReadInt32(buf, 24), count = Marshal.ReadInt32(buf, 28);
                    var sb = new StringBuilder();
                    for (int i = 0; i < each * count; i++) sb.Append(Marshal.ReadByte(buf, 32 + i).ToString("X2"));
                    Console.WriteLine($"{t,7:F2}s HID {col} {sb}");
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return 0;
    }
    if (msg == Native.WM_TIMER) { Native.PostQuitMessage(0); return 0; }
    return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
};

var wc = new Native.WNDCLASSEXW { cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), lpszClassName = "SlenderInputLog" };
Native.RegisterClassExW(ref wc);
var hwnd = Native.CreateWindowExW(0, "SlenderInputLog", "", 0, 0, 0, 0, 0, new IntPtr(-3), 0, 0, 0);
var devices = new[]
{
    new Native.RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = hwnd },
    new Native.RAWINPUTDEVICE { usUsagePage = 0x0C, usUsage = 0x01, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = hwnd },
    new Native.RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x80, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = hwnd },
    new Native.RAWINPUTDEVICE { usUsagePage = 0xFF43, usUsage = 0x0202, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = hwnd },
    new Native.RAWINPUTDEVICE { usUsagePage = 0xFF43, usUsage = 0x0204, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = hwnd },
};
if (!Native.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>()))
    Console.WriteLine($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
Native.SetTimer(hwnd, 1, (uint)(seconds * 1000), 0);
while (Native.GetMessageW(out var m, 0, 0, 0) > 0) { Native.TranslateMessage(ref m); Native.DispatchMessageW(ref m); }
GC.KeepAlive(proc);
Console.WriteLine("Done.");

static class Native
{
    public const uint WM_INPUT = 0x00FF, WM_TIMER = 0x0113, RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIDEV_INPUTSINK = 0x100;
    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public nint hwndTarget; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW { public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra; public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public nint hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
    [DllImport("user32.dll")] public static extern uint GetRawInputData(nint h, uint cmd, nint data, ref uint size, uint header);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfoW(nint h, uint cmd, nint data, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] public static extern nint SetTimer(nint h, nint id, uint ms, nint proc);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG m, nint h, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG m);
}
