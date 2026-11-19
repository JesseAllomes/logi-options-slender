using System.Runtime.InteropServices;

namespace Slender.GalaxyBuds.Windows;

/// <summary>
/// Winsock Bluetooth RFCOMM to the buds' control service. Connecting by service UUID lets Windows resolve the RFCOMM channel (it is
/// 27 on one firmware and must not be hard-coded). No driver, COM port or admin rights; nothing exists until Create.
/// </summary>
public sealed unsafe partial class WindowsBudsTransport : IBudsTransport
{
    /// <summary>The buds' control service (SDP name GEARMANAGER).</summary>
    public static readonly Guid ControlService = new("2e73a4ad-332d-41fc-90e2-16bef06523f2");

    public IBudsLink Create(ulong address) => new Link(address);

    private const int AF_BTH = 32;
    private const int SOCK_STREAM = 1;
    private const int BTHPROTO_RFCOMM = 3;
    private static readonly nint InvalidSocket = -1;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SockaddrBth
    {
        public ushort AddressFamily;
        public ulong BtAddr;
        public Guid ServiceClassId;
        public uint Port;
    }

    [LibraryImport("ws2_32.dll")]
    private static partial int WSAStartup(ushort version, byte* data);

    [LibraryImport("ws2_32.dll")]
    private static partial int WSACleanup();

    [LibraryImport("ws2_32.dll")]
    private static partial nint socket(int af, int type, int protocol);

    [LibraryImport("ws2_32.dll")]
    private static partial int connect(nint s, SockaddrBth* name, int namelen);

    [LibraryImport("ws2_32.dll")]
    private static partial int send(nint s, byte* buf, int len, int flags);

    [LibraryImport("ws2_32.dll")]
    private static partial int recv(nint s, byte* buf, int len, int flags);

    [LibraryImport("ws2_32.dll")]
    private static partial int closesocket(nint s);

    [LibraryImport("ws2_32.dll")]
    private static partial int WSAGetLastError();

    private sealed class Link(ulong address) : IBudsLink
    {
        private readonly object _gate = new();
        private nint _socket = InvalidSocket;
        private bool _started, _disposed;

        public void Connect()
        {
            nint s;
            lock (_gate)
            {
                if (_disposed) throw new IOException("closed");
                if (!_started)
                {
                    byte* wsa = stackalloc byte[512];
                    int rc = WSAStartup(0x0202, wsa);
                    if (rc != 0) throw new IOException($"WSAStartup failed ({rc})");
                    _started = true;
                }

                s = socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
                if (s == InvalidSocket) throw new IOException($"socket failed ({WSAGetLastError()})");
                _socket = s;
            }

            var addr = new SockaddrBth { AddressFamily = AF_BTH, BtAddr = address, ServiceClassId = ControlService, Port = 0 };
            if (connect(s, &addr, sizeof(SockaddrBth)) != 0)
            {
                int err = WSAGetLastError();
                Dispose();
                throw new IOException($"connect failed ({err})");
            }
        }

        public int Read(byte[] buffer)
        {
            nint s = _socket;
            if (s == InvalidSocket) return 0;
            fixed (byte* p = buffer)
            {
                int n = recv(s, p, buffer.Length, 0);
                if (n >= 0) return n;
            }

            // A close from Dispose shows up as an error on the blocked recv; that is the end of the link, not a fault.
            if (_disposed) return 0;
            throw new IOException($"recv failed ({WSAGetLastError()})");
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            nint s = _socket;
            if (s == InvalidSocket) throw new IOException("closed");
            fixed (byte* p = data)
            {
                int done = 0;
                while (done < data.Length)
                {
                    int n = send(s, p + done, data.Length - done, 0);
                    if (n <= 0) throw new IOException($"send failed ({WSAGetLastError()})");
                    done += n;
                }
            }
        }

        public void Dispose()
        {
            nint s;
            bool cleanup;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                s = _socket;
                _socket = InvalidSocket;
                cleanup = _started;
            }

            if (s != InvalidSocket) closesocket(s);
            if (cleanup) WSACleanup();
        }
    }
}
