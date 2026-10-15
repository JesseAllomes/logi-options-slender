namespace Slender.Engine;

/// <summary>
/// How the tray tells Settings' "Identify" panel which spare K860 key was pressed: it writes "&lt;cid&gt; &lt;utc ticks&gt;" to
/// spare-key.txt in the state folder (atomic replace); Settings polls it while the panel is open and counts a press when the
/// ticks are newer than the moment the panel opened.
/// </summary>
public static class SpareKeyReport
{
    public const string FileName = "spare-key.txt";

    public static string PathIn(string stateDirectory) => Path.Combine(stateDirectory, FileName);

    public static string Format(ushort cid, DateTimeOffset at) => $"{cid} {at.UtcTicks}";

    /// <summary>Parses "&lt;cid&gt; &lt;ticks&gt;"; false for anything else.</summary>
    public static bool TryParse(string? text, out ushort cid, out DateTimeOffset at)
    {
        cid = 0;
        at = default;
        var parts = text?.Trim().Split(' ');
        if (parts is not { Length: 2 } || !ushort.TryParse(parts[0], out cid) || !long.TryParse(parts[1], out long ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return false;
        at = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    public static void Write(string stateDirectory, ushort cid, ILog? log = null)
    {
        try
        {
            Directory.CreateDirectory(stateDirectory);
            string path = PathIn(stateDirectory), temp = path + ".tmp";
            File.WriteAllText(temp, Format(cid, DateTimeOffset.UtcNow));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Error("spare key report failed", e);
        }
    }

    /// <summary>The last reported press, or null when there is none or the file is unreadable.</summary>
    public static (ushort Cid, DateTimeOffset At)? Read(string stateDirectory)
    {
        try
        {
            return TryParse(File.ReadAllText(PathIn(stateDirectory)), out ushort cid, out DateTimeOffset at) ? (cid, at) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
