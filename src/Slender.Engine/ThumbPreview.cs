using System.Globalization;

namespace Slender.Engine;

/// <summary>
/// Receives the thumb wheel and the top button while Settings' thumb wheel card is open (preview mode). The engine runs nothing
/// for them then. Called on the hook or HID thread: implementations must return at once and never throw.
/// </summary>
public interface IThumbPreviewSink
{
    /// <summary>A thumb wheel turn, 1/120 notch units, positive = right.</summary>
    void Rotated(int delta);

    /// <summary>The top button went down (true) or up.</summary>
    void TopButton(bool down);
}

/// <summary>
/// Line protocol between the tray (pipe server) and Settings (client) for the thumb wheel card's live preview:
/// "r &lt;delta&gt;" for a turn, "t 1" / "t 0" for the top button. The client never writes; closing it ends preview mode.
/// </summary>
public static class ThumbPreview
{
    /// <summary>Current-user-only named pipe the tray listens on.</summary>
    public const string PipeName = "Slender.ThumbPreview";

    public static string Rotation(int delta) => "r " + delta.ToString(CultureInfo.InvariantCulture);

    public static string Top(bool down) => down ? "t 1" : "t 0";

    /// <summary>Parses one line. Kind is 'r' (Value = delta) or 't' (Value = 1 down, 0 up); false for anything else.</summary>
    public static bool TryParse(string? line, out char kind, out int value)
    {
        kind = '\0';
        value = 0;
        if (line is null || line.Length < 3 || line[1] != ' ' || line[0] is not ('r' or 't')) return false;
        if (!int.TryParse(line.AsSpan(2), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return false;
        kind = line[0];
        return kind == 'r' || value is 0 or 1;
    }
}
