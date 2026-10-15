using System.Diagnostics;

namespace Slender.Tray;

/// <summary>
/// Starts Slender.Settings.exe from the tray's own folder. Settings is single-instance: it owns the named event below, and a
/// running copy is brought to the front by signalling it instead of starting another.
/// </summary>
public static class SettingsLauncher
{
    public const string ExeName = "Slender.Settings.exe";
    public const string ShowEventName = @"Local\Slender.Settings.Show";

    /// <summary>Signals a running settings window to come to the front. False when none is running.</summary>
    public static bool TrySignalRunning()
    {
        if (!EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? handle))
        {
            return false;
        }

        using (handle)
        {
            return handle.Set();
        }
    }

    /// <summary>Resolves the settings exe next to this app; in a dev tree, falls back to the Settings project output.</summary>
    public static string? ResolveExe(string? baseDirectory = null)
    {
        string dir = baseDirectory ?? AppContext.BaseDirectory;
        string beside = Path.Combine(dir, ExeName);
        if (File.Exists(beside))
        {
            return beside;
        }

        // Dev tree: ...\src\Slender.Tray\bin\<cfg>\<tfm>\ -> ...\src\Slender.Settings\bin\<cfg>\net10.0-windows10.0.19041.0\win-x64\
        string cfg = new DirectoryInfo(dir.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Name ?? "Debug";
        DirectoryInfo? srcRoot = new DirectoryInfo(dir).Parent?.Parent?.Parent?.Parent;
        if (srcRoot is not null)
        {
            string dev = Path.Combine(srcRoot.FullName, "Slender.Settings", "bin", cfg, "net10.0-windows10.0.19041.0", "win-x64", ExeName);
            if (File.Exists(dev))
            {
                return dev;
            }
        }

        return null;
    }

    public const string PopoverArgument = "--popover";

    /// <summary>How the settings exe is started; the popover flag is only passed for the tray-icon click.</summary>
    public static ProcessStartInfo StartInfo(string exe, bool popover)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        if (popover)
        {
            info.ArgumentList.Add(PopoverArgument);
        }

        return info;
    }

    /// <summary>Brings the running window forward or starts a new one (with --popover for the tray-icon click). Returns false when the exe cannot be found or started.</summary>
    public static bool Launch(bool popover = false)
    {
        if (TrySignalRunning())
        {
            return true;
        }

        string? exe = ResolveExe();
        if (exe is null)
        {
            return false;
        }

        try
        {
            using Process? p = Process.Start(StartInfo(exe, popover));
            return p is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
