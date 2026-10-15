using System.Runtime.InteropServices;

namespace Slender.Engine;

/// <summary>
/// Real monitors. External displays: DDC/CI through dxva2 (GetMonitorBrightness / SetMonitorBrightness). The laptop's internal
/// panel (found with QueryDisplayConfig output technology) uses the video driver's brightness IOCTLs on "\\.\LCD", the same
/// driver interface behind WmiMonitorBrightnessMethods. WMI itself is not used: System.Management is not NativeAOT-safe and a raw
/// IWbemServices COM binding is far more code than three DeviceIoControl calls. Pure P/Invoke.
/// </summary>
internal sealed unsafe class WindowsMonitorBrightness : IMonitorProvider
{
    public nint MonitorAtCursor() =>
        GetCursorPos(out var pt) ? MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */) : 0;

    public IBrightnessDevice? Open(nint monitor)
    {
        if (IsInternalPanel(monitor)) return LcdBrightness.TryOpen();
        return DdcBrightness.TryOpen(monitor);
    }

    // ---- internal panel detection ----

    private static bool IsInternalPanel(nint monitor)
    {
        var info = new MonitorInfoEx { CbSize = (uint)sizeof(MonitorInfoEx) };
        if (!GetMonitorInfoW(monitor, ref info)) return false;
        var gdiName = new string(info.SzDevice);

        if (GetDisplayConfigBufferSizes(2 /* QDC_ONLY_ACTIVE_PATHS */, out var np, out var nm) != 0 || np == 0) return false;
        var paths = new PathInfo[np];
        var modes = new byte[Math.Max(1, (int)nm) * 64]; // DISPLAYCONFIG_MODE_INFO is 64 bytes; contents unused
        fixed (PathInfo* pPaths = paths)
        fixed (byte* pModes = modes)
        {
            if (QueryDisplayConfig(2, ref np, pPaths, ref nm, pModes, 0) != 0) return false;
        }
        for (var i = 0; i < np; i++)
        {
            var name = new SourceName { Type = 1, Size = (uint)sizeof(SourceName), AdapterLo = paths[i].SrcAdapterLo, AdapterHi = paths[i].SrcAdapterHi, Id = paths[i].SrcId };
            if (DisplayConfigGetDeviceInfo(&name) != 0) continue;
            if (!string.Equals(new string(name.GdiName), gdiName, StringComparison.OrdinalIgnoreCase)) continue;
            return paths[i].OutputTechnology is 6 /* LVDS */ or 11 /* DP embedded */ or 13 /* UDI embedded */ or 0x80000000 /* INTERNAL */;
        }
        return false;
    }

    // ---- DDC/CI ----

    private sealed class DdcBrightness : IBrightnessDevice
    {
        private readonly PhysicalMonitor[] _monitors;
        private uint _min, _max;

        private DdcBrightness(PhysicalMonitor[] monitors) => _monitors = monitors;

        public static DdcBrightness? TryOpen(nint monitor)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return null;
            var array = new PhysicalMonitor[count];
            fixed (PhysicalMonitor* p = array)
            {
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, p)) return null;
            }
            return new DdcBrightness(array);
        }

        public bool TryGet(out int percent)
        {
            percent = 0;
            foreach (var m in _monitors)
            {
                if (!GetMonitorBrightness(m.Handle, out var min, out var cur, out var max) || max <= min) continue;
                _min = min; _max = max;
                percent = (int)Math.Round((cur - min) * 100.0 / (max - min));
                return true;
            }
            return false;
        }

        public bool TrySet(int percent)
        {
            var raw = (uint)Math.Round(_min + percent / 100.0 * (_max - _min));
            var ok = false;
            foreach (var m in _monitors)
                if (SetMonitorBrightness(m.Handle, raw)) ok = true;
            return ok;
        }

        public void Dispose()
        {
            fixed (PhysicalMonitor* p = _monitors) DestroyPhysicalMonitors((uint)_monitors.Length, p);
        }
    }

    // ---- internal panel (video driver IOCTLs) ----

    private sealed class LcdBrightness : IBrightnessDevice
    {
        private const uint QuerySupported = 0x230496, QueryDisplay = 0x230498, SetDisplay = 0x23049C;
        private readonly nint _handle;
        private byte[] _levels = [];

        private LcdBrightness(nint handle) => _handle = handle;

        public static LcdBrightness? TryOpen()
        {
            var h = CreateFileW("\\\\.\\LCD", 0xC0000000 /* GENERIC_READ | GENERIC_WRITE */, 3, 0, 3 /* OPEN_EXISTING */, 0, 0);
            return h is 0 or -1 ? null : new LcdBrightness(h);
        }

        public bool TryGet(out int percent)
        {
            percent = 0;
            var levels = new byte[256];
            if (!IoctlOut(_handle, QuerySupported, levels, out var n) || n == 0) return false;
            _levels = levels[..(int)n];
            var current = new byte[3]; // DISPLAY_BRIGHTNESS: policy, AC level, DC level
            if (!IoctlOut(_handle, QueryDisplay, current, out n) || n < 3) return false;
            percent = current[1];
            return true;
        }

        public bool TrySet(int percent)
        {
            var level = _levels.Length == 0 ? (byte)percent : _levels.MinBy(l => Math.Abs(l - percent));
            var value = new byte[] { 3 /* DISPLAYPOLICY_BOTH */, level, level };
            return IoctlIn(_handle, SetDisplay, value);
        }

        public void Dispose() => CloseHandle(_handle);

        private static bool IoctlOut(nint h, uint code, byte[] buffer, out uint returned)
        {
            fixed (byte* p = buffer) return DeviceIoControl(h, code, null, 0, p, (uint)buffer.Length, out returned, 0);
        }

        private static bool IoctlIn(nint h, uint code, byte[] buffer)
        {
            fixed (byte* p = buffer) return DeviceIoControl(h, code, p, (uint)buffer.Length, null, 0, out _, 0);
        }
    }

    // ---- native ----

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint CbSize;
        public int L, T, R, B, WL, WT, WR, WB;
        public uint Flags;
        public fixed char SzDevice[32];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PhysicalMonitor
    {
        public nint Handle;
        public fixed char Description[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo // DISPLAYCONFIG_PATH_INFO, 72 bytes
    {
        public uint SrcAdapterLo, SrcAdapterHi, SrcId, SrcModeIdx, SrcStatus;
        public uint TgtAdapterLo, TgtAdapterHi, TgtId, TgtModeIdx, OutputTechnology, Rotation, Scaling;
        public uint RefreshNum, RefreshDen, ScanLine, TargetAvailable, TgtStatus, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SourceName // DISPLAYCONFIG_SOURCE_DEVICE_NAME, 84 bytes
    {
        public uint Type, Size, AdapterLo, AdapterHi, Id;
        public fixed char GdiName[32];
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, PathInfo* pathArray, ref uint modes, byte* modeArray, nint topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(SourceName* packet);

    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, PhysicalMonitor* array);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor* array);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(nint physical, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(nint physical, uint value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")]
    private static extern bool DeviceIoControl(nint h, uint code, byte* inBuf, uint inSize, byte* outBuf, uint outSize, out uint returned, nint overlapped);
}
