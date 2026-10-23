namespace Slender.Yealink;

/// <summary>
/// The collections this module reads: the private telephony copy (COL02), the Teams button (COL04), the standard telephony one
/// (COL01, input only: Teams and the 3CX web client own its outputs) and the vendor channel (COL05, read, and written only with
/// requests from the allowlist), and, only while the base-ring source is on, the consumer keys (COL03, input only).
/// </summary>
public enum YealinkCollection { Telephony, Teams, Standard, Vendor, ConsumerKeys }

/// <summary>Called on the transport's read thread; the span is only valid for the call.</summary>
public delegate void YealinkReportHandler(YealinkCollection collection, ReadOnlySpan<byte> report);

/// <summary>Finds and opens the headset. Open is called only between the module's Start and Stop.</summary>
public interface IYealinkTransport
{
    /// <summary>
    /// Opens the WH64's telephony, Teams and standard telephony collections (the telephony one for writing too only when
    /// writeLeds) and, only when vendorReads, the vendor channel (shared read/write), and the consumer keys (read only) only when consumerKeys. Null when the headset is not present.
    /// Nothing is read until the returned link's Start. Never throws for a missing device; failures go to log.
    /// </summary>
    IYealinkLink? Open(bool writeLeds, bool vendorReads, YealinkReportHandler onReport, Action<string> log, bool consumerKeys = false);
}

/// <summary>One open session. Disposing cancels pending I/O, joins the read thread and closes every handle.</summary>
public interface IYealinkLink : IDisposable
{
    /// <summary>Usage layout of the telephony collection; null when that collection could not be opened.</summary>
    ICollectionCaps? Telephony { get; }

    /// <summary>Usage layout of the standard telephony collection (COL01); null when that collection could not be opened.</summary>
    ICollectionCaps? Standard { get; }

    /// <summary>The read loop ended on an error (device removed, say); the link is dead until reopened.</summary>
    bool IsFaulted { get; }

    /// <summary>Begins reading; reports arrive through the handler given to Open.</summary>
    void Start();

    /// <summary>Writes one output report to the telephony collection. False when not writable or already disposed.</summary>
    bool Write(ReadOnlySpan<byte> report);

    /// <summary>
    /// Writes one vendor request to COL05. False when the channel is not open for writing (vendor reads off, or it could not be
    /// opened) or the link is disposed. Only YealinkVendorSession calls this, and it builds requests from the allowlist alone.
    /// </summary>
    bool WriteVendor(ReadOnlySpan<byte> report);
}
