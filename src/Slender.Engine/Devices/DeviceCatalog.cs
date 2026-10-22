using Slender.Hidpp.Transport;

namespace Slender.Engine.Devices;

/// <summary>The devices Slender supports. Adding a device is one entry here plus its module.</summary>
public static class DeviceCatalog
{
    public const string MxMaster4 = "mx-master-4";
    public const string ErgoK860 = "ergo-k860";

    public static IReadOnlyList<SupportedDevice> All { get; } =
    [
        new(MxMaster4, "MX Master 4", WindowsHidTransportFactory.LogitechVendorId, WindowsHidTransportFactory.MxMaster4ProductIds, ReportsLink: true),
        new(ErgoK860, "ERGO K860", WindowsHidTransportFactory.LogitechVendorId, WindowsHidTransportFactory.ErgoK860ProductIds, ReportsLink: true),
    ];
}
