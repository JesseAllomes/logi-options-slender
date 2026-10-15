namespace Slender.Hidpp.Inventory;

public sealed class CallRecord
{
    public string Getter { get; set; } = "";
    public int Function { get; set; }
    public string RequestHex { get; set; } = "";
    /// <summary>ok, hid-error, timeout, or skipped (budget exhausted).</summary>
    public string Status { get; set; } = "ok";
    public int? ErrorCode { get; set; }
    public string? ReplyHex { get; set; }
    public Dictionary<string, object?>? Decoded { get; set; }
}

public sealed class FeatureRecord
{
    public int Index { get; set; }
    public ushort Id { get; set; }
    public string IdHex => $"0x{Id:X4}";
    public string Name { get; set; } = InventoryNames.Unknown;
    public int? Version { get; set; }
    public int TypeFlagsRaw { get; set; }
    public List<string> Flags { get; set; } = [];
    public bool Called { get; set; }
    /// <summary>"called" or "listed-only".</summary>
    public string Disposition { get; set; } = "listed-only";
    public string Reason { get; set; } = "";
    public string? OptionsPlusNote { get; set; }
    public List<CallRecord> Calls { get; set; } = [];
}

public sealed class FirmwareEntity
{
    public int Entity { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Build { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class CidRecord
{
    public ushort Cid { get; set; }
    public string CidHex => $"0x{Cid:X4}";
    public int Task { get; set; }
    public int Flags { get; set; }
    public List<string> FlagNames { get; set; } = [];
    public int Position { get; set; }
    public int Group { get; set; }
    public int GroupMask { get; set; }
    public string? Name { get; set; }
    public string? OptionsPlusLabel { get; set; }
    public Dictionary<string, object?>? Reporting { get; set; }
}

public sealed class DeviceInventory
{
    public string Device { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public int SwId { get; set; }
    public int DeviceIndex { get; set; }
    public string? HidCollection { get; set; }
    public int? ProductId { get; set; }
    public string? ProtocolVersion { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
    public string? FriendlyName { get; set; }
    public int FeatureCount { get; set; }
    public int RequestsSent { get; set; }
    public int ErrorCount { get; set; }
    public string? Aborted { get; set; }
    public List<FirmwareEntity> Firmware { get; set; } = [];
    public List<FeatureRecord> Features { get; set; } = [];
    public List<CidRecord> Cids { get; set; } = [];
    public List<string> OptionsPlusSlots { get; set; } = [];
}
