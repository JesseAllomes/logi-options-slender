using System.Text.Json;

namespace Slender.Settings.Logic;

/// <summary>Window size and last selection, remembered between runs (%LOCALAPPDATA%\Slender\settings-ui.json).</summary>
public sealed class UiState
{
    public int Width { get; set; } = MinWidth;
    public int Height { get; set; } = DefaultHeight;

    /// <summary>Default window height (DIPs); taller since 2026-10-20 (user asked: the card and pages need the room).</summary>
    public const int DefaultHeight = 900;

    /// <summary>
    /// Smallest window (DIPs). Width: the size the user set on 2026-10-21 and asked never to go below. Height: the old 640 plus
    /// 2.5 app-rail icons (52 each), as the user asked on 2026-10-21.
    /// </summary>
    public const int MinWidth = 1166;
    public const int MinHeight = 770;

    /// <summary>Top-left of the full window when it was last closed (screen pixels); null until then.</summary>
    public int? X { get; set; }
    public int? Y { get; set; }

    /// <summary>2 = the remembered height has had the taller default applied once (missing in older files, so 0).</summary>
    public int SizeVersion { get; set; }
    public string Device { get; set; } = DeviceIds.Mouse;

    /// <summary>Process name of the selected app; null = All apps.</summary>
    public string? App { get; set; }

    /// <summary>True hides the picker's Shortcuts tab (suggested shortcuts) everywhere.</summary>
    public bool HideShortcutSuggestions { get; set; }

    /// <summary>Suggested shortcuts hidden one by one: ShortcutLibrary.HideKey(group id, chord).</summary>
    public List<string> HiddenShortcuts { get; set; } = [];

    /// <summary>Unused since the Advanced expander was replaced by My patterns; kept so old ui.json files still load.</summary>
    public bool HapticsAdvancedOpen { get; set; }

    public static UiState Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                UiState? s = JsonSerializer.Deserialize<UiState>(File.ReadAllText(path));
                if (s is not null)
                {
                    s.Width = Math.Clamp(s.Width, MinWidth, 4000);
                    s.Height = Math.Clamp(s.Height, MinHeight, 3000);
                    if (s.SizeVersion < 2)
                    {
                        s.Height = Math.Max(s.Height, DefaultHeight); // files from before the taller default grow once
                        s.SizeVersion = 2;
                    }
                    return s;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall through to defaults.
        }

        return new UiState();
    }

    public void Save(string path)
    {
        SizeVersion = 2; // whatever is saved now is the user's own size
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not worth failing the close for.
        }
    }
}
