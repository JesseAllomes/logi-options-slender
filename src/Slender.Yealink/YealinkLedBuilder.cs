namespace Slender.Yealink;

/// <summary>
/// Builds the telephony copy's LED output report. Only Mute, Off-Hook, Ring and Hold on that one output report can be set;
/// every other usage is refused, so nothing here can reach a feature, command or other collection. Holds the last LED state
/// because each write replaces the whole report.
/// </summary>
public sealed class YealinkLedBuilder
{
    private readonly ICollectionCaps _caps;
    private readonly byte[] _report;
    private readonly byte _reportId;

    private YealinkLedBuilder(ICollectionCaps caps, byte reportId)
    {
        _caps = caps;
        _reportId = reportId;
        _report = new byte[caps.OutputReportLength];
        _report[0] = reportId;
    }

    /// <summary>Null when the caps carry no output slot for the mute LED (nothing safe to drive).</summary>
    public static YealinkLedBuilder? TryCreate(ICollectionCaps caps)
    {
        if (caps.OutputReportLength < 2 || !caps.TryGetOutput((ushort)YealinkLed.Mute, out var mute)) return null;
        return new YealinkLedBuilder(caps, mute.ReportId);
    }

    public static bool IsAllowed(ushort usage) =>
        usage is (ushort)YealinkLed.Mute or (ushort)YealinkLed.OffHook or (ushort)YealinkLed.Ring or (ushort)YealinkLed.Hold;

    public ReadOnlySpan<byte> Report => _report;

    public bool Get(YealinkLed led) => TryResolve((ushort)led, out var slot) && (_report[slot.Bit >> 3] >> (slot.Bit & 7) & 1) != 0;

    public void Set(YealinkLed led, bool on) => Set((ushort)led, on);

    /// <summary>Refuses (ArgumentOutOfRangeException) any usage that is not an allowlisted LED, and (InvalidOperationException) one this device does not map.</summary>
    public void Set(ushort usage, bool on)
    {
        if (!IsAllowed(usage)) throw new ArgumentOutOfRangeException(nameof(usage), usage, "Not an allowlisted LED usage.");
        if (!TryResolve(usage, out var slot)) throw new InvalidOperationException($"LED usage 0x{usage:X2} is not on the output report.");
        byte mask = (byte)(1 << (slot.Bit & 7));
        if (on) _report[slot.Bit >> 3] |= mask; else _report[slot.Bit >> 3] &= (byte)~mask;
    }

    private bool TryResolve(ushort usage, out UsageSlot slot) =>
        _caps.TryGetOutput(usage, out slot) && slot.ReportId == _reportId && (slot.Bit >> 3) >= 1 && (slot.Bit >> 3) < _report.Length;
}
