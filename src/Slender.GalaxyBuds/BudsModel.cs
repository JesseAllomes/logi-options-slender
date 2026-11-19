using System.Text;
using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.GalaxyBuds;

/// <summary>Where one earbud is, as the buds report it. The nibble values are from the capability map; the enum is unconfirmed beyond "in case" (3), so the mapping lives only here.</summary>
public enum BudsPlacement { Disconnected = 0, Wearing = 1, Idle = 2, InCase = 3, CaseClosed = 4 }

/// <summary>
/// What the buds last said, as plain fields. Not thread-safe: the module owns one under its lock. The Raw array is indexed like
/// GalaxyBudsSettingsCatalog.All; a null entry means the buds have not reported that one.
/// </summary>
public sealed class BudsModel
{
    public int? BatteryLeft, BatteryRight, BatteryCase;
    public bool? ChargingLeft, ChargingRight, ChargingCase;
    public BudsPlacement? PlacementLeft, PlacementRight;
    public string? Firmware, Model;

    /// <summary>206 answer: full charge cycles per bud (kept across reconnects; see BudsModule.CycleReadEvery).</summary>
    public double? CyclesLeft, CyclesRight;

    /// <summary>118 answer (revision 1 only): multipoint supported and the streaming mask (1 A2DP, 2 HFP, 4/8/16 LE audio).</summary>
    public bool? MultipointSupported;
    public int? MultipointStreaming;
    public readonly int?[] Raw = new int?[GalaxyBudsSettingsCatalog.All.Count];

    public static string? PlacementText(BudsPlacement? p) => p switch
    {
        BudsPlacement.Wearing => "wearing",
        BudsPlacement.Idle => "idle",
        BudsPlacement.InCase => "in case",
        BudsPlacement.CaseClosed => "case closed",
        BudsPlacement.Disconnected => "disconnected",
        _ => null,
    };

    /// <summary>The one place the placement nibble is mapped.</summary>
    public static BudsPlacement? PlacementOf(int nibble) =>
        nibble is >= 0 and <= 4 ? (BudsPlacement)nibble : null;

    public int? Get(string key) => Raw[GalaxyBudsSettingsCatalog.IndexOf(key)];

    public void Set(string key, int raw)
    {
        int i = GalaxyBudsSettingsCatalog.IndexOf(key);
        if (i >= 0) Raw[i] = raw;
    }

    /// <summary>True when either earbud is reported as worn.</summary>
    public bool AnyWorn => PlacementLeft == BudsPlacement.Wearing || PlacementRight == BudsPlacement.Wearing;

    public bool PlacementKnown => PlacementLeft is not null && PlacementRight is not null;

    /// <summary>Gesture bits as text: "All on", "All off" or the enabled ones.</summary>
    public static string GesturesText(int bits)
    {
        if ((bits & 0xFF) == 0xFF) return "All on";
        if ((bits & 0xFF) == 0) return "All off";
        var on = new List<string>(6);
        if ((bits & 0x80) != 0) on.Add("Single tap");
        if ((bits & 0x08) != 0) on.Add("Double tap");
        if ((bits & 0x04) != 0) on.Add("Triple tap");
        if ((bits & 0x02) != 0) on.Add("Pinch and hold");
        if ((bits & 0x10) != 0) on.Add("Double tap in calls");
        if ((bits & 0x20) != 0) on.Add("Hold in calls");
        if (on.Count == 0) on.Add("Other gestures");
        return string.Join(", ", on);
    }

    public BudsState ToState(bool connected, bool findActive, string? writeProblem)
    {
        var settings = new List<BudsSetting>(GalaxyBudsSettingsCatalog.All.Count);
        for (int i = 0; i < GalaxyBudsSettingsCatalog.All.Count; i++)
        {
            if (Raw[i] is not { } raw) continue;
            var def = GalaxyBudsSettingsCatalog.All[i];
            settings.Add(new BudsSetting(def.Key, def.Group, def.Label, def.Key == GalaxyBudsSettingsCatalog.Gestures ? GesturesText(raw) : def.Text(raw), raw));
        }
        return new BudsState(connected, BatteryLeft, BatteryRight, BatteryCase, ChargingLeft, ChargingRight, ChargingCase,
            PlacementText(PlacementLeft), PlacementText(PlacementRight), Firmware, Model, settings, findActive, writeProblem,
            CyclesLeft, CyclesRight, MultipointSupported, MultipointStreaming);
    }
}

/// <summary>Reads the buds' status messages into a <see cref="BudsModel"/>. Pure; short messages change only what they can carry.</summary>
public static class BudsStatusParser
{
    /// <summary>Applies one frame. Returns true when it was a status message the parser handles.</summary>
    public static bool Apply(BudsModel m, BudsFrame frame)
    {
        switch (frame.Id)
        {
            case BudsIds.ExtendedStatusUpdated: ApplyExtended(m, frame.Payload); return true;
            case BudsIds.StatusUpdated: ApplyStatus(m, frame.Payload); return true;
            case BudsIds.NoiseControlsUpdate:
                if (frame.Payload.Length >= 1) m.Set(GalaxyBudsSettingsCatalog.NoiseMode, frame.Payload[0]);
                return true;
            case BudsIds.VersionInfoLong: ApplyVersion(m, frame.Payload); return true;
            case BudsIds.DebugSku: ApplySku(m, frame.Payload); return true;
            case BudsIds.BatteryCycles: ApplyCycles(m, frame.Payload); return true;
            case BudsIds.MultipointInfo: ApplyMultipoint(m, frame.Payload); return true;
            default: return false;
        }
    }

    private static int Hi(byte b) => b >> 4;
    private static int Lo(byte b) => b & 0x0F;
    private static int Invert(byte b) => b == 0 ? 1 : 0;

    private static void SetBattery(BudsModel m, int l, int r, int c)
    {
        m.BatteryLeft = Percent(l);
        m.BatteryRight = Percent(r);
        m.BatteryCase = Percent(c);
    }

    private static int? Percent(int v) => v is >= 0 and <= 100 ? v : null;

    private static void SetCharging(BudsModel m, byte bits)
    {
        m.ChargingLeft = (bits & 0x10) != 0;
        m.ChargingRight = (bits & 0x04) != 0;
        m.ChargingCase = (bits & 0x01) != 0;
    }

    private static void SetPlacement(BudsModel m, byte b)
    {
        m.PlacementLeft = BudsModel.PlacementOf(Hi(b));
        m.PlacementRight = BudsModel.PlacementOf(Lo(b));
    }

    /// <summary>96: [rev, battL, battR, coupled, main, placement, caseBatt, chargingBits].</summary>
    private static void ApplyStatus(BudsModel m, byte[] p)
    {
        if (p.Length < 7) return;
        SetBattery(m, p[1], p[2], p[6]);
        SetPlacement(m, p[5]);
        if (p.Length >= 8) SetCharging(m, p[7]);
    }

    /// <summary>97 (revision 4): layout in the capability map, section 3. Fields past the end of a short message stay as they were.</summary>
    private static void ApplyExtended(BudsModel m, byte[] p)
    {
        if (p.Length < 13) return;
        SetBattery(m, p[2], p[3], p[7]);
        SetPlacement(m, p[6]);
        if (p.Length > 43) SetCharging(m, p[43]);

        void Put(string key, int index, Func<byte, int>? map = null)
        {
            if (index < p.Length) m.Set(key, map is null ? p[index] : map(p[index]));
        }

        Put(GalaxyBudsSettingsCatalog.NoiseMode, 12);
        Put(GalaxyBudsSettingsCatalog.AmbientLevel, 23);
        Put(GalaxyBudsSettingsCatalog.NoiseOneBud, 28);
        Put(GalaxyBudsSettingsCatalog.VoiceDetect, 26);
        Put(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 27);
        Put(GalaxyBudsSettingsCatalog.SirenDetect, 52);
        Put(GalaxyBudsSettingsCatalog.TouchHoldLeft, 11, Hi);
        Put(GalaxyBudsSettingsCatalog.TouchHoldRight, 11, Lo);
        Put(GalaxyBudsSettingsCatalog.CycleLeft, 21, Hi);
        Put(GalaxyBudsSettingsCatalog.CycleRight, 21, Lo);
        Put(GalaxyBudsSettingsCatalog.Gestures, 10);
        Put(GalaxyBudsSettingsCatalog.EqPreset, 9);
        Put(GalaxyBudsSettingsCatalog.AdaptiveEq, 49);
        Put(GalaxyBudsSettingsCatalog.Balance, 25);
        Put(GalaxyBudsSettingsCatalog.Audio360, 35);
        Put(GalaxyBudsSettingsCatalog.HeadTracking, 42);
        Put(GalaxyBudsSettingsCatalog.AdaptiveVolume, 51);
        Put(GalaxyBudsSettingsCatalog.AmbientCustom, 29);
        Put(GalaxyBudsSettingsCatalog.AmbientCustomLeft, 30, Hi);
        Put(GalaxyBudsSettingsCatalog.AmbientCustomRight, 30, Lo);
        Put(GalaxyBudsSettingsCatalog.AmbientCustomTone, 31);
        Put(GalaxyBudsSettingsCatalog.Sidetone, 33);
        // Wire 0 = on for these two; the logical raw is 1 = on.
        Put(GalaxyBudsSettingsCatalog.InEarCalls, 34, Invert);
        Put(GalaxyBudsSettingsCatalog.Seamless, 19, Invert);
        Put(GalaxyBudsSettingsCatalog.AutoPause, 46);
        Put(GalaxyBudsSettingsCatalog.NoiseCancellingStrength, 24);
    }

    /// <summary>Most cycles a bud battery is believed to report; anything above (or negative) is not a cycle count.</summary>
    public const double MaxCycles = 100_000;

    /// <summary>
    /// 206 SOC_BATTERY_CYCLE answer: 16 bytes, two big-endian signed 64-bit counts (left, then right) in ten-thousandths of a full
    /// charge cycle. Any other length is ignored; an out-of-range count leaves that side unknown.
    /// </summary>
    private static void ApplyCycles(BudsModel m, byte[] p)
    {
        if (p.Length != 16) return;
        m.CyclesLeft = Cycles(System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(p.AsSpan(0, 8)));
        m.CyclesRight = Cycles(System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(p.AsSpan(8, 8)));
    }

    private static double? Cycles(long tenThousandths) =>
        tenThousandths >= 0 && tenThousandths / 10000.0 <= MaxCycles ? tenThousandths / 10000.0 : null;

    /// <summary>
    /// 118 MULTIPOINT_INFO answer: [0] revision, [1] supported, [2] state, [3] focus, [4] streaming mask, then bytes not mapped.
    /// Only revision 1 (the one captured) is read; another revision leaves the multipoint fields unknown.
    /// </summary>
    private static void ApplyMultipoint(BudsModel m, byte[] p)
    {
        if (p.Length < 5 || p[0] != 1)
        {
            m.MultipointSupported = null;
            m.MultipointStreaming = null;
            return;
        }

        m.MultipointSupported = p[1] != 0;
        m.MultipointStreaming = p[4];
    }

    /// <summary>104: [hwL, hwR, firmwareL (21 ASCII bytes), firmwareR (21)]. The left string is shown.</summary>
    private static void ApplyVersion(BudsModel m, byte[] p)
    {
        if (p.Length < 2 + 21) return;
        string s = Ascii(p.AsSpan(2, 21));
        if (s.Length > 0) m.Firmware = s;
    }

    /// <summary>34: 14-character SKU per bud; "SM-R640N..." is shown as "SM-R640".</summary>
    private static void ApplySku(BudsModel m, byte[] p)
    {
        string s = Ascii(p.AsSpan(0, Math.Min(14, p.Length)));
        if (s.Length == 0) return;
        m.Model = s.StartsWith("SM-", StringComparison.Ordinal) && s.Length >= 7 ? s[..7] : s;
    }

    private static string Ascii(ReadOnlySpan<byte> b)
    {
        int n = b.IndexOf((byte)0);
        if (n >= 0) b = b[..n];
        var sb = new StringBuilder(b.Length);
        foreach (byte x in b)
            if (x is >= 0x20 and < 0x7F) sb.Append((char)x);
        return sb.ToString().Trim();
    }
}
