namespace Slender.GalaxyBuds;

/// <summary>One RFCOMM control channel to the buds. Dispose may be called from any thread and must unblock a Connect or Read in progress.</summary>
public interface IBudsLink : IDisposable
{
    /// <summary>Opens the channel (blocking). Throws <see cref="IOException"/> when the buds cannot be reached.</summary>
    void Connect();

    /// <summary>Blocks for the next bytes; returns 0 when the channel closed. Throws <see cref="IOException"/> on a socket error.</summary>
    int Read(byte[] buffer);

    void Write(ReadOnlySpan<byte> data);
}

/// <summary>Creates links to buds by Bluetooth address (nothing is opened until Connect).</summary>
public interface IBudsTransport
{
    IBudsLink Create(ulong address);
}
