using Slender.Hidpp;

namespace Slender.Engine;

public sealed record OnboardChoice(string Name, string Label);

/// <summary>
/// What the picker offers: one optional modifier plus a key, or a media key. Everything is checked through
/// <see cref="OnboardChordEncoder"/>, so the picker can never offer or accept what the keyboard cannot store.
/// </summary>
public static class OnboardChoices
{
    /// <summary>"" means no modifier.</summary>
    public static IReadOnlyList<OnboardChoice> Modifiers { get; } =
        [new("", "No modifier"), new("Ctrl", "Ctrl"), new("Shift", "Shift"), new("Alt", "Alt"), new("Win", "Win")];

    public static IReadOnlyList<OnboardChoice> Media { get; } =
    [
        new("MediaPlayPause", "Play / pause"), new("MediaNext", "Next track"), new("MediaPrevious", "Previous track"),
        new("MediaStop", "Stop"), new("VolumeMute", "Mute"), new("VolumeUp", "Volume up"), new("VolumeDown", "Volume down"),
        new("Calculator", "Calculator"), new("BrightnessUp", "Brightness up"), new("BrightnessDown", "Brightness down"),
        new("BrowserSearch", "Browser search"), new("BrowserHome", "Browser home"), new("BrowserBack", "Browser back"),
    ];

    public static IReadOnlyList<OnboardChoice> Keys { get; } = BuildKeys();

    /// <summary>Keys then media keys: the one list the picker shows.</summary>
    public static IReadOnlyList<OnboardChoice> MainKeys { get; } = [.. Keys, .. Media];

    private static List<OnboardChoice> BuildKeys()
    {
        var list = new List<OnboardChoice>();
        for (var c = 'A'; c <= 'Z'; c++) list.Add(new(c.ToString(), c.ToString()));
        for (var c = '0'; c <= '9'; c++) list.Add(new(c.ToString(), c.ToString()));
        for (var i = 1; i <= 24; i++) list.Add(new("F" + i, "F" + i));
        foreach (var n in new[] { "Enter", "Esc", "Backspace", "Tab", "Space", "Minus", "Plus", "Backslash", "Semicolon", "Backtick", "Comma", "Period",
                                  "Slash", "PrintScreen", "Insert", "Home", "PageUp", "Delete", "End", "PageDown", "Right", "Left", "Down", "Up", "Menu" })
            list.Add(new(n, n));
        return list;
    }

    /// <summary>The chord for a modifier choice name ("" = none) and a main key name, or the encoder's reason it cannot be stored.</summary>
    public static bool TryBuildChord(string? modifier, string? key, out string chord, out string error)
    {
        chord = "";
        if (string.IsNullOrWhiteSpace(key)) { error = "choose a key"; return false; }
        var text = string.IsNullOrWhiteSpace(modifier) ? key : modifier + "+" + key;
        if (!OnboardChordEncoder.TryEncode(text, out _, out error)) return false;
        chord = text;
        return true;
    }

    /// <summary>Splits a stored chord back into the picker's two choices (modifier "" when none).</summary>
    public static (string Modifier, string Key) Split(string chord)
    {
        var parts = chord.Split('+', StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? (CanonicalModifier(parts[0]), parts[^1]) : ("", parts[0]);
    }

    private static string CanonicalModifier(string m) =>
        m.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl"
        : m.Equals("Windows", StringComparison.OrdinalIgnoreCase) || m.Equals("Meta", StringComparison.OrdinalIgnoreCase) ? "Win" : m;

    /// <summary>Plain text for what the keyboard reports, e.g. "Win+Tab", "Play / pause", "Not set".</summary>
    public static string Describe(OnboardAction action)
    {
        switch (action.Type)
        {
            case OnboardActionType.Empty: return "Not set (keyboard's own function)";
            case OnboardActionType.Key:
            {
                var key = Keys.FirstOrDefault(k => OnboardChordEncoder.TryEncode(k.Name, out var a, out _) && a.Param == action.Param);
                var mods = new List<string>();
                if ((action.Modifiers & (byte)OnboardModifiers.Ctrl) != 0) mods.Add("Ctrl");
                if ((action.Modifiers & (byte)OnboardModifiers.Shift) != 0) mods.Add("Shift");
                if ((action.Modifiers & (byte)OnboardModifiers.Alt) != 0) mods.Add("Alt");
                if ((action.Modifiers & (byte)OnboardModifiers.Meta) != 0) mods.Add("Win");
                mods.Add(key?.Label ?? $"key 0x{action.Param:X2}");
                return string.Join("+", mods);
            }
            case OnboardActionType.Consumer:
            {
                var m = Media.FirstOrDefault(k => OnboardChordEncoder.TryEncode(k.Name, out var a, out _) && a.Param == action.Param);
                return m?.Label ?? $"Media 0x{action.Param:X4}";
            }
            default: return action.ToString();
        }
    }
}
