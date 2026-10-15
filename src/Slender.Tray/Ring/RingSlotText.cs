using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tray.Ring;

/// <summary>What the ring shows for a slot: name, shortcut text and glyph. Pure, no state.</summary>
public static class RingSlotText
{
    private static readonly Dictionary<string, (string Name, string Glyph)> Chords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl+X"] = ("Cut", ""),
        ["Ctrl+C"] = ("Copy", ""),
        ["Ctrl+V"] = ("Paste", ""),
        ["Ctrl+Z"] = ("Undo", ""),
        ["Ctrl+Y"] = ("Redo", ""),
        ["Ctrl+Shift+Z"] = ("Redo", ""),
        ["Ctrl+S"] = ("Save", ""),
        ["Ctrl+A"] = ("Select all", ""),
        ["Ctrl+F"] = ("Find", ""),
        ["Ctrl+P"] = ("Print", ""),
        ["Alt+Left"] = ("Back", ""),
        ["Alt+Right"] = ("Forward", ""),
        ["VolumeUp|VolumeDown"] = ("Volume", ""),
        ["VolumeMute"] = ("Mute", ""),
        ["PlayPause"] = ("Play / pause", ""),
        ["MediaPlayPause"] = ("Play / pause", ""),
        ["MediaNext"] = ("Next track", ""),
        ["MediaPrev"] = ("Previous track", ""),
    };

    public const string KeyboardGlyph = "", GlobeGlyph = "", FolderGlyph = "", TextGlyph = "", BrightnessGlyph = "",
        MouseGlyph = "", BackGlyph = "", ForwardGlyph = "", AppGlyph = "", ExcelGlyph = "", OffGlyph = "", RingGlyph = "";

    /// <summary>The slot's own name, else one derived from its action.</summary>
    public static string Name(RingSlot slot)
    {
        if (slot.Name is { Length: > 0 } n) return n;
        if (slot.IsFolder) return RingDefaults.FolderName;
        return slot.Action switch
        {
            ActionSpec.None => string.Empty,
            ActionSpec.Keys k => Chords.TryGetValue(k.Chord.Trim(), out var known) ? known.Name : k.Chord.Contains('|') ? k.Chord.Split('|')[0].Trim() : k.Chord,
            ActionSpec.MouseButton m => m.Button.ToLowerInvariant() switch { "back" => "Back", "forward" => "Forward", var b => b.Length == 0 ? "Click" : char.ToUpperInvariant(b[0]) + b[1..] + " click" },
            ActionSpec.Excel e => SpaceOut(e.Command.ToString()),
            ActionSpec.Launch l => LaunchName(l.Target),
            ActionSpec.Text t => Snippet(t.Value),
            ActionSpec.Brightness b => b.StepPercent >= 0 ? "Brightness up" : "Brightness down",
            ActionSpec.Disabled => "Off",
            ActionSpec.ActionsRing => "Actions ring",
            _ => ActionText.Describe(slot.Action),
        };
    }

    /// <summary>The small mono line under the name: the chord ("Ctrl X"), the mouse button, or what the slot does.</summary>
    public static string Shortcut(RingSlot slot)
    {
        if (slot.IsFolder) return "Folder";
        return slot.Action switch
        {
            ActionSpec.Keys k when k.Chord.Contains('|') => "Scroll to adjust",
            ActionSpec.Keys k => k.Chord.Replace('+', ' '),
            ActionSpec.MouseButton m => m.Button.ToLowerInvariant() switch { "back" => "Mouse back", "forward" => "Mouse fwd", var b => "Mouse " + b },
            ActionSpec.Brightness => "Scroll to adjust",
            ActionSpec.Excel => "Excel",
            ActionSpec.Launch => "Open",
            ActionSpec.Text => "Text",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The Segoe Fluent Icons glyph for a slot. "glyph:XXXX" is honoured; null when the slot should show a file icon
    /// (see <see cref="IconFile"/>) and none could be loaded the caller falls back to <see cref="FallbackGlyph"/>.
    /// </summary>
    public static string Glyph(RingSlot slot)
    {
        if (slot.Icon is { } icon && icon.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(icon.AsSpan(6), System.Globalization.NumberStyles.HexNumber, null, out var cp) && cp is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF))
            return char.ConvertFromUtf32(cp);
        return FallbackGlyph(slot);
    }

    /// <summary>A sensible glyph per action type.</summary>
    public static string FallbackGlyph(RingSlot slot)
    {
        if (slot.IsFolder) return FolderGlyph;
        return slot.Action switch
        {
            ActionSpec.Keys k => Chords.TryGetValue(k.Chord.Trim(), out var known) ? known.Glyph : KeyboardGlyph,
            ActionSpec.MouseButton m => m.Button.ToLowerInvariant() switch { "back" => BackGlyph, "forward" => ForwardGlyph, _ => MouseGlyph },
            ActionSpec.Launch l => IsUrl(l.Target) ? GlobeGlyph : AppGlyph,
            ActionSpec.Text => TextGlyph,
            ActionSpec.Brightness => BrightnessGlyph,
            ActionSpec.Excel => ExcelGlyph,
            ActionSpec.Disabled => OffGlyph,
            ActionSpec.ActionsRing => RingGlyph,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The file whose icon the slot shows: "file:path", or a Launch target that is an exe or shortcut when no icon is set. Null otherwise.
    /// Only local drives are used: UNC paths (either slash) and mapped network drives are never probed.
    /// </summary>
    public static string? IconFile(RingSlot slot)
    {
        string? path = null;
        if (slot.Icon is { Length: > 0 } icon)
            path = icon.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && icon.Length > 5 ? icon[5..] : null;
        else if (!slot.IsFolder && slot.Action is ActionSpec.Launch l && !IsUrl(l.Target)
            && (l.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || l.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            path = l.Target;
        if (path is null || !IsLocal(path)) return null;
        return path;
    }

    private static bool IsLocal(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith(@"\/", StringComparison.Ordinal) || path.StartsWith(@"/\", StringComparison.Ordinal)) return false;
        if (!Path.IsPathFullyQualified(path)) return false;
        try
        {
            var root = Path.GetPathRoot(path);
            return root is not null && new DriveInfo(root).DriveType is not (DriveType.Network or DriveType.NoRootDirectory);
        }
        catch (Exception e) when (e is ArgumentException or IOException) { return false; }
    }

    /// <summary>Cuts text to about <paramref name="maxChars"/> characters with an ellipsis, never splitting a surrogate pair.</summary>
    public static string Fit(string text, int maxChars)
    {
        if (text.Length <= maxChars || maxChars < 2) return text;
        int cut = maxChars - 1;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
    }

    private static bool IsUrl(string target) => target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string LaunchName(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "Open";
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var t = target.TrimEnd('\\', '/');
        var name = Path.GetFileNameWithoutExtension(t);
        return name.Length > 0 ? name : t;
    }

    private static string Snippet(string value)
    {
        var line = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length == 0 ? "Text" : Fit(line, 18);
    }

    private static string SpaceOut(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append(' ').Append(char.ToLowerInvariant(pascal[i]));
            else sb.Append(pascal[i]);
        }
        return sb.ToString();
    }
}
