using System.Runtime.InteropServices;

namespace Slender.GalaxyBuds.Windows;

/// <summary>
/// Calls back when a device interface of one class arrives or is removed: CM_Register_Notification, pushed by Windows on its own
/// thread. No window, no thread or timer of ours; Dispose unregisters.
/// </summary>
public sealed unsafe partial class DeviceInterfaceWatcher : IDisposable
{
    /// <summary>Bluetooth devices known to the Windows Bluetooth stack (paired devices appear and disappear here).</summary>
    public static readonly Guid BthPortDevice = new("0850302A-B344-4FDA-9BE9-90576B8D46F0");

    /// <summary>Audio endpoints and filters; a Bluetooth headset connecting for music or calls adds some.</summary>
    public static readonly Guid KsAudio = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");

    private const uint FilterTypeDeviceInterface = 0;
    private const uint ActionArrival = 0;
    private const uint ActionRemoval = 1;

    [StructLayout(LayoutKind.Explicit, Size = 416)]
    private struct Filter
    {
        [FieldOffset(0)] public uint CbSize;
        [FieldOffset(4)] public uint Flags;
        [FieldOffset(8)] public uint FilterType;
        [FieldOffset(12)] public uint Reserved;
        [FieldOffset(16)] public Guid ClassGuid;
    }

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Register_Notification(Filter* filter, nint context,
        delegate* unmanaged<nint, nint, uint, nint, uint, uint> callback, out nint notifyContext);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Unregister_Notification(nint notifyContext);

    private readonly object _gate = new();
    private readonly Action _changed;
    private readonly bool _arrivalsOnly;
    private GCHandle _self;
    private nint _notify;
    private bool _disposed;

    public DeviceInterfaceWatcher(Guid interfaceClass, Action changed, bool arrivalsOnly)
    {
        _changed = changed;
        _arrivalsOnly = arrivalsOnly;
        var self = GCHandle.Alloc(this);
        var filter = new Filter { CbSize = (uint)sizeof(Filter), FilterType = FilterTypeDeviceInterface, ClassGuid = interfaceClass };
        uint cr = CM_Register_Notification(&filter, GCHandle.ToIntPtr(self), &OnNotify, out nint h);
        if (cr != 0)
        {
            self.Free();
            throw new InvalidOperationException($"CM_Register_Notification failed (0x{cr:X})");
        }

        _self = self;
        _notify = h;
    }

    public void Dispose()
    {
        nint h;
        GCHandle self;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            h = _notify;
            self = _self;
            _notify = 0;
            _self = default;
        }

        // Unregister waits for a running callback, so this must not hold _gate or run on the callback thread.
        if (h != 0) CM_Unregister_Notification(h);
        if (self.IsAllocated) self.Free();
    }

    [UnmanagedCallersOnly]
    private static uint OnNotify(nint hNotify, nint context, uint action, nint eventData, uint eventDataSize)
    {
        try
        {
            if (action != ActionArrival && action != ActionRemoval) return 0;
            if (GCHandle.FromIntPtr(context).Target is DeviceInterfaceWatcher w && !(w._arrivalsOnly && action != ActionArrival)) w._changed();
        }
        catch { /* never throw into native code */ }
        return 0;
    }
}
