using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Slender.Hidpp.Transport;

/// <summary>One enumerated HID collection (interface) with its capabilities.</summary>
public sealed record HidCollectionInfo(
    string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    ushort InputLength, ushort OutputLength, ushort FeatureLength);

/// <summary>SetupDi enumeration of HID interfaces; selects the MX Master 4 HID++ collection (docs/protocol.md, Transport).</summary>
public sealed class WindowsHidTransportFactory : IHidppTransportFactory
{
    public const ushort LogitechVendorId = 0x046D;
    public const ushort HidppUsagePage = 0xFF43;
    public const ushort HidppUsage = 0x0202;
    public static readonly ushort[] MxMaster4ProductIds = [0xB042, 0xB048];
    /// <summary>ERGO K860 over Bluetooth LE (confirmed on device 2026-10-15).</summary>
    public static readonly ushort[] ErgoK860ProductIds = [0xB359];

    private readonly ushort[] _productIds;

    /// <summary>Selects the MX Master 4 by default; pass <see cref="ErgoK860ProductIds"/> for the keyboard.</summary>
    public WindowsHidTransportFactory(ushort[]? productIds = null) { _productIds = productIds ?? MxMaster4ProductIds; }

    /// <summary>Lists HID collections for a vendor (all vendors when null). Opened with zero access, so it never disturbs the OS.</summary>
    public static IReadOnlyList<HidCollectionInfo> Enumerate(ushort? vendorId = LogitechVendorId)
    {
        var result = new List<HidCollectionInfo>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        using var set = NativeMethods.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set.IsInvalid) return result;

        for (uint i = 0; ; i++)
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data)) break;
            var path = GetInterfacePath(set, ref data);
            if (path == null) continue;
            var info = Query(path, vendorId);
            if (info != null) result.Add(info);
        }
        return result;
    }

    private static string? GetInterfacePath(DeviceInfoSetHandle set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
        if (required < 6) return null;
        IntPtr buf = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize (8 on x64, 6 on x86), then the WCHAR path.
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, buf, required, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static HidCollectionInfo? Query(string path, ushort? vendorId)
    {
        using var h = NativeMethods.CreateFile(path, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;

        var attrs = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!NativeMethods.HidD_GetAttributes(h, ref attrs)) return null;
        if (vendorId is { } v && attrs.VendorID != v) return null;

        if (!NativeMethods.HidD_GetPreparsedData(h, out var pre)) return null;
        using (pre)
        {
            if (NativeMethods.HidP_GetCaps(pre, out var caps) != NativeMethods.HIDP_STATUS_SUCCESS) return null;
            return new HidCollectionInfo(path, attrs.VendorID, attrs.ProductID, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
        }
    }

    /// <summary>Ordered candidates: exact usage (0xFF43/0x0202) first, then any vendor page &gt;= 0xFF00, on a matching PID.</summary>
    public static IEnumerable<HidCollectionInfo> SelectCandidates(IEnumerable<HidCollectionInfo> all, ushort[]? productIds = null)
    {
        var mine = all.Where(c => c.VendorId == LogitechVendorId && (productIds ?? MxMaster4ProductIds).Contains(c.ProductId)).ToList();
        foreach (var c in mine.Where(c => c.UsagePage == HidppUsagePage && c.Usage == HidppUsage)) yield return c;
        foreach (var c in mine.Where(c => c.UsagePage >= 0xFF00 && !(c.UsagePage == HidppUsagePage && c.Usage == HidppUsage))) yield return c;
    }

    public IHidppTransport? TryOpen()
    {
        foreach (var c in SelectCandidates(Enumerate(), _productIds))
        {
            var h = NativeMethods.CreateFile(c.Path, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid)
            {
                h.Dispose();
                continue;
            }
            return new WindowsHidTransport(h, c.InputLength, c.OutputLength);
        }
        return null;
    }
}
