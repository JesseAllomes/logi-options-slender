using System.Globalization;
using System.Runtime.InteropServices;
using Slender.Engine.Native;

namespace Slender.Engine.Devices;

/// <summary>
/// HID interface arrival/removal from the Windows device notification (CM_Register_Notification): no window, no polling, no
/// thread of ours. Snapshot lists interface paths via SetupDi and never opens a device.
/// </summary>
public sealed unsafe class WindowsHidPresence : IDevicePresenceSource
{
    private readonly object _gate = new();
    private GCHandle _self;
    private nint _notify;
    private bool _disposed;

    public event EventHandler? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _notify != 0) return;
            var self = GCHandle.Alloc(this);
            var filter = new NativeMethods.CM_NOTIFY_FILTER
            {
                cbSize = (uint)sizeof(NativeMethods.CM_NOTIFY_FILTER),
                FilterType = NativeMethods.CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
                ClassGuid = NativeMethods.GUID_DEVINTERFACE_HID,
            };
            var cr = NativeMethods.CM_Register_Notification(&filter, GCHandle.ToIntPtr(self), &OnNotify, out var h);
            if (cr != 0)
            {
                self.Free();
                throw new InvalidOperationException($"CM_Register_Notification failed (0x{cr:X})");
            }
            _self = self;
            _notify = h;
        }
    }

    public void Dispose()
    {
        nint h;
        GCHandle self;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            h = _notify; self = _self;
            _notify = 0; _self = default;
        }
        // Unregister waits for a running callback, so it must not hold _gate or run on the callback thread.
        if (h != 0) NativeMethods.CM_Unregister_Notification(h);
        if (self.IsAllocated) self.Free();
    }

    [UnmanagedCallersOnly]
    private static uint OnNotify(nint hNotify, nint context, uint action, nint eventData, uint eventDataSize)
    {
        try
        {
            if (action != NativeMethods.CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL
                && action != NativeMethods.CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL) return 0;
            if (GCHandle.FromIntPtr(context).Target is WindowsHidPresence p) p.Changed?.Invoke(p, EventArgs.Empty);
        }
        catch { /* never throw into native code */ }
        return 0;
    }

    public IReadOnlySet<HidDeviceKey> Snapshot()
    {
        var keys = new HashSet<HidDeviceKey>();
        var guid = NativeMethods.GUID_DEVINTERFACE_HID;
        var set = NativeMethods.SetupDiGetClassDevs(ref guid, 0, 0, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set == 0 || set == -1) return keys;
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new NativeMethods.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)sizeof(NativeMethods.SP_DEVICE_INTERFACE_DATA) };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref data)) break;
                var path = GetPath(set, ref data);
                if (path != null && ParseKey(path) is { } key) keys.Add(key);
            }
        }
        finally { NativeMethods.SetupDiDestroyDeviceInfoList(set); }
        return keys;
    }

    private static string? GetPath(nint set, ref NativeMethods.SP_DEVICE_INTERFACE_DATA data)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out uint required, 0);
        if (required < 6) return null;
        var buf = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize (8 on x64, 6 on x86), then the WCHAR path.
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, buf, required, out _, 0)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// Vendor/product from a HID interface path. USB: "hid#vid_6993&amp;pid_b0ae&amp;mi_03...". Bluetooth LE (HOGP):
    /// "hid#{00001812-...}_dev_vid&amp;02046d_pid&amp;b042_rev&amp;...", where the two digits after "vid&amp;" are the vendor-id
    /// source (02 = USB-IF) and the vendor is the next four. Null for anything else.
    /// </summary>
    public static HidDeviceKey? ParseKey(string interfacePath)
    {
        if (string.IsNullOrEmpty(interfacePath)) return null;
        var p = interfacePath.ToLowerInvariant();

        int v = p.IndexOf("vid_", StringComparison.Ordinal);
        if (v >= 0)
        {
            int d = p.IndexOf("pid_", StringComparison.Ordinal);
            if (Hex4(p, v + 4, out var vid) && d >= 0 && Hex4(p, d + 4, out var pid)) return new HidDeviceKey(vid, pid);
            return null;
        }

        v = p.IndexOf("_dev_vid&", StringComparison.Ordinal);
        if (v >= 0)
        {
            int d = p.IndexOf("_pid&", v, StringComparison.Ordinal);
            if (Hex4(p, v + 9 + 2, out var vid) && d >= 0 && Hex4(p, d + 5, out var pid)) return new HidDeviceKey(vid, pid);
        }
        return null;
    }

    private static bool Hex4(string s, int start, out ushort value)
    {
        value = 0;
        if (start < 0 || start + 4 > s.Length) return false;
        return ushort.TryParse(s.AsSpan(start, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
