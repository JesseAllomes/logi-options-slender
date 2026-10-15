using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slender.Engine.Config;

/// <summary>
/// Reads and writes status.json (SlenderStatus). The tray writes it with an atomic replace; the settings app reads it.
/// Reads tolerate a missing, locked or half-written file by returning null.
/// </summary>
public static class StatusJson
{
    public static string Serialize(SlenderStatus status) => JsonSerializer.Serialize(status, StatusJsonContext.Default.SlenderStatus);

    /// <summary>Parses status text; null when it is empty or malformed.</summary>
    public static SlenderStatus? Parse(string text)
    {
        try
        {
            SlenderStatus? status = JsonSerializer.Deserialize(text, StatusJsonContext.Default.SlenderStatus);
            return status is { Devices: not null } ? status : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes through a temp file and a move, so a reader never sees a partial file.</summary>
    public static void WriteAtomic(string path, SlenderStatus status)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, Serialize(status), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    /// <summary>Reads status.json; null when missing, unreadable or malformed.</summary>
    public static SlenderStatus? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
