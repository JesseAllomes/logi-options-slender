using System.Globalization;
using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>One choosable slot icon: a Segoe Fluent Icons code point and a name for tooltips and narration.</summary>
public sealed record RingGlyph(string Code, string Name);

/// <summary>How a slot's Icon string reads: Automatic (null), a glyph ("glyph:E8C8") or a file ("file:C:\path").</summary>
public enum RingIconKind { Automatic, Glyph, File }

/// <summary>Slot icons. Pure.</summary>
public static class RingGlyphs
{
    public const string GlyphPrefix = "glyph:";
    public const string FilePrefix = "file:";

    /// <summary>A curated set of useful glyphs for the icon grid.</summary>
    public static IReadOnlyList<RingGlyph> Catalog { get; } =
    [
        new("E8C6", "Cut"), new("E8C8", "Copy"), new("E77F", "Paste"), new("E7A7", "Undo"), new("E7A6", "Redo"),
        new("E72B", "Back"), new("E72A", "Forward"), new("E74E", "Save"), new("E749", "Print"), new("E721", "Search"),
        new("E80F", "Home"), new("E8B7", "Folder"), new("E8A5", "Document"), new("E715", "Mail"), new("E787", "Calendar"),
        new("E8BD", "Chat"), new("E774", "Web"), new("E71B", "Link"), new("E72E", "Lock"), new("E713", "Settings"),
        new("E767", "Volume"), new("E74F", "Mute"), new("E768", "Play"), new("E769", "Pause"), new("E893", "Next track"),
        new("E892", "Previous track"), new("E706", "Brightness"), new("E8A3", "Zoom in"), new("E71F", "Zoom out"), new("E740", "Full screen"),
        new("E72C", "Refresh"), new("E74D", "Delete"), new("E710", "Add"), new("E73E", "Done"), new("E711", "Close"),
        new("E70F", "Edit"), new("E718", "Pin"), new("E734", "Favourite"), new("E76E", "Emoji"), new("E765", "Keyboard"),
        new("E962", "Mouse"), new("E722", "Camera"), new("E91B", "Picture"), new("E720", "Microphone"), new("E8EF", "Calculator"),
        new("E943", "Code"), new("E72D", "Share"), new("E896", "Download"), new("E898", "Upload"), new("EA80", "Idea"),
    ];

    public static string GlyphIcon(string code) => GlyphPrefix + code.ToUpperInvariant();

    public static string FileIcon(string path) => FilePrefix + path;

    public static RingIconKind Kind(string? icon, out string value)
    {
        value = string.Empty;
        if (icon is not null && icon.StartsWith(GlyphPrefix, StringComparison.OrdinalIgnoreCase) && icon.Length > GlyphPrefix.Length)
        {
            value = icon[GlyphPrefix.Length..];
            return RingIconKind.Glyph;
        }

        if (icon is not null && icon.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) && icon.Length > FilePrefix.Length)
        {
            value = icon[FilePrefix.Length..];
            return RingIconKind.File;
        }

        return RingIconKind.Automatic;
    }

    /// <summary>The character for a hex code point ("E8C8"), or null when it is not valid hex.</summary>
    public static string? ToChar(string code) =>
        int.TryParse(code, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp) && cp is > 0 and <= 0x10FFFF && !(cp is >= 0xD800 and <= 0xDFFF)
            ? char.ConvertFromUtf32(cp)
            : null;

    /// <summary>The glyph code a slot shows when its icon is Automatic and no app icon applies.</summary>
    public static string AutoGlyph(RingSlot slot)
    {
        if (slot.IsFolder)
        {
            return "E8B7";
        }

        return slot.Action switch
        {
            ActionSpec.Keys k => k.Chord.ToLowerInvariant() switch
            {
                "ctrl+x" => "E8C6",
                "ctrl+c" => "E8C8",
                "ctrl+v" => "E77F",
                "ctrl+z" => "E7A7",
                "ctrl+y" => "E7A6",
                "alt+left" => "E72B",
                "alt+right" => "E72A",
                "ctrl+s" => "E74E",
                "ctrl+p" => "E749",
                "volumeup" or "volumedown" => "E767",
                "volumemute" => "E74F",
                "mediaplaypause" => "E768",
                "medianext" => "E893",
                "mediaprevious" => "E892",
                "win+shift+s" => "E91B",
                _ => "E765",
            },
            ActionSpec.MouseButton m => m.Button switch { "Back" => "E72B", "Forward" => "E72A", _ => "E962" },
            ActionSpec.Excel => "E8A5",
            ActionSpec.ActionsRing => "E9AE",
            ActionSpec.Launch l => LooksLikeUrl(l.Target) ? "E774" : "E8A7",
            ActionSpec.Brightness => "E706",
            ActionSpec.Disabled => "E711",
            ActionSpec.Text => "E8D2",
            ActionSpec.Macro => "E768", // play
            _ => "E710",
        };
    }

    private static bool LooksLikeUrl(string target) =>
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
