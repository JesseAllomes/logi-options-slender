namespace Slender.Engine.Import;

/// <summary>HID keyboard usage (usage page 0x07) to ChordParser names.</summary>
internal static class HidKeys
{
    private static readonly Dictionary<int, string> Names = Build();

    private static Dictionary<int, string> Build()
    {
        var d = new Dictionary<int, string>
        {
            [40] = "Enter", [41] = "Esc", [42] = "Backspace", [43] = "Tab", [44] = "Space",
            [45] = "Minus", [46] = "Equals", [49] = "Backslash", [51] = "Semicolon", [53] = "Backtick",
            [54] = "Comma", [55] = "Period", [56] = "Slash",
            [70] = "PrintScreen", [73] = "Insert", [74] = "Home", [75] = "PageUp", [76] = "Delete", [77] = "End", [78] = "PageDown",
            [88] = "Enter", [79] = "Right", [80] = "Left", [81] = "Down", [82] = "Up", [101] = "Menu",
        };
        for (int i = 0; i < 26; i++) d[4 + i] = ((char)('A' + i)).ToString();
        for (int i = 0; i < 9; i++) d[30 + i] = ((char)('1' + i)).ToString();
        d[39] = "0";
        for (int i = 1; i <= 12; i++) d[57 + i] = "F" + i;
        for (int i = 13; i <= 24; i++) d[104 + (i - 13)] = "F" + i;
        return d;
    }

    /// <summary>Modifier usage 224-231 to (ChordParser name, order, isRightHand).</summary>
    private static bool Modifier(int usage, out string name, out int order, out bool right)
    {
        right = usage >= 228;
        order = 0;
        name = "";
        switch (usage & ~4)
        {
            case 224: name = "Ctrl"; order = 0; return usage is >= 224 and <= 231;
            case 225: name = "Shift"; order = 1; return usage is >= 224 and <= 231;
            case 226: name = "Alt"; order = 2; return usage is >= 224 and <= 231;
            case 227: name = "Win"; order = 3; return usage is >= 224 and <= 231;
            default: return false;
        }
    }

    public static bool IsModifier(int usage) => Modifier(usage, out _, out _, out _);

    /// <summary>Builds "Ctrl+Shift+P". False if the key or any modifier has no name. Approximation is set when right-hand modifiers were folded to left.</summary>
    public static bool TryChord(int code, IEnumerable<int> modifiers, out string chord, out string? approximation)
    {
        chord = "";
        approximation = null;
        if (!Names.TryGetValue(code, out string? key)) return false;
        var mods = new SortedDictionary<int, string>();
        bool right = false;
        foreach (int m in modifiers)
        {
            if (!Modifier(m, out string n, out int order, out bool r)) return false;
            mods[order] = n;
            right |= r;
        }
        chord = string.Join('+', mods.Values.Append(key));
        if (!ChordParser.TryParse(chord, out _)) return false;
        if (code == 88) approximation = $"Keypad Enter became Enter in '{chord}'.";
        if (right)
        {
            string note = $"Right-hand modifier keys became their left-hand equivalents in '{chord}'.";
            approximation = approximation is null ? note : approximation + " " + note;
        }
        return true;
    }

    public static string DescribeModifiers(IEnumerable<int> modifiers) =>
        string.Join('+', modifiers.Select(m => Modifier(m, out string n, out _, out bool r) ? (r ? "R" : "L") + n : "usage " + m));
}
