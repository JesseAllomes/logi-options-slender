using System.Diagnostics;

namespace Slender.Settings.Services;

/// <summary>An app that has a visible window right now (or an exe picked from disk).</summary>
public sealed record RunningApp(string Process, string DisplayName, string? ExePath);

/// <summary>Lists apps with a window, for "Add app". Disposes every Process it touches.</summary>
public static class RunningAppsCatalog
{
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "slender.settings", "slender.tray", "applicationframehost", "textinputhost", "searchhost", "startmenuexperiencehost",
        "shellexperiencehost", "lockapp", "systemsettings", "widgets", "dwm", "idle", "logioptionsplus", "logioptionsplus_agent",
    };

    public static IReadOnlyList<RunningApp> List(IEnumerable<string?> alreadyAdded)
    {
        var skip = new HashSet<string>(alreadyAdded.Where(a => a is not null).Select(a => a!), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                string name = p.ProcessName;
                if (Hidden.Contains(name) || skip.Contains(name) || result.ContainsKey(name))
                {
                    continue;
                }

                if (p.MainWindowHandle == 0 || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                {
                    continue;
                }

                string? path = null;
                string? description = null;
                try
                {
                    path = p.MainModule?.FileName;
                    if (path is not null)
                    {
                        FileVersionInfo v = FileVersionInfo.GetVersionInfo(path);
                        description = string.IsNullOrWhiteSpace(v.FileDescription) ? v.ProductName : v.FileDescription;
                    }
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or IOException)
                {
                    // Elevated process: listed without a path (its icon cannot be read).
                }

                result[name] = new RunningApp(name.ToLowerInvariant(), Friendly(description, name), path);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process exited while we looked at it.
            }
            finally
            {
                p.Dispose();
            }
        }

        return result.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Display name for an exe picked from disk.</summary>
    public static RunningApp FromExe(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string? description = null;
        try
        {
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(path);
            description = string.IsNullOrWhiteSpace(v.FileDescription) ? v.ProductName : v.FileDescription;
        }
        catch (Exception e) when (e is IOException or FileNotFoundException)
        {
            // No version info: the file name is used.
        }

        return new RunningApp(name.ToLowerInvariant(), Friendly(description, name), path);
    }

    private static string Friendly(string? description, string process)
    {
        if (!string.IsNullOrWhiteSpace(description) && description.Length <= 28)
        {
            return description.Trim();
        }

        return process.Length == 0 ? process : char.ToUpperInvariant(process[0]) + process[1..];
    }
}
