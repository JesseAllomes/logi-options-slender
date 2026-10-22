using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.Tray;

/// <summary>
/// Remove device (m53), UI thread: forgets the device in the registry, and only if that took, backs up the config file and resets
/// that device's Slender settings through the tray's normal save path. A failed backup leaves the settings alone. Windows
/// pairing is never touched.
/// </summary>
public sealed class DeviceForgetter(
    Func<string, bool> forget,
    string? configPath,
    Func<SlenderConfig> current,
    Func<SlenderConfig, bool> apply,
    ILog log,
    Func<DateTime>? now = null)
{
    public const int MaxBackups = 5;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);

    /// <summary>wParam of the forget message: index into DeviceCatalog.All; out of range is ignored.</summary>
    public void Forget(int index)
    {
        if (index < 0 || index >= DeviceCatalog.All.Count) return;
        string id = DeviceCatalog.All[index].Id;
        if (!forget(id)) return;

        if (!Backup(id)) return;
        if (apply(DeviceSettingsReset.Forget(current(), id))) log.Info($"removed device {id}: its settings were reset (config backed up first)");
    }

    private bool Backup(string id)
    {
        if (configPath is null)
        {
            log.Error($"remove device {id}: no config file to back up; settings kept");
            return false;
        }

        try
        {
            if (!File.Exists(configPath)) return true; // nothing saved yet: the settings are already the defaults
            string dir = Path.GetDirectoryName(configPath) ?? string.Empty;
            string prefix = $"config.before-remove-{id}-";
            File.Copy(configPath, Path.Combine(dir, $"{prefix}{_now():yyyyMMdd-HHmmss}.json"), overwrite: true);
            // The timestamp sorts as text, so the oldest come first.
            string[] old = Directory.GetFiles(dir, prefix + "*.json");
            Array.Sort(old, StringComparer.Ordinal);
            foreach (string f in old.Take(Math.Max(0, old.Length - MaxBackups)))
            {
                try { File.Delete(f); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.Error("could not delete an old config backup", e); }
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error($"remove device {id}: config backup failed; settings kept", e);
            return false;
        }
    }
}
