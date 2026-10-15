using System.Text;
using Slender.Engine;

namespace Slender.Settings.Services;

/// <summary>Appends unexpected errors to %LOCALAPPDATA%\Slender\logs\settings.log. Never throws.</summary>
public static class CrashLog
{
    public static void Write(Exception e)
    {
        try
        {
            Directory.CreateDirectory(SlenderPaths.LogsDirectory);
            string path = Path.Combine(SlenderPaths.LogsDirectory, "settings.log");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 512 * 1024)
            {
                File.Move(path, path + ".1", overwrite: true);
            }

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ERROR {e}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}
