using System.Reflection;
using System.Runtime.InteropServices;
using Slender.Engine.Native;

namespace Slender.Engine;

/// <summary>
/// Out-of-process Excel automation. Nothing is loaded into Excel. Every call is queued to ONE STA worker
/// (bounded 64, drop-oldest) so the hook/HID threads never wait for Excel; methods return true when queued.
/// PRIMARY path: AccessibleObjectFromWindow(EXCEL7, OBJID_NATIVEOM) -> Window.SmallScroll (late-bound COM).
/// FALLBACK path: PostMessage WM_HSCROLL to EXCEL7 when Excel is busy (edit mode, call rejected) or COM fails.
/// Which path feels better is to be confirmed on device.
/// </summary>
public sealed class ExcelBridge : IExcelBridge, IDisposable
{
    private const int MaxFallbackLines = 50;
    private readonly BoundedWorker _worker;
    private readonly ILog _log;

    public ExcelBridge(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _worker = new BoundedWorker("Slender.ExcelSta", 64, sta: true, _log);
    }

    public bool TryScrollColumns(nint excelWindow, int columns)
    {
        if (excelWindow == 0 || columns == 0) return false;
        _worker.Post(() => ScrollOnWorker(excelWindow, columns));
        return true;
    }

    public bool TryRunCommand(nint excelWindow, ExcelCommand command)
    {
        if (excelWindow == 0) return false;
        _worker.Post(() => CommandOnWorker(excelWindow, command));
        return true;
    }

    public void Dispose() => _worker.Dispose();

    // ---- worker-thread code below ----

    private void ScrollOnWorker(nint hwnd, int columns)
    {
        var excel7 = FindExcel7(hwnd);
        if (excel7 == 0) return;
        try
        {
            using var scope = new ComScope();
            var window = scope.Track(AcquireWindow(excel7))!;
            window.GetType().InvokeMember("SmallScroll", BindingFlags.InvokeMethod, null, window,
                [Math.Abs(columns)], null, null, [columns > 0 ? "ToRight" : "ToLeft"]);
            return; // PRIMARY path succeeded
        }
        catch (Exception ex)
        {
            _log.Info($"COM scroll failed ({(IsBusy(ex) ? "busy" : ex.GetType().Name)}); using WM_HSCROLL fallback");
        }
        // FALLBACK path
        var msg = columns > 0 ? NativeMethods.SB_LINERIGHT : NativeMethods.SB_LINELEFT;
        for (var i = 0; i < Math.Min(Math.Abs(columns), MaxFallbackLines); i++)
            NativeMethods.PostMessage(excel7, NativeMethods.WM_HSCROLL, msg, 0);
    }

    private void CommandOnWorker(nint hwnd, ExcelCommand command)
    {
        try
        {
            var excel7 = FindExcel7(hwnd);
            if (excel7 == 0) throw new InvalidOperationException("no EXCEL7 window");
            using var scope = new ComScope();
            var win = scope.Track(AcquireWindow(excel7))!;
            switch (command)
            {
                case ExcelCommand.NextSheet: ActivateSibling(scope, win, "Next"); break;
                case ExcelCommand.PreviousSheet: ActivateSibling(scope, win, "Previous"); break;
                case ExcelCommand.ZoomIn: SetZoom(win, Zoom(win) + 10); break;
                case ExcelCommand.ZoomOut: SetZoom(win, Zoom(win) - 10); break;
                case ExcelCommand.ZoomReset: SetZoom(win, 100); break;
                case ExcelCommand.ScrollToFirstColumn: Put(win, "ScrollColumn", 1); break;
                case ExcelCommand.ScrollToLastUsedColumn:
                {
                    var sheet = scope.Track(Get(win, "ActiveSheet"))!;
                    var used = scope.Track(Get(sheet, "UsedRange"))!;
                    var cols = scope.Track(Get(used, "Columns"))!;
                    var last = Convert.ToInt32(Get(used, "Column")) + Convert.ToInt32(Get(cols, "Count")) - 1;
                    Put(win, "ScrollColumn", Math.Clamp(last, 1, 16384));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Info($"COM command {command} failed ({(IsBusy(ex) ? "busy" : ex.GetType().Name)})");
            // Keyboard fallback for sheet switching (Excel's own shortcuts).
            if (command is ExcelCommand.NextSheet or ExcelCommand.PreviousSheet
                && ChordParser.TryParse(command == ExcelCommand.NextSheet ? "Ctrl+PageDown" : "Ctrl+PageUp", out var keys))
                KeyInjector.SendChord(keys);
        }
    }

    private static void ActivateSibling(ComScope scope, object win, string which)
    {
        var sheet = scope.Track(Get(win, "ActiveSheet"))!;
        var sibling = scope.Track(Get(sheet, which));
        if (sibling is not null) sibling.GetType().InvokeMember("Activate", BindingFlags.InvokeMethod, null, sibling, null);
    }

    private static int Zoom(object win) => Convert.ToInt32(Get(win, "Zoom"));
    private static void SetZoom(object win, int z) => Put(win, "Zoom", Math.Clamp(z, 10, 400));

    private static object? Get(object o, string name) =>
        o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);

    private static void Put(object o, string name, object value) =>
        o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, [value]);

    private static object AcquireWindow(nint excel7)
    {
        var iid = new Guid("00020400-0000-0000-C000-000000000046"); // IID_IDispatch
        var hr = NativeMethods.AccessibleObjectFromWindow(excel7, NativeMethods.OBJID_NATIVEOM, ref iid, out var obj);
        if (hr != 0 || obj is null) throw new COMException("AccessibleObjectFromWindow failed", hr);
        return obj;
    }

    /// <summary>Walks to the root window, requires class XLMAIN, then finds a visible EXCEL7 descendant.</summary>
    internal static nint FindExcel7(nint hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == 0) root = hwnd;
        var cls = NativeMethods.ClassNameOf(root);
        if (cls == "EXCEL7") return root;
        if (cls != "XLMAIN") return 0;
        nint found = 0;
        NativeMethods.EnumChildWindows(root, (child, _) =>
        {
            if (NativeMethods.ClassNameOf(child) == "EXCEL7" && NativeMethods.IsWindowVisible(child)) { found = child; return false; }
            return true;
        }, 0);
        return found;
    }

    /// <summary>RPC_E_CALLREJECTED, RPC_E_SERVERCALL_RETRYLATER, VBA_E_IGNORE (Excel in edit mode).</summary>
    internal static bool IsBusy(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is COMException c && (uint)c.HResult is 0x80010001 or 0x8001010A or 0x800AC472) return true;
        return false;
    }

    private sealed class ComScope : IDisposable
    {
        private readonly List<object> _refs = [];

        public object? Track(object? o)
        {
            if (o is not null && Marshal.IsComObject(o)) _refs.Add(o);
            return o;
        }

        public void Dispose()
        {
            for (var i = _refs.Count - 1; i >= 0; i--)
                try { Marshal.FinalReleaseComObject(_refs[i]); } catch { }
            _refs.Clear();
        }
    }
}
