using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Slender.Settings.Logic;

namespace Slender.Settings.Services;

/// <summary>
/// A low-level keyboard hook that lives only while the key recorder is open, so Win and Alt shortcuts can be recorded
/// instead of acting (WinChordCapture decides). It only acts while the Settings window is in front; keys typed into other
/// windows pass straight through. Injected keys (the tray's own output) are ignored. Dispose removes it.
/// </summary>
internal sealed class RecorderKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;

    private readonly nint _window;
    private readonly DispatcherQueue _queue;
    private readonly Action<CaptureResult> _recorded;
    private readonly WinChordCapture _capture = new();
    private readonly HookProc _proc; // kept alive for as long as the hook is installed
    private nint _hook;

    public RecorderKeyHook(nint window, DispatcherQueue queue, Action<CaptureResult> recorded)
    {
        _window = window;
        _queue = queue;
        _recorded = recorded;
        _proc = OnHook;
        _hook = SetWindowsHookExW(WhKeyboardLl, _proc, GetModuleHandleW(null), 0);
    }

    public bool IsInstalled => _hook != 0;

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }

        _capture.Reset();
    }

    private nint OnHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            int message = (int)wParam;
            bool down = message is WmKeyDown or WmSysKeyDown;
            bool up = message is WmKeyUp or WmSysKeyUp;
            bool ours = NativeMethods.GetForegroundWindow() == _window;
            if ((down || up) && (info.Flags & LlkhfInjected) == 0 && (ours || _capture.WinDown))
            {
                CaptureResult r = _capture.OnKey((ushort)info.VkCode, down, Held(0x11), Held(0x12), Held(0x10));
                if (r.Chord is not null || r.Unsupported)
                {
                    _queue.TryEnqueue(() => _recorded(r)); // keep the hook callback short
                }

                if (r.Swallow)
                {
                    return 1;
                }
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int idHook, HookProc proc, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
}
