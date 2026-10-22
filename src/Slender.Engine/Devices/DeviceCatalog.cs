using Slender.Hidpp.Transport;

namespace Slender.Engine.Devices;

/// <summary>The devices Slender supports. Adding a device is one entry here plus its module.</summary>
public static class DeviceCatalog
{
    public const string MxMaster4 = "mx-master-4";
    public const string ErgoK860 = "ergo-k860";
    public const string YealinkWh64 = "yealink-wh64";
    public const ushort YealinkVendorId = 0x6993;
    public const ushort YealinkWh64ProductId = 0xB0AE;

    public static IReadOnlyList<SupportedDevice> All { get; } =
    [
        new(MxMaster4, "MX Master 4", WindowsHidTransportFactory.LogitechVendorId, WindowsHidTransportFactory.MxMaster4ProductIds, ReportsLink: true),
        new(ErgoK860, "ERGO K860", WindowsHidTransportFactory.LogitechVendorId, WindowsHidTransportFactory.ErgoK860ProductIds, ReportsLink: true),
        // USB: the HID interface being listed is the link. The module lives in Slender.Yealink and is registered by the tray later.
        new(YealinkWh64, "Yealink WH64", YealinkVendorId, [YealinkWh64ProductId], ReportsLink: false),
    ];
}
