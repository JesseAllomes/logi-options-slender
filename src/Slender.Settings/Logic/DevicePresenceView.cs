using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>
/// How Settings shows one device, from the tray's status. A registry-aware tray (any device has Presence) leaves never-installed
/// devices out of the list and says active / sleeping / disconnected; an older tray lists every device with Presence null and
/// the old Connected-based treatment applies unchanged.
/// </summary>
public static class DevicePresenceView
{
    public const string Active = "active";
    public const string Sleeping = "sleeping";
    public const string Disconnected = "disconnected";

    /// <summary>True when the status comes from a tray that reports presence.</summary>
    public static bool RegistryAware(SlenderStatus? status) => status is not null && status.Devices.Any(d => d.Presence is not null);

    /// <summary>False only for a device a registry-aware tray left out of the list (never installed).</summary>
    public static bool Visible(SlenderStatus? status, string id) => !RegistryAware(status) || status!.Devices.Any(d => d.Id == id);

    private static string? Presence(SlenderStatus? status, string id) =>
        RegistryAware(status) ? status!.Devices.FirstOrDefault(d => d.Id == id)?.Presence : null;

    /// <summary>Greyed (0.5 opacity): disconnected, or in the legacy case not connected (also when the tray is down).</summary>
    public static bool Dimmed(SlenderStatus? status, string id) => Presence(status, id) switch
    {
        Disconnected => true,
        Active or Sleeping => false,
        _ => status is null || status.Devices.FirstOrDefault(d => d.Id == id) is not { Connected: true },
    };

    /// <summary>Only a disconnected device can be removed.</summary>
    public static bool Removable(SlenderStatus? status, string id) => Presence(status, id) == Disconnected;

    /// <summary>Popover subtitle.</summary>
    public static string Subtitle(SlenderStatus? status, string id)
    {
        DeviceState? state = status?.Devices.FirstOrDefault(d => d.Id == id);
        return Presence(status, id) switch
        {
            Sleeping => "Asleep",
            Disconnected => "Disconnected",
            Active => "Connected",
            _ => status is null ? "Slender isn't running" : state is null ? "Not seen yet" : state.Connected ? "Connected" : "Not connected",
        };
    }

    /// <summary>Rail/popover battery slot when there is no level to show: "Asleep" while sleeping, otherwise the old words.</summary>
    public static string? BatteryWord(SlenderStatus? status, string id) => Presence(status, id) switch
    {
        Sleeping => "Asleep",
        Disconnected => "Off",
        _ => null,
    };

    /// <summary>Suffix for the accessible name, empty when the device is shown normally.</summary>
    public static string AccessibleSuffix(SlenderStatus? status, string id) => Presence(status, id) switch
    {
        Sleeping => ", asleep",
        Disconnected => ", disconnected",
        Active => string.Empty,
        _ => Dimmed(status, id) ? ", not connected" : string.Empty,
    };

    /// <summary>InfoBar title and message when the device needs one; null when it does not (tray down is handled by the caller).</summary>
    public static (string Title, string Message)? InfoBar(SlenderStatus? status, string id, string name, bool mouse)
    {
        switch (Presence(status, id))
        {
            case Sleeping:
                return ($"{name} is asleep", "Changes apply when it wakes.");
            case Disconnected:
                return ($"{name} isn't connected", "Turn it on or check Bluetooth. Your changes are saved and apply when it connects.");
            case Active:
                return null;
        }

        if (status is null || status.Devices.FirstOrDefault(d => d.Id == id) is { Connected: true })
        {
            return null;
        }

        return ($"{name} isn't connected", mouse
            ? "Turn it on or check Bluetooth. Your changes are saved and apply when it connects."
            : "Slender doesn't control this keyboard yet. Your changes are saved for when it does.");
    }
}
