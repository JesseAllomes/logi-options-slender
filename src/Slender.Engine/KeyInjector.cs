using System.Runtime.InteropServices;
using Slender.Engine.Native;

namespace Slender.Engine;

/// <summary>SendInput helpers (keyboard chords and mouse buttons). Never throws.</summary>
internal static class KeyInjector
{
    public static bool SendChord(IReadOnlyList<KeyStroke> keys)
    {
        if (keys.Count == 0) return false;
        var inputs = new NativeMethods.INPUT[keys.Count * 2];
        for (var i = 0; i < keys.Count; i++)
        {
            inputs[i] = Key(keys[i], up: false);
            inputs[inputs.Length - 1 - i] = Key(keys[i], up: true);
        }
        return Send(inputs);
    }

    public static bool SendMouseButton(string button, bool down)
    {
        uint flags, data = 0;
        switch (button.ToLowerInvariant())
        {
            case "left": flags = down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP; break;
            case "right": flags = down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP; break;
            case "middle": flags = down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP; break;
            case "back": flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP; data = NativeMethods.XBUTTON1; break;
            case "forward": flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP; data = NativeMethods.XBUTTON2; break;
            default: return false;
        }
        var input = new NativeMethods.INPUT { Type = NativeMethods.INPUT_MOUSE };
        input.Mi = new NativeMethods.MOUSEINPUT { DwFlags = flags, MouseData = data };
        return Send([input]);
    }

    private static NativeMethods.INPUT Key(KeyStroke k, bool up)
    {
        var input = new NativeMethods.INPUT { Type = NativeMethods.INPUT_KEYBOARD };
        input.Ki = new NativeMethods.KEYBDINPUT
        {
            WVk = k.Vk,
            DwFlags = (k.Extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0) | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
        };
        return input;
    }

    private static bool Send(NativeMethods.INPUT[] inputs)
    {
        try { return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == inputs.Length; }
        catch { return false; }
    }
}
