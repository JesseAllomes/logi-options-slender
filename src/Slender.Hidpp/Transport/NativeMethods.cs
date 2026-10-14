using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Slender.Hidpp.Transport;

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA
{
    public uint cbSize;
    public Guid InterfaceClassGuid;
    public uint Flags;
    public nuint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HIDD_ATTRIBUTES
{
    public uint Size;
    public ushort VendorID;
    public ushort ProductID;
    public ushort VersionNumber;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HIDP_CAPS
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;
    public fixed ushort Reserved[17];
    public ushort NumberLinkCollectionNodes;
    public ushort NumberInputButtonCaps;
    public ushort NumberInputValueCaps;
    public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps;
    public ushort NumberOutputValueCaps;
    public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps;
    public ushort NumberFeatureValueCaps;
    public ushort NumberFeatureDataIndices;
}

/// <summary>SetupDi device-info set; destroyed on release.</summary>
internal sealed partial class DeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DeviceInfoSetHandle() : base(true) { }

    protected override bool ReleaseHandle() => NativeMethods.SetupDiDestroyDeviceInfoList(handle);
}

/// <summary>HID preparsed data; freed on release.</summary>
internal sealed partial class PreparsedDataHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public PreparsedDataHandle() : base(true) { }

    protected override bool ReleaseHandle() => NativeMethods.HidD_FreePreparsedData(handle);
}

internal static unsafe partial class NativeMethods
{
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1;
    public const uint FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const int ERROR_IO_PENDING = 997;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    [LibraryImport("hid.dll")]
    public static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle device, out PreparsedDataHandle preparsed);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_FreePreparsedData(IntPtr preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(PreparsedDataHandle preparsed, out HIDP_CAPS caps);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    public static partial DeviceInfoSetHandle SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(DeviceInfoSetHandle deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetail(DeviceInfoSetHandle deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr detailData, uint detailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security,
        uint creationDisposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(SafeFileHandle file, byte* buffer, uint bytesToRead, uint* bytesRead, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(SafeFileHandle file, byte* buffer, uint bytesToWrite, uint* bytesWritten, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOverlappedResult(SafeFileHandle file, NativeOverlapped* overlapped, out uint transferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CancelIoEx(SafeFileHandle file, NativeOverlapped* overlapped);
}
