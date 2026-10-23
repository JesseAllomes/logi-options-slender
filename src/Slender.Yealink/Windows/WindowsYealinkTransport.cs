using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Slender.Engine.Devices;

namespace Slender.Yealink.Windows;

/// <summary>
/// Finds the WH64's HID interface (USB MI_03) by vendor/product and collection and opens, shared: the telephony copy (COL02), the
/// Teams collection (COL04), the standard telephony collection (COL01) for input only, and, only when vendor reads are on, the
/// vendor channel (COL05) for reads and allowlisted requests. The consumer keys (COL03) are opened, read only, only when asked for; COL01 is never written.
/// </summary>
public sealed class WindowsYealinkTransport : IYealinkTransport
{
    private const ushort TelephonyPage = 0xFF30;
    private const ushort TeamsPage = 0xFF99;
    private const ushort StandardPage = 0x000B;
    private const ushort VendorPage = 0xFF00;
    private const ushort ConsumerPage = 0x000C;

    public IYealinkLink? Open(bool writeLeds, bool vendorReads, YealinkReportHandler onReport, Action<string> log, bool consumerKeys = false)
    {
        string? telephonyPath = null, teamsPath = null, standardPath = null, vendorPath = null, consumerPath = null;
        foreach (var path in EnumeratePaths())
        {
            switch (Classify(path))
            {
                case YealinkCollection.Telephony: telephonyPath ??= path; break;
                case YealinkCollection.Teams: teamsPath ??= path; break;
                case YealinkCollection.Standard: standardPath ??= path; break;
                case YealinkCollection.Vendor: if (vendorReads) vendorPath ??= path; break;
                case YealinkCollection.ConsumerKeys: if (consumerKeys) consumerPath ??= path; break;
            }
        }
        if (telephonyPath == null && teamsPath == null && standardPath == null && vendorPath == null && consumerPath == null) return null;

        SafeFileHandle? tel = null, teams = null, std = null, vendor = null, consumer = null;
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
            CollectionCaps? stdCaps = null;
            if (standardPath != null)
            {
                std = OpenHandle(standardPath, false, log);
                if (std != null)
                {
                    stdCaps = HidpCapsReader.Read(std, out ushort page);
                    if (stdCaps == null || page != StandardPage)
                    {
                        log("Yealink: standard telephony collection did not match; skipped");
                        std.Dispose(); std = null; stdCaps = null;
                    }
                }
            }
            int consumerLength = 0;
            if (consumerPath != null)
            {
                consumer = OpenHandle(consumerPath, false, log);
                if (consumer != null)
                {
                    var cc = HidpCapsReader.Read(consumer, out ushort page);
                    if (cc == null || page != ConsumerPage)
                    {
                        log("Yealink: consumer keys collection did not match; skipped");
                        consumer.Dispose(); consumer = null;
                    }
                    else consumerLength = cc.InputReportLength;
                }
            }
            int vendorIn = 0, vendorOut = 0;
            if (vendorPath != null)
            {
                vendor = OpenHandle(vendorPath, true, log);
                if (vendor != null)
                {
                    var vc = HidpCapsReader.Read(vendor, out ushort page);
                    if (vc == null || page != VendorPage || vc.InputReportLength < VendorFrame.Length || vc.OutputReportLength < VendorFrame.Length)
                    {
                        log("Yealink: vendor collection did not match; skipped");
                        vendor.Dispose(); vendor = null;
                    }
                    else { vendorIn = vc.InputReportLength; vendorOut = vc.OutputReportLength; }
                }
            }
            if (tel == null && teams == null && std == null && vendor == null && consumer == null) return null;

            var link = new WindowsYealinkLink(new LinkParts
            {
                Telephony = tel, TelephonyCaps = caps, TelephonyLength = caps?.InputReportLength ?? 0,
                Teams = teams, TeamsLength = teamsLength,
                Standard = std, StandardCaps = stdCaps, StandardLength = stdCaps?.InputReportLength ?? 0,
                Consumer = consumer, ConsumerLength = consumerLength,
                Vendor = vendor, VendorLength = vendorIn, VendorOutLength = vendorOut,
                WriteLeds = writeLeds,
            }, onReport, log);
            tel = null; teams = null; std = null; vendor = null; consumer = null;
            return link;
        }
        finally
        {
            tel?.Dispose();
            teams?.Dispose();
            std?.Dispose();
            vendor?.Dispose();
            consumer?.Dispose();
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
    /// interface, COL01, COL02, COL03, COL04 or COL05. Null for everything else, so other devices and the other collections are never touched.
    /// </summary>
    internal static YealinkCollection? Classify(string interfacePath)
    {
        var p = interfacePath.ToLowerInvariant();
        if (!p.Contains($"vid_{DeviceCatalog.YealinkVendorId:x4}", StringComparison.Ordinal)
            || !p.Contains($"pid_{DeviceCatalog.YealinkWh64ProductId:x4}", StringComparison.Ordinal)
            || !p.Contains("mi_03", StringComparison.Ordinal)) return null;
        if (p.Contains("&col01", StringComparison.Ordinal)) return YealinkCollection.Standard;
        if (p.Contains("&col02", StringComparison.Ordinal)) return YealinkCollection.Telephony;
        if (p.Contains("&col03", StringComparison.Ordinal)) return YealinkCollection.ConsumerKeys;
        if (p.Contains("&col04", StringComparison.Ordinal)) return YealinkCollection.Teams;
        if (p.Contains("&col05", StringComparison.Ordinal)) return YealinkCollection.Vendor;
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
/// What the transport opened for one link; null handles are collections that are absent or were skipped.
/// </summary>
internal sealed class LinkParts
{
    public SafeFileHandle? Telephony, Teams, Standard, Vendor, Consumer;
    public CollectionCaps? TelephonyCaps, StandardCaps;
    public int TelephonyLength, TeamsLength, StandardLength, VendorLength, VendorOutLength, ConsumerLength;
    public bool WriteLeds;
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
    private readonly SafeFileHandle? _writeHandle, _vendorHandle;
    private readonly int _vendorOutLength;
    private readonly YealinkReportHandler _onReport;
    private readonly Action<string> _log;
    private readonly object _writeLock = new();
    private readonly ManualResetEvent _stop = new(false);
    private ManualResetEvent? _writeEvent;
    private Thread? _thread;
    private volatile bool _faulted;
    private volatile bool _disposed;

    public WindowsYealinkLink(LinkParts parts, YealinkReportHandler onReport, Action<string> log)
    {
        var list = new List<Channel>(5);
        if (parts.Telephony != null) list.Add(new Channel(YealinkCollection.Telephony, parts.Telephony, parts.TelephonyLength));
        if (parts.Teams != null) list.Add(new Channel(YealinkCollection.Teams, parts.Teams, parts.TeamsLength));
        if (parts.Standard != null) list.Add(new Channel(YealinkCollection.Standard, parts.Standard, parts.StandardLength));
        if (parts.Vendor != null) list.Add(new Channel(YealinkCollection.Vendor, parts.Vendor, parts.VendorLength));
        if (parts.Consumer != null) list.Add(new Channel(YealinkCollection.ConsumerKeys, parts.Consumer, parts.ConsumerLength));
        _channels = [.. list];
        Telephony = parts.Telephony != null ? parts.TelephonyCaps : null;
        Standard = parts.Standard != null ? parts.StandardCaps : null;
        if (parts.WriteLeds && parts.Telephony != null) _writeHandle = parts.Telephony;
        if (parts.Vendor != null)
        {
            _vendorHandle = parts.Vendor;
            _vendorOutLength = parts.VendorOutLength;
        }
        if (_writeHandle != null || _vendorHandle != null) _writeEvent = new ManualResetEvent(false);
        _onReport = onReport;
        _log = log;
    }

    public ICollectionCaps? Telephony { get; }

    public ICollectionCaps? Standard { get; }

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

    public bool Write(ReadOnlySpan<byte> report) => WriteCore(_writeHandle, report);

    /// <summary>Pads the request with zeros to the collection's output report length (the request is 64 bytes, usually the same).</summary>
    public bool WriteVendor(ReadOnlySpan<byte> report)
    {
        if (_vendorHandle == null || report.Length > _vendorOutLength || _vendorOutLength > 512) return false;
        if (report.Length == _vendorOutLength) return WriteCore(_vendorHandle, report);
        Span<byte> padded = stackalloc byte[_vendorOutLength];
        report.CopyTo(padded);
        return WriteCore(_vendorHandle, padded);
    }

    private bool WriteCore(SafeFileHandle? handle, ReadOnlySpan<byte> report)
    {
        lock (_writeLock)
        {
            if (_disposed || handle == null || _writeEvent == null || handle.IsClosed) return false;
            var ov = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            try
            {
                _writeEvent.Reset();
                ov->EventHandle = _writeEvent.SafeWaitHandle.DangerousGetHandle();
                fixed (byte* p = report)
                {
                    if (!NativeMethods.WriteFile(handle, p, (uint)report.Length, null, ov))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != NativeMethods.ERROR_IO_PENDING) throw new Win32Exception(err);
                        if (!_writeEvent.WaitOne(WriteTimeoutMs))
                        {
                            NativeMethods.CancelIoEx(handle, ov);
                            NativeMethods.GetOverlappedResult(handle, ov, out _, true);
                            throw new TimeoutException("HID write timed out");
                        }
                    }
                    if (!NativeMethods.GetOverlappedResult(handle, ov, out _, false))
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
