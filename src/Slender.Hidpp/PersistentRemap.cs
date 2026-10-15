namespace Slender.Hidpp;

/// <summary>HID++ 0x1C00 Persistent Remappable Action: feature id, functions and wire constants (docs/protocol.md).</summary>
public static class PersistentRemapId
{
    public const ushort Feature = 0x1C00;

    // Function numbers (high nibble of the function/SwId byte), from Solaar hidpp20.py request codes 0x00..0x50.
    public const byte FnGetCapabilities = 0;
    public const byte FnGetCount = 1;
    public const byte FnGetCidInfo = 2;
    public const byte FnGetMapping = 3;
    /// <summary>Writes persistent keyboard memory.</summary>
    public const byte FnSetMapping = 4;
    /// <summary>Writes persistent keyboard memory (restores the factory mapping).</summary>
    public const byte FnReset = 5;

    /// <summary>Host index meaning "the current host" (Solaar uses 0xFF throughout).</summary>
    public const byte CurrentHost = 0xFF;
}

/// <summary>Action type byte of a 0x1C00 mapping (Solaar special_keys.ACTIONID).</summary>
public enum OnboardActionType : byte
{
    Empty = 0x00,
    Key = 0x01,
    Mouse = 0x02,
    XDisplacement = 0x03,
    YDisplacement = 0x04,
    VScroll = 0x05,
    HScroll = 0x06,
    Consumer = 0x07,
    Internal = 0x08,
    Power = 0x09,
}

/// <summary>Modifier bits of a keystroke action (Solaar KEYMOD, left-hand modifiers).</summary>
[Flags]
public enum OnboardModifiers : byte
{
    None = 0,
    Ctrl = 0x01,
    Shift = 0x02,
    Alt = 0x04,
    Meta = 0x08,
}

/// <summary>The 4 data bytes of a mapping: action type (1), parameter (2, big endian), modifier mask (1).</summary>
public readonly record struct OnboardAction(OnboardActionType Type, ushort Param, byte Modifiers)
{
    public byte[] ToBytes() => [(byte)Type, (byte)(Param >> 8), (byte)Param, Modifiers];

    public static OnboardAction FromBytes(ReadOnlySpan<byte> b) =>
        new((OnboardActionType)b[0], (ushort)((b[1] << 8) | b[2]), b[3]);

    public static OnboardAction Keystroke(byte hidUsage, OnboardModifiers mods) =>
        new(OnboardActionType.Key, hidUsage, (byte)mods);

    public static OnboardAction Consumer(ushort usage) => new(OnboardActionType.Consumer, usage, 0);

    public override string ToString() => Type switch
    {
        OnboardActionType.Empty => "Empty",
        OnboardActionType.Key => $"Key 0x{Param:X2} mods 0x{Modifiers:X2}",
        OnboardActionType.Consumer => $"Consumer 0x{Param:X4}",
        _ => $"{Type} 0x{Param:X4} mods 0x{Modifiers:X2}",
    };
}

/// <summary>One control as read from 0x1C00: CID, current action and the raw status byte.</summary>
public readonly record struct OnboardMapping(ushort Cid, OnboardAction Action, byte Status);

/// <summary>Converts Slender chord strings to 0x1C00 actions. Anything not expressible is rejected with a reason.</summary>
public static class OnboardChordEncoder
{
    private static readonly Dictionary<string, OnboardModifiers> Mods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = OnboardModifiers.Ctrl, ["Control"] = OnboardModifiers.Ctrl,
        ["Shift"] = OnboardModifiers.Shift,
        ["Alt"] = OnboardModifiers.Alt,
        ["Win"] = OnboardModifiers.Meta, ["Windows"] = OnboardModifiers.Meta, ["Meta"] = OnboardModifiers.Meta,
    };

    // HID usage page 0x0C values (Solaar special_keys.py HID_CONSUMERCODES).
    private static readonly Dictionary<string, ushort> Consumer = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MediaPlayPause"] = 0x00CD, ["MediaNext"] = 0x00B5, ["MediaPrevious"] = 0x00B6, ["MediaStop"] = 0x00B7,
        ["VolumeMute"] = 0x00E2, ["VolumeUp"] = 0x00E9, ["VolumeDown"] = 0x00EA,
        ["Calculator"] = 0x0192, ["BrightnessUp"] = 0x006F, ["BrightnessDown"] = 0x0070,
        ["BrowserSearch"] = 0x0221, ["BrowserHome"] = 0x0223, ["BrowserBack"] = 0x0224,
    };

    private static readonly Dictionary<string, byte> Keys = BuildKeys();

    private static Dictionary<string, byte> BuildKeys()
    {
        var d = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = 0x28, ["Return"] = 0x28, ["Esc"] = 0x29, ["Escape"] = 0x29, ["Backspace"] = 0x2A,
            ["Tab"] = 0x2B, ["Space"] = 0x2C, ["Minus"] = 0x2D, ["Plus"] = 0x2E, ["Equals"] = 0x2E,
            ["Backslash"] = 0x31, ["Semicolon"] = 0x33, ["Backtick"] = 0x35, ["Comma"] = 0x36, ["Period"] = 0x37,
            ["Slash"] = 0x38, ["PrintScreen"] = 0x46, ["Insert"] = 0x49, ["Ins"] = 0x49, ["Home"] = 0x4A,
            ["PageUp"] = 0x4B, ["PgUp"] = 0x4B, ["Delete"] = 0x4C, ["Del"] = 0x4C, ["End"] = 0x4D,
            ["PageDown"] = 0x4E, ["PgDn"] = 0x4E, ["Right"] = 0x4F, ["Left"] = 0x50, ["Down"] = 0x51, ["Up"] = 0x52,
            ["Menu"] = 0x65,
        };
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = (byte)(0x04 + (c - 'A'));
        for (var c = '1'; c <= '9'; c++) d[c.ToString()] = (byte)(0x1E + (c - '1'));
        d["0"] = 0x27;
        for (var i = 1; i <= 12; i++) d["F" + i] = (byte)(0x39 + i);
        for (var i = 13; i <= 24; i++) d["F" + i] = (byte)(0x68 + i - 13);
        return d;
    }

    /// <summary>
    /// "Ctrl+Shift+S" gives Key + usage + left-hand modifier mask. A single media name gives a Consumer action.
    /// Rejected: empty, unknown names, modifiers only, more than one main key, modifiers with a media key,
    /// more than one modifier (the K860 drops them all).
    /// </summary>
    public static bool TryEncode(string? chord, out OnboardAction action, out string error)
    {
        action = default;
        error = "";
        if (string.IsNullOrWhiteSpace(chord)) { error = "empty chord"; return false; }
        var mods = OnboardModifiers.None;
        string? main = null;
        foreach (var raw in chord.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) { error = "empty chord segment"; return false; }
            if (Mods.TryGetValue(part, out var m)) { mods |= m; continue; }
            if (!Keys.ContainsKey(part) && !Consumer.ContainsKey(part)) { error = $"cannot encode '{part}'"; return false; }
            if (main != null) { error = "more than one non-modifier key"; return false; }
            main = part;
        }
        if (main == null) { error = "no main key (modifiers only)"; return false; }
        if (Consumer.TryGetValue(main, out var usage))
        {
            if (mods != OnboardModifiers.None) { error = "modifiers cannot be combined with a media key"; return false; }
            action = OnboardAction.Consumer(usage);
            return true;
        }
        // ERGO K860 (tested 2026-10-15): one modifier is honoured (Ctrl+Esc, Win+E work); with two or more the firmware drops
        // every modifier and sends the bare key (Ctrl+Shift+Esc arrived as Esc). Refuse rather than store a silent surprise.
        if (System.Numerics.BitOperations.PopCount((uint)mods) > 1) { error = "the keyboard only stores one modifier (e.g. Ctrl+Esc or Win+E)"; return false; }
        action = OnboardAction.Keystroke(Keys[main], mods);
        return true;
    }

    public static OnboardAction Encode(string chord) =>
        TryEncode(chord, out var a, out var err) ? a : throw new ArgumentException($"'{chord}': {err}", nameof(chord));
}
