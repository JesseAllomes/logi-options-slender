namespace Slender.Hidpp.Inventory;

/// <summary>
/// The type byte returned by IFeatureSet getFeatureId (0x0001 fn1, reply byte 2).
/// Bit layout, from Logitech's published HID++ 2.0 x0001 IFeatureSet spec and Solaar hidpp20.py FEATURE_FLAG
/// (internal 0x20, hidden 0x40, obsolete 0x80):
///   bit 7 obsolete, bit 6 hidden, bit 5 engineering, bit 4 manufacturing-deactivatable, bit 3 compliance-deactivatable.
/// </summary>
public readonly record struct FeatureTypeFlags(byte Raw)
{
    public bool Obsolete => (Raw & 0x80) != 0;
    public bool Hidden => (Raw & 0x40) != 0;
    public bool Engineering => (Raw & 0x20) != 0;
    public bool ManufacturingDeactivatable => (Raw & 0x10) != 0;
    public bool ComplianceDeactivatable => (Raw & 0x08) != 0;

    /// <summary>Engineering or manufacturing features are listed only, never queried.</summary>
    public bool EngineeringOrManufacturing => Engineering || ManufacturingDeactivatable;

    public IReadOnlyList<string> Names()
    {
        var l = new List<string>();
        if (Obsolete) l.Add("obsolete");
        if (Hidden) l.Add("hidden");
        if (Engineering) l.Add("engineering");
        if (ManufacturingDeactivatable) l.Add("manufacturing-deactivatable");
        if (ComplianceDeactivatable) l.Add("compliance-deactivatable");
        return l;
    }
}
