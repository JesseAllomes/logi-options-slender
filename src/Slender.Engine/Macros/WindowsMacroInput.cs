using System.Runtime.InteropServices;
using Slender.Engine.Native;

namespace Slender.Engine.Macros;

/// <summary>SendInput for macros: the existing key and button senders plus pointer moves and the Esc check. Never throws.</summary>
public sealed class WindowsMacroInput(IShellLauncher? shell = null, ILog? log = null) : IMacroInput
{
    private const uint MouseMove = 0x0001, MouseAbsolute = 0x8000, MouseVirtualDesk = 0x4000;
    private const int SmXVirtual = 76, SmYVirtual = 77, SmCxVirtual = 78, SmCyVirtual = 79;
    private const int VkEscape = 0x1B;

    private readonly IShellLauncher _shell = shell ?? new ShellLauncher();
    private readonly ILog _log = log ?? NullLog.Instance;

    public bool KeyDown(KeyStroke key) => KeyInjector.SendKeyEvents([new KeyEvent(key, false)]);

    public bool KeyUp(KeyStroke key) => KeyInjector.SendKeyEvents([new KeyEvent(key, true)]);

    public bool Chord(IReadOnlyList<KeyStroke> keys) => KeyInjector.SendChord(keys);

    public bool Type(IReadOnlyList<TextKey> keys) => KeyInjector.SendText(keys);

    public bool MouseButton(string button, bool down) => KeyInjector.SendMouseButton(button, down);

    public bool Wheel(int vertical, int horizontal) => KeyInjector.SendVerticalWheel(vertical) && KeyInjector.SendHorizontalWheel(horizontal);

    /// <summary>Relative moves go through the pointer's position plus the offset, so mouse acceleration never bends them.</summary>
    public bool MoveBy(int dx, int dy)
    {
        try { return NativeMethods.GetCursorPos(out var p) && MoveTo(p.X + dx, p.Y + dy); }
        catch { return false; }
    }

    public bool MoveTo(int x, int y)
    {
        try
        {
            int left = GetSystemMetrics(SmXVirtual), top = GetSystemMetrics(SmYVirtual);
            int width = Math.Max(2, GetSystemMetrics(SmCxVirtual)), height = Math.Max(2, GetSystemMetrics(SmCyVirtual));
            x = Math.Clamp(x, left, left + width - 1);
            y = Math.Clamp(y, top, top + height - 1);
            var input = new NativeMethods.INPUT { Type = NativeMethods.INPUT_MOUSE };
            input.Mi = new NativeMethods.MOUSEINPUT
            {
                Dx = (int)((x - left) * 65535L / (width - 1)),
                Dy = (int)((y - top) * 65535L / (height - 1)),
                DwFlags = MouseMove | MouseAbsolute | MouseVirtualDesk,
            };
            return NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>()) == 1;
        }
        catch { return false; }
    }

    public void Launch(string target, string? arguments)
    {
        try { _shell.Open(target, arguments); }
        catch (Exception ex) { _log.Error($"macro launch '{target}' failed", ex); }
    }

    public bool EscapeHeld()
    {
        try { return (GetAsyncKeyState(VkEscape) & 0x8000) != 0; }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);
}
