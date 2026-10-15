namespace Slender.Engine;

/// <summary>One key of a chord. Extended keys need KEYEVENTF_EXTENDEDKEY.</summary>
public readonly record struct KeyStroke(ushort Vk, bool Extended);

/// <summary>Parses "Ctrl+Shift+P", "Alt+Left", "Win+Tab", "Ctrl+PageDown", "F5" into virtual keys (modifiers first).</summary>
public static class ChordParser
{
    private static readonly Dictionary<string, KeyStroke> Names = Build();

    private static Dictionary<string, KeyStroke> Build()
    {
        var d = new Dictionary<string, KeyStroke>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = new(0xA2, false), ["Control"] = new(0xA2, false),
            ["Shift"] = new(0xA0, false),
            ["Alt"] = new(0xA4, false),
            ["Win"] = new(0x5B, true), ["Windows"] = new(0x5B, true), ["Meta"] = new(0x5B, true),
            ["Enter"] = new(0x0D, false), ["Return"] = new(0x0D, false),
            ["Esc"] = new(0x1B, false), ["Escape"] = new(0x1B, false),
            ["Tab"] = new(0x09, false), ["Space"] = new(0x20, false),
            ["Backspace"] = new(0x08, false),
            ["Delete"] = new(0x2E, true), ["Del"] = new(0x2E, true),
            ["Insert"] = new(0x2D, true), ["Ins"] = new(0x2D, true),
            ["Home"] = new(0x24, true), ["End"] = new(0x23, true),
            ["PageUp"] = new(0x21, true), ["PgUp"] = new(0x21, true),
            ["PageDown"] = new(0x22, true), ["PgDn"] = new(0x22, true),
            ["Left"] = new(0x25, true), ["Up"] = new(0x26, true),
            ["Right"] = new(0x27, true), ["Down"] = new(0x28, true),
            ["PrintScreen"] = new(0x2C, true), ["Menu"] = new(0x5D, true),
            ["Comma"] = new(0xBC, false), ["Period"] = new(0xBE, false),
            ["Minus"] = new(0xBD, false), ["Plus"] = new(0xBB, false), ["Equals"] = new(0xBB, false),
            ["Semicolon"] = new(0xBA, false), ["Slash"] = new(0xBF, false),
            ["MediaPlayPause"] = new(0xB3, false), ["MediaNext"] = new(0xB0, false),
            ["MediaPrevious"] = new(0xB1, false), ["MediaStop"] = new(0xB2, false),
            ["VolumeMute"] = new(0xAD, false), ["VolumeDown"] = new(0xAE, false), ["VolumeUp"] = new(0xAF, false),
            ["Backslash"] = new(0xDC, false), ["Backtick"] = new(0xC0, false),
        };
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = new KeyStroke(c, false);
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = new KeyStroke(c, false);
        for (var i = 1; i <= 24; i++) d["F" + i] = new KeyStroke((ushort)(0x6F + i), false);
        return d;
    }

    private static bool IsModifier(ushort vk) => vk is 0xA2 or 0xA0 or 0xA4 or 0x5B;

    /// <summary>Returns false for empty/unknown input. Modifiers are ordered first, then the main key.</summary>
    public static bool TryParse(string? chord, out IReadOnlyList<KeyStroke> keys)
    {
        keys = [];
        if (string.IsNullOrWhiteSpace(chord)) return false;
        var mods = new List<KeyStroke>();
        var rest = new List<KeyStroke>();
        foreach (var raw in chord.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0 || !Names.TryGetValue(part, out var k)) return false;
            (IsModifier(k.Vk) ? mods : rest).Add(k);
        }
        if (rest.Count == 0) return false;
        mods.AddRange(rest);
        keys = mods;
        return true;
    }
}
