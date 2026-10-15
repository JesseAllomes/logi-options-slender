using System.Text;

namespace Slender.Hidpp.Inventory;

public enum IterationKind
{
    /// <summary>One call per entry in <see cref="InventoryGetter.ParamSets"/>.</summary>
    None,
    /// <summary>Calls with parameter [i] + tail for i = 0..count-1, count from byte CountByte of the first reply of SourceFunction.</summary>
    CountedIndex,
    /// <summary>Calls with parameter [offset] + tail for offset = 0, Step, 2*Step... below the count.</summary>
    CountedOffset,
    /// <summary>Calls with parameter [cidHi, cidLo] + tail for every CID (reply bytes 0-1) returned by SourceFunction.</summary>
    PerCid,
}

public sealed record InventoryIteration(IterationKind Kind, byte SourceFunction, int CountByte = 0, int Step = 1, int Max = 64, byte[]? Tail = null);

public delegate Dictionary<string, object?> InventoryDecoder(byte[] request, byte[] reply);

/// <summary>One vetted read-only function. <see cref="Source"/> cites where the getter is documented.</summary>
public sealed record InventoryGetter(
    ushort FeatureId, byte Function, string Name, string Source, string Basis,
    InventoryDecoder Decode, IReadOnlyList<byte[]>? ParamSets = null, InventoryIteration? Iteration = null, bool Core = false)
{
    public IReadOnlyList<byte[]> Params => ParamSets ?? [[]];
}

public sealed class InventoryBlockedException(ushort featureId, byte function, string why)
    : Exception($"Inventory request blocked: feature 0x{featureId:X4} fn {function}: {why}")
{
    public ushort FeatureId { get; } = featureId;
    public byte Function { get; } = function;
}

/// <summary>
/// The only functions the inventory may send (<see cref="Table"/>), and the features it must never touch
/// (<see cref="NeverCall"/>). <see cref="EnsureAllowed"/> runs before every request.
/// </summary>
public static class InventoryGetters
{
    /// <summary>
    /// Features whose functions are never called, whatever the device reports: firmware update, reset, GPIO, OOB, config
    /// properties, host change/hosts info, hidden-feature enabler and the 0x1E engineering block, firmware properties, LED software
    /// control, onboard profiles and mouse button spy. They are listed with id, version, flags and name only.
    /// </summary>
    public static readonly IReadOnlySet<ushort> NeverCall = BuildNeverCall();

    private static HashSet<ushort> BuildNeverCall()
    {
        var s = new HashSet<ushort>
        {
            0x00C2, 0x00C3, 0x00D0, 0x00D1, // DFU control / DFU
            0x1802, // device reset
            0x1803, // GPIO access
            0x1805, // OOB state
            0x1806, // config device props
            0x1814, 0x1815, // change host / hosts info
            0x1E00, // enable hidden features
            0x1F1F, // firmware properties
            0x18A1, // LED software control
            0x8100, 0x8110, // onboard profiles / mouse button spy
        };
        for (ushort id = 0x1E01; id <= 0x1E22; id++) s.Add(id);
        return s;
    }

    private static readonly InventoryGetter[] Entries = BuildTable();

    public static IReadOnlyList<InventoryGetter> Table => Entries;

    public static IEnumerable<InventoryGetter> For(ushort featureId) => Entries.Where(g => g.FeatureId == featureId);

    public static bool IsInTable(ushort featureId, byte function) => Entries.Any(g => g.FeatureId == featureId && g.Function == function);

    /// <summary>Throws unless (feature, function) is in <see cref="Table"/>, the feature is not in <see cref="NeverCall"/> and is not flagged engineering/manufacturing.</summary>
    public static void EnsureAllowed(ushort featureId, byte function, FeatureTypeFlags flags = default)
    {
        if (NeverCall.Contains(featureId)) throw new InventoryBlockedException(featureId, function, "feature is on the never-call list");
        if (flags.EngineeringOrManufacturing) throw new InventoryBlockedException(featureId, function, "feature is flagged engineering/manufacturing");
        if (!IsInTable(featureId, function)) throw new InventoryBlockedException(featureId, function, "function is not in the vetted getter table");
    }

    // ------------------------------------------------------------------ decoding helpers

    private static Dictionary<string, object?> D(params (string, object?)[] kv)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    private static int U16(byte[] p, int i) => (p[i] << 8) | p[i + 1];

    private static string Hex(byte[] p, int from, int len) => Convert.ToHexString(p, from, Math.Min(len, p.Length - from));

    private static string Ascii(byte[] p, int from, int len)
    {
        var sb = new StringBuilder();
        for (int i = from; i < from + len && i < p.Length; i++)
        {
            if (p[i] == 0) break;
            sb.Append(p[i] is >= 0x20 and < 0x7F ? (char)p[i] : '?');
        }
        return sb.ToString();
    }

    private static List<string> Bits(int v, params (int Mask, string Name)[] names) =>
        names.Where(n => (v & n.Mask) != 0).Select(n => n.Name).ToList();

    /// <summary>Control (0x1B04 getCidInfo) flag names. Solaar special_keys.py KEY_FLAG: low byte reply[4], high byte reply[8].</summary>
    public static List<string> DecodeKeyFlags(int flags) => Bits(flags,
        (0x001, "mouse"), (0x002, "fkey"), (0x004, "nonstandard"), (0x008, "fn-sensitive"), (0x010, "reprogrammable"),
        (0x020, "divertable"), (0x040, "persistent-divert"), (0x080, "virtual"),
        (0x100, "raw-xy"), (0x200, "force-raw-xy"), (0x400, "analytics-key-events"), (0x800, "raw-wheel"));

    /// <summary>Control (0x1B04 getCidReporting) mapping flags. Solaar special_keys.py MAPPING_FLAG: low byte reply[2], high byte reply[5].</summary>
    public static List<string> DecodeMappingFlags(int flags) => Bits(flags,
        (0x001, "diverted"), (0x004, "persistently-diverted"), (0x010, "raw-xy-diverted"), (0x040, "force-raw-xy-diverted"),
        (0x100, "analytics-key-events-reporting"), (0x400, "raw-wheel-reporting"));

    private static string Named(int v, params string[] names) => v >= 0 && v < names.Length ? names[v] : $"unknown({v})";

    private static Dictionary<string, object?> RawOnly(byte[] r) => D(("raw", Hex(r, 0, 16)));

    // ------------------------------------------------------------------ the table

    private static InventoryGetter[] BuildTable()
    {
        const string solaarH = "Solaar lib/logitech_receiver/hidpp20.py";
        const string solaarS = "Solaar lib/logitech_receiver/settings_templates.py";
        const string documented = "documented";
        const string inferred = "inferred (fn0 getter by the HID++ 2.0 convention; confirm against the device)";

        return
        [
            // ---- core (sent by the runner's IRoot/IFeatureSet phase)
            new(0x0000, 1, "IRoot.ping (protocol version)", "Logitech HID++ 2.0 x0000 IRoot; docs/protocol.md", documented,
                (_, r) => D(("major", r[0]), ("minor", r[1]), ("echo", r[2])), [[0, 0, 0x5A]], null, true),
            new(0x0000, 0, "IRoot.getFeature (index lookup)", "Logitech HID++ 2.0 x0000 IRoot; docs/protocol.md", documented,
                (_, r) => D(("index", r[0]), ("typeFlags", r[1]), ("version", r[2])), null, null, true),
            new(0x0001, 0, "IFeatureSet.getCount", "Logitech x0001 IFeatureSet; " + solaarH, documented,
                (_, r) => D(("count", r[0])), null, null, true),
            new(0x0001, 1, "IFeatureSet.getFeatureId", "Logitech x0001 IFeatureSet; " + solaarH, documented,
                (_, r) => D(("featureId", $"0x{U16(r, 0):X4}"), ("typeFlags", r[2]), ("version", r[3])),
                null, new(IterationKind.CountedIndex, 0, 0, 1, 255), true),

            // ---- 0x0003 device firmware version: Solaar hidpp20.py get_firmware (fn0 entity count, fn1 info per entity)
            new(0x0003, 0, "getEntityCount", solaarH + " get_firmware", documented,
                (_, r) => D(("entityCount", r[0]), ("raw", Hex(r, 0, 4)))),
            new(0x0003, 1, "getFwInfo(entity)", solaarH + " get_firmware", documented,
                (_, r) => D(("kind", Named(r[0] & 0x0F, "firmware", "bootloader", "hardware", "other")), ("name", Ascii(r, 1, 3)),
                    ("version", $"{r[4]:X2}.{r[5]:X2}"), ("build", $"{r[6]:X2}{r[7]:X2}"), ("active", (r[8] & 1) != 0), ("extra", Hex(r, 9, 7))),
                null, new(IterationKind.CountedIndex, 0, 0, 1, 16)),

            // ---- 0x0005 device name/type: Solaar hidpp20.py get_name / get_kind
            new(0x0005, 0, "getNameCount", solaarH + " get_name", documented, (_, r) => D(("length", r[0]))),
            new(0x0005, 1, "getName(offset)", solaarH + " get_name", documented,
                (_, r) => D(("chunk", Ascii(r, 0, 16))), null, new(IterationKind.CountedOffset, 0, 0, 16, 64)),
            new(0x0005, 2, "getType", solaarH + " get_kind", documented,
                (_, r) => D(("type", r[0]), ("name", InventoryNames.DeviceType(r[0])))),

            // ---- 0x0007 friendly name: Solaar hidpp20.py get_friendly_name (fn0 length, fn1 chunk by index, fn2 default name)
            new(0x0007, 0, "getFriendlyNameLen", solaarH + " get_friendly_name", documented,
                (_, r) => D(("length", r[0]), ("maxLength", r[1]), ("defaultLength", r[2]))),
            new(0x0007, 1, "getFriendlyName(offset)", solaarH + " get_friendly_name", documented,
                (_, r) => D(("chunk", Ascii(r, 1, 15))), null, new(IterationKind.CountedOffset, 0, 0, 15, 60)),
            new(0x0007, 2, "getDefaultName(offset)", solaarH + " get_friendly_name", documented,
                (_, r) => D(("chunk", Ascii(r, 1, 15))), null, new(IterationKind.CountedOffset, 0, 2, 15, 60)),
            // 0x0008 KeepAlive is not listed: its only documented function sets a timeout, so it is not a pure getter.

            // ---- batteries
            new(0x1000, 0, "getBatteryLevelStatus", solaarH + " (BATTERY_STATUS read); docs/protocol.md", documented,
                (_, r) => D(("dischargePercent", r[0]), ("nextLevelPercent", r[1]),
                    ("status", Named(r[2], "discharging", "recharging", "almost full", "charged", "slow recharge", "invalid battery", "thermal error", "other charging error")))),
            new(0x1000, 1, "getBatteryCapability", "Logitech x1000 BatteryStatus spec; " + solaarH, inferred,
                (_, r) => D(("levels", r[0]), ("flags", r[1]), ("raw", Hex(r, 0, 6)))),
            new(0x1001, 0, "getBatteryVoltage", solaarH + " (BATTERY_VOLTAGE read)", documented,
                (_, r) => D(("millivolts", U16(r, 0)), ("flagsRaw", $"0x{r[2]:X2}"))),
            new(0x1004, 0, "getCapabilities", "Logitech x1004 UnifiedBattery spec; " + solaarH, documented,
                (_, r) => D(("supportedLevels", Bits(r[0], (1, "critical"), (2, "low"), (4, "good"), (8, "full"))), ("flagsRaw", $"0x{r[1]:X2}"), ("raw", Hex(r, 0, 6)))),
            new(0x1004, 1, "getStatus", solaarH + " (UNIFIED_BATTERY); docs/protocol.md", documented,
                (_, r) => D(("percent", r[0]), ("level", Bits(r[1], (1, "critical"), (2, "low"), (4, "good"), (8, "full"))),
                    ("charging", Named(r[2], "discharging", "charging", "charging slow", "charge complete", "error")), ("externalPower", r[3]))),

            // ---- 0x1B04 reprogrammable controls v4: Solaar hidpp20.py ReprogrammableKeyV4 (fn0 count, fn1 cid info, fn2 cid reporting)
            new(0x1B04, 0, "getControlCount", solaarH + " ReprogrammableKeyV4; docs/protocol.md", documented,
                (_, r) => D(("count", r[0]))),
            new(0x1B04, 1, "getCidInfo(index)", solaarH + " ReprogrammableKeyV4; special_keys.py KEY_FLAG", documented,
                (_, r) =>
                {
                    int flags = r[4] | (r[8] << 8);
                    return D(("cid", $"0x{U16(r, 0):X4}"), ("task", $"0x{U16(r, 2):X4}"), ("flags", flags), ("flagNames", DecodeKeyFlags(flags)),
                        ("position", r[5]), ("group", r[6]), ("groupMask", r[7]));
                },
                null, new(IterationKind.CountedIndex, 0, 0, 1, 64)),
            new(0x1B04, 2, "getCidReporting(cid)", solaarH + " ReprogrammableKeyV4; special_keys.py MAPPING_FLAG", documented,
                (_, r) =>
                {
                    int flags = r[2] | (r[5] << 8);
                    return D(("cid", $"0x{U16(r, 0):X4}"), ("mappedTo", $"0x{U16(r, 3):X4}"), ("flags", flags), ("flagNames", DecodeMappingFlags(flags)));
                },
                null, new(IterationKind.PerCid, 1)),

            // ---- 0x1C00 persistent remappable action: docs/protocol.md table (Solaar hidpp20.py PersistentRemappableAction); counts/info only
            new(0x1C00, 0, "getCapabilities", solaarH + " PersistentRemappableAction; docs/protocol.md", documented,
                (_, r) => D(("flags", $"0x{U16(r, 0):X4}"))),
            new(0x1C00, 1, "getCount", solaarH + " PersistentRemappableAction; docs/protocol.md", documented,
                (_, r) => D(("count", r[0]))),
            new(0x1C00, 2, "getCidInfo(index)", solaarH + " PersistentRemappableAction; docs/protocol.md", documented,
                (_, r) => D(("cid", $"0x{U16(r, 0):X4}")), null, new(IterationKind.CountedIndex, 1, 0, 1, 64, [0xFF])),

            // 0x1D4B WirelessDeviceStatus: event-only in Solaar and the Logitech spec (no getter), so listed only.

            // ---- 0x19B0 haptics: docs/protocol.md (confirmed on the MX Master 4: fn1 getState)
            new(0x19B0, 1, "getState", "docs/protocol.md Haptics row (confirmed on device)", documented,
                (_, r) => D(("enabled", (r[0] & 1) != 0), ("level", r[1]))),
            // 0x19C0 force sensing button: docs/protocol.md lists fn2 as a read of a u16 BE value.
            new(0x19C0, 2, "readForce", "docs/protocol.md Force sensing row", inferred,
                (_, r) => D(("value", U16(r, 0)), ("raw", Hex(r, 0, 4)))),

            // ---- SmartShift
            new(0x2110, 0, "getRatchetControlMode", solaarS + " SmartShift (read_fnid 0x00)", documented,
                (_, r) => D(("mode", Named(r[0], "unset", "free-spin", "ratchet")), ("autoDisengage", r[1]), ("autoDisengageDefault", r[2]))),
            new(0x2111, 0, "getCapabilities", "Logitech x2111 SmartShiftEnhanced spec; " + solaarS, documented,
                (_, r) => D(("flags", $"0x{r[0]:X2}"), ("tunableTorque", (r[0] & 1) != 0), ("defaultAutoDisengage", r[1]), ("defaultTunableTorque", r[2]), ("maxForce", r[3]))),
            new(0x2111, 1, "getRatchetControlMode", solaarS + " SmartShiftEnhanced; docs/protocol.md (confirmed on device)", documented,
                (_, r) => D(("mode", Named(r[0], "unset", "free-spin", "ratchet")), ("autoDisengage", r[1]), ("force", r[2]))),

            // ---- 0x2121 hi-res wheel: Solaar settings_templates.py HiResScroll / hidpp20.py (cap flags 0x08 invert, 0x04 ratchet)
            new(0x2121, 0, "getWheelCapability", solaarH + " / " + solaarS + " (HIRES_WHEEL)", documented,
                (_, r) => D(("multiplier", r[0]), ("flags", $"0x{r[1]:X2}"), ("hasInvert", (r[1] & 0x08) != 0), ("hasRatchetSwitch", (r[1] & 0x04) != 0))),
            new(0x2121, 1, "getWheelMode", solaarS + " (HIRES_WHEEL mode: 0x01 target, 0x02 resolution, 0x04 invert)", documented,
                (_, r) => D(("flags", $"0x{r[0]:X2}"), ("diverted", (r[0] & 1) != 0), ("hiRes", (r[0] & 2) != 0), ("inverted", (r[0] & 4) != 0))),
            new(0x2121, 2, "getRatchetSwitchStatus", "Logitech x2121 HiResWheel spec; docs/protocol.md allowlist", documented,
                (_, r) => D(("ratchet", r[0]))),

            // ---- 0x2150 thumb wheel: docs/protocol.md Thumb wheel row (confirmed on device)
            new(0x2150, 0, "getInfo", "docs/protocol.md Thumb wheel row (confirmed on device)", documented,
                (_, r) => D(("nativeResolution", U16(r, 0)), ("divertedResolution", U16(r, 2)), ("defaultDirection", r[4]),
                    ("caps", Bits(r[5], (1, "timestamp"), (2, "touch"), (4, "proximity"), (8, "single-tap"))), ("timeUnit", U16(r, 6)))),
            new(0x2150, 1, "getConfig", "docs/protocol.md Thumb wheel row (confirmed on device)", documented,
                (_, r) => D(("mode", Named(r[0], "native", "diverted")), ("inverted", r[1] != 0))),

            // ---- 0x2201 adjustable DPI: Solaar hidpp20.py get_dpi_list / settings_templates.py DPI
            new(0x2201, 0, "getSensorCount", solaarH + " (ADJUSTABLE_DPI)", documented, (_, r) => D(("count", r[0]))),
            new(0x2201, 1, "getSensorDpiList(sensor)", solaarS + " (DPI list: values, 0xE000|step marks a range)", documented,
                DecodeDpiList, null, new(IterationKind.CountedIndex, 0, 0, 1, 8)),
            new(0x2201, 2, "getSensorDpi(sensor)", solaarS + " DPI; docs/protocol.md", documented,
                (_, r) => D(("sensor", r[0]), ("dpi", U16(r, 1)), ("defaultDpi", U16(r, 3)), ("tail", Hex(r, 5, 3))),
                null, new(IterationKind.CountedIndex, 0, 0, 1, 8)),

            // ---- 0x2205 pointer speed: Solaar settings_templates.py PointerSpeed (read_fnid 0x00, 16-bit, 1/256 steps)
            new(0x2205, 0, "getPointerSpeed", solaarS + " PointerSpeed", documented,
                (_, r) => D(("raw", U16(r, 0)), ("speed", U16(r, 0) / 256.0))),

            // ---- 0x40A3 Fn inversion multi-host: docs/protocol.md (fn0 getter, request p[0] = host)
            new(0x40A3, 0, "getFnInversion(host)", "docs/protocol.md keyboard section (confirmed on the ERGO K860)", documented,
                (_, r) => D(("host", r[0]), ("inverted", r[1] != 0), ("p2", r[2]), ("p3", r[3])),
                [[0xFF], [0], [1], [2]]),

            // ---- 0x4521 / 0x4522 disable keys
            new(0x4521, 0, "getCapabilities", solaarS + " DisableKeyboardKeys (key mask)", documented, DecodeDisableKeys),
            new(0x4521, 1, "getDisabledKeys", solaarS + " DisableKeyboardKeys (read_fnid 0x10)", documented, DecodeDisableKeys),
            new(0x4522, 0, "getCapabilities", "Logitech x4522 DisableKeysByUsage draft spec (Solaar names the feature only)", inferred,
                (_, r) => RawOnly(r)),

            // ---- keyboard platform / layout / lock keys
            new(0x4530, 0, "getPlatform", "Logitech x4530 DualPlatform spec (Solaar names the feature only)", inferred,
                (_, r) => D(("value", r[0]), ("raw", Hex(r, 0, 4)))),
            new(0x4540, 0, "getKeyboardLayout", solaarH + " keyboard_layout (KEYBOARD_LAYOUT_2)", documented,
                (_, r) => D(("layout", r[0]))),
            new(0x4220, 0, "getLockKeyState", "Logitech x4220 LockKeyState spec (Solaar names the feature only)", inferred,
                (_, r) => D(("mask", $"0x{r[0]:X2}"), ("raw", Hex(r, 0, 4)))),

            // ---- lighting
            new(0x1990, 0, "getIllumination", "Logitech x1990 Illumination spec", inferred,
                (_, r) => D(("on", (r[0] & 1) != 0), ("raw", Hex(r, 0, 4)))),
            new(0x1982, 0, "getBacklightConfig", solaarS + " Backlight2 (read_fnid 0x00)", documented, (_, r) => RawOnly(r)),

            // ---- gaming
            new(0x8010, 0, "getGKeyCount", solaarH + " (GKEY count)", documented, (_, r) => D(("count", r[0]))),
            new(0x8020, 0, "getMKeyCount", solaarH + " (MKEYS count)", documented, (_, r) => D(("count", r[0]))),
        ];
    }

    private static Dictionary<string, object?> DecodeDisableKeys(byte[] _, byte[] r) =>
        D(("mask", $"0x{r[0]:X2}"), ("keys", Bits(r[0], (1, "caps lock"), (2, "num lock"), (4, "scroll lock"), (8, "insert"), (0x10, "win"))));

    private static Dictionary<string, object?> DecodeDpiList(byte[] request, byte[] r)
    {
        var values = new List<int>();
        for (int i = 1; i + 1 < r.Length; i += 2)
        {
            int v = U16(r, i);
            if (v == 0) break;
            values.Add(v);
        }
        var discrete = new List<int>();
        var ranges = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] >= 0xE000 && i > 0 && i + 1 < values.Count)
            {
                ranges.Add($"{values[i - 1]}-{values[i + 1]} step {values[i] & 0x1FFF}");
                discrete.Remove(values[i - 1]);
                i++;
                continue;
            }
            if (values[i] < 0xE000) discrete.Add(values[i]);
        }
        return D(("sensor", r[0]), ("discrete", discrete), ("ranges", ranges), ("raw", Hex(r, 0, 16)));
    }
}
