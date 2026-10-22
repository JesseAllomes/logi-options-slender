using Microsoft.Win32;

namespace Slender.Settings.Services;

/// <summary>Reads the uninstall entries (display name, install location) the way Apps and Features lists them. Read only.</summary>
public static class InstalledPrograms
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IEnumerable<(string? Name, string? Location)> Entries()
    {
        foreach ((RegistryHive hive, RegistryView view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        })
        {
            RegistryKey? root = null;
            string[] names = [];
            try
            {
                root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(Uninstall);
                names = root?.GetSubKeyNames() ?? [];
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }

            using (root)
            {
                foreach (string name in names)
                {
                    string? display = null, location = null;
                    try
                    {
                        using RegistryKey? key = root!.OpenSubKey(name);
                        display = key?.GetValue("DisplayName") as string;
                        location = key?.GetValue("InstallLocation") as string;
                    }
                    catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                    {
                    }

                    if (display is not null)
                    {
                        yield return (display, location);
                    }
                }
            }
        }
    }
}
