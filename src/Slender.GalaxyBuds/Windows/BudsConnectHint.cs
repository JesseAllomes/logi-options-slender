using Slender.Engine.Devices;

namespace Slender.GalaxyBuds.Windows;

/// <summary>
/// Windows pushes that suggest the buds just connected: a Bluetooth device arriving, or an audio interface arriving (the buds' music
/// and call endpoints appear when they connect). Registered only while the module runs; each is one CM notification, no thread.
/// The module only acts on it while its link is down, so unrelated arrivals cost one lock and a check.
/// </summary>
public static class BudsConnectHint
{
    public static IDisposable? Create(Action hint)
    {
        var made = new List<DeviceInterfaceWatcher>(2);
        try
        {
            made.Add(new DeviceInterfaceWatcher(DeviceInterfaceWatcher.BthPortDevice, hint, arrivalsOnly: true));
            made.Add(new DeviceInterfaceWatcher(DeviceInterfaceWatcher.KsAudio, hint, arrivalsOnly: true));
        }
        catch
        {
            foreach (var w in made) w.Dispose();
            return null;
        }

        return new Group(made);
    }

    /// <summary>The paired buds' Bluetooth address, or null when none is paired.</summary>
    public static ulong? PairedAddress() =>
        BluetoothClassicPresence.FindAddresses(DeviceCatalog.SamsungBluetoothVendorId, DeviceCatalog.GalaxyBuds4ProProductId) is { Count: > 0 } l ? l[0] : null;

    private sealed class Group(List<DeviceInterfaceWatcher> watchers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var w in watchers) w.Dispose();
        }
    }
}
