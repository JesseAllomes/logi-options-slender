using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Slender.Hidpp.Transport;

/// <summary>
/// Overlapped HID file I/O on one HID++ collection. One read and one write may be in flight at a time.
/// The overlapped structure and buffer are native memory freed only after the I/O has completed or been cancelled.
/// </summary>
public sealed unsafe class WindowsHidTransport : IHidppTransport
{
    private const int ErrorOperationAborted = 995;
    private const int WriteTimeoutMs = 2000;

    private readonly SafeFileHandle _handle;
    private readonly int _inputLength;
    private readonly int _outputLength;
    private readonly ManualResetEvent _readEvent = new(false);
    private readonly ManualResetEvent _writeEvent = new(false);
    private readonly object _writeLock = new();
    private volatile bool _disposed;

    internal WindowsHidTransport(SafeFileHandle handle, int inputLength, int outputLength)
    {
        _handle = handle;
        _inputLength = inputLength;
        _outputLength = outputLength;
    }

    public bool IsOpen => !_disposed && !_handle.IsClosed;

    public ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var copy = report.ToArray();
        return new ValueTask(Task.Run(() => WriteCore(copy), ct));
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, int timeoutMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<int>(Task.Run(() =>
        {
            var data = ReadCore(timeoutMs, ct);
            int n = Math.Min(data.Length, buffer.Length);
            data.AsSpan(0, n).CopyTo(buffer.Span);
            return n;
        }, CancellationToken.None));
    }

    private void WriteCore(byte[] report)
    {
        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Pad to the collection's output report length (caps.OutputReportByteLength).
            int len = Math.Max(report.Length, _outputLength);
            var padded = new byte[len];
            report.CopyTo(padded, 0);

            var ov = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            try
            {
                _writeEvent.Reset();
                ov->EventHandle = _writeEvent.SafeWaitHandle.DangerousGetHandle();
                fixed (byte* p = padded)
                {
                    bool ok = NativeMethods.WriteFile(_handle, p, (uint)len, null, ov);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != NativeMethods.ERROR_IO_PENDING) throw new Win32Exception(err);
                        if (!_writeEvent.WaitOne(WriteTimeoutMs))
                        {
                            NativeMethods.CancelIoEx(_handle, ov);
                            NativeMethods.GetOverlappedResult(_handle, ov, out _, true);
                            throw new TimeoutException("HID write timed out");
                        }
                    }
                    if (!NativeMethods.GetOverlappedResult(_handle, ov, out _, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally { NativeMemory.Free(ov); }
        }
    }

    /// <summary>Blocking overlapped read with timeout and cancellation. Returns an empty array on timeout.</summary>
    private byte[] ReadCore(int timeoutMs, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int len = _inputLength > 0 ? _inputLength : 64;
        var buf = (byte*)NativeMemory.AllocZeroed((nuint)len);
        var ov = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        try
        {
            _readEvent.Reset();
            ov->EventHandle = _readEvent.SafeWaitHandle.DangerousGetHandle();
            bool ok = NativeMethods.ReadFile(_handle, buf, (uint)len, null, ov);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != NativeMethods.ERROR_IO_PENDING) throw new Win32Exception(err);

                int signalled = WaitHandle.WaitAny([_readEvent, ct.WaitHandle], timeoutMs < 0 ? Timeout.Infinite : timeoutMs);
                if (signalled != 0)
                {
                    // Timeout or cancellation: cancel, then wait for the kernel to finish with our buffer.
                    NativeMethods.CancelIoEx(_handle, ov);
                    if (!NativeMethods.GetOverlappedResult(_handle, ov, out uint late, true))
                    {
                        int e = Marshal.GetLastWin32Error();
                        if (e != ErrorOperationAborted) throw new Win32Exception(e);
                        ct.ThrowIfCancellationRequested();
                        return [];
                    }
                    // Data raced in just before the cancel took effect: keep it.
                    return new ReadOnlySpan<byte>(buf, (int)late).ToArray();
                }
            }
            if (!NativeMethods.GetOverlappedResult(_handle, ov, out uint n, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new ReadOnlySpan<byte>(buf, (int)n).ToArray();
        }
        finally
        {
            NativeMemory.Free(ov);
            NativeMemory.Free(buf);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (!_handle.IsClosed) NativeMethods.CancelIoEx(_handle, null); } catch (ObjectDisposedException) { }
        _handle.Dispose();
    }
}
