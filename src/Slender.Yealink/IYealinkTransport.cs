namespace Slender.Yealink;

/// <summary>The two collections this module reads. COL01 (owned by Teams and the 3CX web client) and COL05 (vendor commands) are never opened.</summary>
public enum YealinkCollection { Telephony, Teams }

/// <summary>Called on the transport's read thread; the span is only valid for the call.</summary>
public delegate void YealinkReportHandler(YealinkCollection collection, ReadOnlySpan<byte> report);

/// <summary>Finds and opens the headset. Open is called only between the module's Start and Stop.</summary>
public interface IYealinkTransport
{
    /// <summary>
    /// Opens the WH64's telephony and Teams collections (the telephony one for writing too only when writeLeds). Null when the
    /// headset is not present. Nothing is read until the returned link's Start. Never throws for a missing device; failures
    /// go to log.
    /// </summary>
    IYealinkLink? Open(bool writeLeds, YealinkReportHandler onReport, Action<string> log);
}

/// <summary>One open session. Disposing cancels pending I/O, joins the read thread and closes every handle.</summary>
public interface IYealinkLink : IDisposable
{
    /// <summary>Usage layout of the telephony collection; null when that collection could not be opened.</summary>
    ICollectionCaps? Telephony { get; }

    /// <summary>The read loop ended on an error (device removed, say); the link is dead until reopened.</summary>
    bool IsFaulted { get; }

    /// <summary>Begins reading; reports arrive through the handler given to Open.</summary>
    void Start();

    /// <summary>Writes one output report to the telephony collection. False when not writable or already disposed.</summary>
    bool Write(ReadOnlySpan<byte> report);
}
