namespace Slender.Yealink;

/// <summary>Where one usage lives in a report: the report id and the absolute bit index in the whole report buffer (byte 0 is the id).</summary>
public readonly record struct UsageSlot(byte ReportId, int Bit);

/// <summary>
/// The usage layout of one HID collection, read from its preparsed data (HidP) rather than assumed. Usages are keyed by usage id
/// alone: the collection's pages (0xFF30 in, 0xFF40 out on the telephony copy) never reuse an id within one direction.
/// </summary>
public interface ICollectionCaps
{
    int InputReportLength { get; }

    int OutputReportLength { get; }

    bool TryGetInput(ushort usage, out UsageSlot slot);

    bool TryGetOutput(ushort usage, out UsageSlot slot);
}

/// <summary>Plain-data caps. The Windows reader fills it from HidP; tests fill it with the Genesys layout.</summary>
public sealed class CollectionCaps(int inputLength, int outputLength,
    IReadOnlyDictionary<ushort, UsageSlot> inputs, IReadOnlyDictionary<ushort, UsageSlot> outputs) : ICollectionCaps
{
    public int InputReportLength { get; } = inputLength;

    public int OutputReportLength { get; } = outputLength;

    public bool TryGetInput(ushort usage, out UsageSlot slot) => inputs.TryGetValue(usage, out slot);

    public bool TryGetOutput(ushort usage, out UsageSlot slot) => outputs.TryGetValue(usage, out slot);
}

/// <summary>The telephony-copy input usages used here (HID Telephony page ids, repeated on Yealink's vendor pages).</summary>
public static class YealinkUsages
{
    public const ushort HookSwitch = 0x20;
    public const ushort PhoneMute = 0x2F;
    public const ushort Flash = 0x21;
    public const ushort Reject = 0x97;
}

/// <summary>The only LEDs this code may drive, by their output usage id.</summary>
public enum YealinkLed : ushort
{
    OffHook = 0x17,
    Mute = 0x09,
    Ring = 0x18,
    Hold = 0x20,
}
