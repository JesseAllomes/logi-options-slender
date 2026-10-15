using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Words for the General page version row, from what the tray published in status.json.</summary>
public static class UpdateText
{
    /// <summary>
    /// "Version 1.1.0 · up to date", "Version 1.1.0 · 1.1.1 ready" or "Version 1.1.0 · 1.1.1 downloading".
    /// Uses ownVersion when the tray published no current version (tray not running, older tray).
    /// </summary>
    public static string Version(SlenderStatus? status, string ownVersion)
    {
        string current = status?.CurrentVersion is { Length: > 0 } v ? v : ownVersion;
        string state = status?.AvailableVersion is not { Length: > 0 } available ? "up to date"
            : status.UpdateReady ? available + " ready"
            : available + " downloading";
        return $"Version {current} · {state}";
    }
}
