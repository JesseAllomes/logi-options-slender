using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Slender.Engine.Devices;

namespace Slender.Yealink.Windows;

/// <summary>
/// Finds the WH64's HID interface (USB MI_03) by vendor/product and collection and opens only the telephony copy (COL02) and
/// the Teams collection (COL04), shared. COL01, COL03 and the COL05 command channel are never opened.
/// </summary>
public sealed class WindowsYealinkTransport : IYealinkTransport
{
    private const ushort TelephonyPage = 0xFF30;
    private const ushort TeamsPage = 0xFF99;

    public IYealinkLink? Open(bool writeLeds, YealinkReportHandler onReport, Action<string> log)
    {
        string? telephonyPath = null, teamsPath = null;
        foreach (var path in EnumeratePaths())
        {
            switch (Classify(path))
            {
                case YealinkCollection.Telephony: telephonyPath ??= path; break;
                case YealinkCollection.Teams: teamsPath ??= path; break;
            }
        }
        if (telephonyPath == null && teamsPath == null) return null;

        SafeFileHandle? tel = null, teams = null;
        try
        {
            CollectionCaps? caps = null;
            int teamsLength = 0;
            if (telephonyPath != null)
            {
                tel = OpenHandle(telephonyPath, writeLeds, log);
                if (tel != null)
                {
                    caps = HidpCapsReader.Read(tel, out ushort page);
                    if (caps == null || page != TelephonyPage)
                    {
                        log("Yealink: telephony collection did not match; skipped");
                        tel.Dispose(); tel = null; caps = null;
                    }
                }
            }
            if (teamsPath != null)
            {
                teams = OpenHandle(teamsPath, false, log);
                if (teams != null)
                {
                    var tc = HidpCapsReader.Read(teams, out ushort page);
                    if (tc == null || page != TeamsPage)
                    {
                        log("Yealink: Teams collection did not match; skipped");
                        teams.Dispose(); teams = null;
                    }
                    else teamsLength = tc.InputReportLength;
                }
            }
            if (tel == null && teams == null) return null;

            var link = new WindowsYealinkLink(tel, caps, caps?.InputReportLength ?? 0, teams, teamsLength, writeLeds, onReport, log);
            tel = null; teams = null;
            return link;
        }
        finally
        {
            tel?.Dispose();
            teams?.Dispose();
        }
    }

    private static SafeFileHandle? OpenHandle(string path, bool write, Action<string> log)
    {
        uint access = NativeMethods.GENERIC_READ | (write ? NativeMethods.GENERIC_WRITE : 0);
        var h = NativeMethods.CreateFile(path, access, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, 0,
            NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, 0);
        if (h.IsInvalid)
        {
            log($"Yealink: open failed (error {Marshal.GetLastWin32Error()})");
            h.Dispose();
            return null;
        }
        return h;
    }

    /// <summary>
    /// Which wanted collection an interface path is, from its text alone (nothing is opened to ask): the WH64's USB MI_03
    /// interface, COL02 or COL04. Null for everything else, so other devices and the other collections are never touched.
    /// </summary>
    internal static YealinkCollection? Classify(string interfacePath)
    {
        var p = interfacePath.ToLowerInvariant();
        if (!p.Contains($"vid_{DeviceCatalog.YealinkVendorId:x4}", StringComparison.Ordinal)
            || !p.Contains($"pid_{DeviceCatalog.YealinkWh64ProductId:x4}", StringComparison.Ordinal)
            || !p.Contains("mi_03", StringComparison.Ordinal)) return null;
        if (p.Contains("&col02", StringComparison.Ordinal)) return YealinkCollection.Telephony;
        if (p.Contains("&col04", StringComparison.Ordinal)) return YealinkCollection.Teams;
        return null;
    }

    private static IEnumerable<string> EnumeratePaths()
    {
        var paths = new List<string>();
        NativeMethods.HidD_GetHidGuid(out var guid);
        using var set = NativeMethods.SetupDiGetClassDevs(ref guid, 0, 0, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set.IsInvalid) return paths;
        for (uint i = 0; ; i++)
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref data)) break;
            var path = GetPath(set, ref data);
            if (path != null) paths.Add(path);
        }
        return paths;
    }

    private static string? GetPath(DeviceInfoSetHandle set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out uint required, 0);
        if (required < 6) return null;
        var buf = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize (8 on x64, 6 on x86), then the WCHAR path.
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref data, buf, required, out _, 0)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}

/// <summary>
/// One open session: a single thread keeps an overlapped read pending on each collection and waits on all of them plus a stop
/// event. The thread, events and read buffers exist only from Start to Dispose. A read error ends the loop and marks the link faulted.
/// </summary>
internal sealed unsafe class WindowsYealinkLink : IYealinkLink
{
    private const int WriteTimeoutMs = 2000;
    private const int JoinTimeoutMs = 3000;

    private sealed class Channel(YealinkCollection kind, SafeFileHandle handle, int length)
    {
        public readonly YealinkCollection Kind = kind;
        public readonly SafeFileHandle Handle = handle;
        public readonly int Length = Math.Max(length, 3);
        public ManualResetEvent? Event;
        public byte* Buffer;
        public NativeOverlapped* Overlapped;
        public bool Pending;
    }

    private readonly Channel[] _channels;
    private readonly SafeFileHandle? _writeHandle;
    private readonly YealinkReportHandler _onReport;
    private readonly Action<string> _log;
    private readonly object _writeLock = new();
    private readonly ManualResetEvent _stop = new(false);
    private ManualResetEvent? _writeEvent;
    private Thread? _thread;
    private volatile bool _faulted;
    private volatile bool _disposed;

    public WindowsYealinkLink(SafeFileHandle? telephony, ICollectionCaps? caps, int telephonyLength,
        SafeFileHandle? teams, int teamsLength, bool writable, YealinkReportHandler onReport, Action<string> log)
    {
        var list = new List<Channel>(2);
        if (telephony != null) list.Add(new Channel(YealinkCollection.Telephony, telephony, telephonyLength));
        if (teams != null) list.Add(new Channel(YealinkCollection.Teams, teams, teamsLength));
        _channels = [.. list];
        Telephony = telephony != null ? caps : null;
        if (writable && telephony != null)
        {
            _writeHandle = telephony;
            _writeEvent = new ManualResetEvent(false);
        }
        _onReport = onReport;
        _log = log;
    }

    public ICollectionCaps? Telephony { get; }

    public bool IsFaulted => _faulted;

    public void Start()
    {
        if (_thread != null || _disposed) return;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "Slender.Yealink" };
        _thread.Start();
    }

    private void ReadLoop()
    {
        try
        {
            foreach (var ch in _channels)
            {
                ch.Event = new ManualResetEvent(false);
                ch.Buffer = (byte*)NativeMemory.AllocZeroed((nuint)ch.Length);
                ch.Overlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            }

            var waits = new WaitHandle[_channels.Length + 1];
            for (int i = 0; i < _channels.Length; i++) waits[i] = _channels[i].Event!;
            waits[^1] = _stop;

            while (!_stop.WaitOne(0))
            {
                foreach (var ch in _channels)
                {
                    if (ch.Pending) continue;
                    ch.Event!.Reset();
                    ch.Overlapped->EventHandle = ch.Event.SafeWaitHandle.DangerousGetHandle();
                    if (!NativeMethods.ReadFile(ch.Handle, ch.Buffer, (uint)ch.Length, null, ch.Overlapped))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != NativeMethods.ERROR_IO_PENDING) throw new Win32Exception(err);
                    }
                    ch.Pending = true;
                }

                int signalled = WaitHandle.WaitAny(waits);
                if (signalled >= _channels.Length) break;

                var done = _channels[signalled];
                done.Pending = false;
                if (!NativeMethods.GetOverlappedResult(done.Handle, done.Overlapped, out uint n, false))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Deliver(done, (int)n);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed) _log($"Yealink: read loop ended: {ex.Message}");
            _faulted = true;
        }
        finally
        {
            foreach (var ch in _channels)
            {
                if (ch.Pending && ch.Overlapped != null)
                {
                    NativeMethods.CancelIoEx(ch.Handle, ch.Overlapped);
                    NativeMethods.GetOverlappedResult(ch.Handle, ch.Overlapped, out _, true);
                }
                if (ch.Overlapped != null) NativeMemory.Free(ch.Overlapped);
                if (ch.Buffer != null) NativeMemory.Free(ch.Buffer);
                ch.Overlapped = null;
                ch.Buffer = null;
                ch.Pending = false;
                ch.Event?.Dispose();
                ch.Event = null;
            }
        }
    }

    private void Deliver(Channel ch, int n)
    {
        try { _onReport(ch.Kind, new ReadOnlySpan<byte>(ch.Buffer, n)); }
        catch (Exception ex) { _log($"Yealink: report handler failed: {ex.Message}"); }
    }

    public bool Write(ReadOnlySpan<byte> report)
    {
        lock (_writeLock)
        {
            if (_disposed || _writeHandle == null || _writeEvent == null || _writeHandle.IsClosed) return false;
            var ov = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            try
            {
                _writeEvent.Reset();
                ov->EventHandle = _writeEvent.SafeWaitHandle.DangerousGetHandle();
                fixed (byte* p = report)
                {
                    if (!NativeMethods.WriteFile(_writeHandle, p, (uint)report.Length, null, ov))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != NativeMethods.ERROR_IO_PENDING) throw new Win32Exception(err);
                        if (!_writeEvent.WaitOne(WriteTimeoutMs))
                        {
                            NativeMethods.CancelIoEx(_writeHandle, ov);
                            NativeMethods.GetOverlappedResult(_writeHandle, ov, out _, true);
                            throw new TimeoutException("HID write timed out");
                        }
                    }
                    if (!NativeMethods.GetOverlappedResult(_writeHandle, ov, out _, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return true;
            }
            finally { NativeMemory.Free(ov); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Set();
        var thread = _thread;
        if (thread != null && thread != Thread.CurrentThread && !thread.Join(JoinTimeoutMs))
            _log("Yealink: read thread did not stop in time");
        lock (_writeLock)
        {
            _writeEvent?.Dispose();
            _writeEvent = null;
        }
        foreach (var ch in _channels) ch.Handle.Dispose();
        _stop.Dispose();
    }
}
