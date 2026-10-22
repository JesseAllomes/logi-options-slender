namespace Slender.Engine.Devices;

/// <summary>
/// How a supported device shows in the app (user 2026-10-22). Hidden: never installed on this computer. Active: present and
/// talking. Sleeping: present, link down for less than DeviceRegistry.SleepGrace (30 min). Disconnected: installed but gone
/// longer than that, or reported gone by its module (switched to another host); shown greyed with Remove device.
/// </summary>
public enum DevicePresence { Hidden, Active, Sleeping, Disconnected }

/// <summary>One HID interface as Windows lists it: vendor and product id only (parsed from the interface path).</summary>
public readonly record struct HidDeviceKey(ushort VendorId, ushort ProductId);

/// <summary>
/// A device Slender supports. Id is the stable key used in config and status.json ("mx-master-4"). Any of ProductIds under VendorId
/// counts as this device. ReportsLink: the module tells the registry when its link goes up or down (a Bluetooth device whose HID
/// interfaces stay listed while it sleeps); when false the interface being listed is the link (a USB device).
/// </summary>
public sealed record SupportedDevice(string Id, string Name, ushort VendorId, IReadOnlyList<ushort> ProductIds, bool ReportsLink)
{
    public bool Matches(HidDeviceKey key) => key.VendorId == VendorId && ProductIds.Contains(key.ProductId);
}

/// <summary>
/// Windows HID interface arrival and removal, pushed by the system (no polling). Changed fires on any arrival or removal, on a
/// system thread; the registry then reads Snapshot. Start registers for notifications; Dispose unregisters.
/// </summary>
public interface IDevicePresenceSource : IDisposable
{
    event EventHandler? Changed;

    void Start();

    /// <summary>Vendor/product of every HID interface listed now (duplicates folded).</summary>
    IReadOnlySet<HidDeviceKey> Snapshot();
}

/// <summary>
/// One device's code inside the tray. The registry calls Start when the device becomes present (Active or Sleeping) and Stop when
/// it becomes Disconnected or the tray exits; both on the registry's thread, never concurrently, and each at most once per
/// transition. A stopped module holds no threads, handles, timers or buffers. An exception from Start or Stop, or a ReportFault
/// call, disables that module only (logged) until the tray restarts. Wake is a hint that the device's interfaces just arrived:
/// a module waiting to reconnect should try now.
/// </summary>
public interface IDeviceModule
{
    string DeviceId { get; }

    void Start();

    void Stop();

    void Wake();
}

/// <summary>A row of the device list: what the registry knows about one supported device.</summary>
public sealed record DeviceEntry(string Id, string Name, DevicePresence Presence, bool Faulted, DateTimeOffset? LastSeen);
