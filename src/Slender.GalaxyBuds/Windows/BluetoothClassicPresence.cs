using System.Globalization;
using System.Runtime.InteropServices;
using Slender.Engine.Devices;

namespace Slender.GalaxyBuds.Windows;

/// <summary>
/// Paired Bluetooth Classic devices as Windows lists them (BTHENUM device ids such as ..._VID&amp;00010075_PID&amp;A013\...&amp;ADDRESS_C00000000),
/// reported as HidDeviceKey(vendor, product) so the registry can match them like HID devices. Arrival and removal come from
/// CM_Register_Notification on the Bluetooth device interface (no polling); Snapshot only reads the id list and opens nothing.
/// </summary>
public sealed partial class BluetoothClassicPresence : IDevicePresenceSource
{
    private const uint FilterEnumerator = 0x1;
    private const uint FilterPresent = 0x100;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_List_Size(out uint length, string filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint CM_Get_Device_ID_List(string filter, char* buffer, uint length, uint flags);

    private readonly object _gate = new();
    private DeviceInterfaceWatcher? _watcher;
    private bool _disposed;

    public event EventHandler? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null) return;
            _watcher = new DeviceInterfaceWatcher(DeviceInterfaceWatcher.BthPortDevice, () => Changed?.Invoke(this, EventArgs.Empty), arrivalsOnly: false);
        }
    }

    public void Dispose()
    {
        DeviceInterfaceWatcher? w;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            w = _watcher;
            _watcher = null;
        }

        w?.Dispose();
    }

    public IReadOnlySet<HidDeviceKey> Snapshot()
    {
        var keys = new HashSet<HidDeviceKey>();
        foreach (var id in ListIds())
            if (ParseKey(id) is { } k) keys.Add(k);
        return keys;
    }

    /// <summary>Addresses of every paired device with this Bluetooth vendor and product id.</summary>
    public static IReadOnlyList<ulong> FindAddresses(ushort vendorId, ushort productId)
    {
        var found = new List<ulong>();
        foreach (var id in ListIds())
            if (ParseKey(id) == new HidDeviceKey(vendorId, productId) && ParseAddress(id) is { } a && !found.Contains(a)) found.Add(a);
        return found;
    }

    private static unsafe List<string> ListIds()
    {
        var ids = new List<string>();
        const uint flags = FilterEnumerator | FilterPresent;
        if (CM_Get_Device_ID_List_Size(out uint len, "BTHENUM", flags) != 0 || len < 2) return ids;
        var buf = new char[len];
        fixed (char* p = buf)
        {
            if (CM_Get_Device_ID_List("BTHENUM", p, len, flags) != 0) return ids;
        }

        int start = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            if (buf[i] != '\0') continue;
            if (i > start) ids.Add(new string(buf, start, i - start));
            start = i + 1;
        }

        return ids;
    }

    /// <summary>Vendor and product from a BTHENUM id ("..._VID&amp;00010075_PID&amp;A013\..."): the Bluetooth SIG vendor source (0001) then the 16-bit vendor id. Null otherwise.</summary>
    public static HidDeviceKey? ParseKey(string instanceId)
    {
        const string vid = "_VID&0001";
        int v = instanceId.IndexOf(vid, StringComparison.OrdinalIgnoreCase);
        if (v < 0) return null;
        int d = instanceId.IndexOf("_PID&", v, StringComparison.OrdinalIgnoreCase);
        if (d < 0) return null;
        if (!Hex4(instanceId, v + vid.Length, out ushort vendor) || !Hex4(instanceId, d + 5, out ushort product)) return null;
        return new HidDeviceKey(vendor, product);
    }

    /// <summary>The Bluetooth address: the 12 hex digits after the last '&amp;' of the instance part ("...&amp;0&amp;AABBCCDDEEFF_C00000000").</summary>
    public static ulong? ParseAddress(string instanceId)
    {
        int slash = instanceId.LastIndexOf('\\');
        int amp = instanceId.LastIndexOf('&');
        if (slash < 0 || amp < slash || amp + 13 > instanceId.Length) return null;
        return ulong.TryParse(instanceId.AsSpan(amp + 1, 12), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var a) && a != 0 ? a : null;
    }

    private static bool Hex4(string s, int start, out ushort value)
    {
        value = 0;
        return start + 4 <= s.Length && ushort.TryParse(s.AsSpan(start, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
