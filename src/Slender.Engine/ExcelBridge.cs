using System.Runtime.InteropServices;
using Slender.Engine.Native;

namespace Slender.Engine;

/// <summary>
/// Out-of-process Excel automation. Nothing is loaded into Excel. Every call is queued to ONE STA worker
/// (bounded 64, drop-oldest) so the hook/HID threads never wait for Excel; methods return true when queued.
/// PRIMARY path: AccessibleObjectFromWindow(EXCEL7, OBJID_NATIVEOM) -> Window.SmallScroll (hand-written IDispatch client, see ComDispatch).
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
            using var window = ComDispatch.FromWindow(excel7, NativeMethods.OBJID_NATIVEOM);
            window.CallNamedInt("SmallScroll", columns > 0 ? "ToRight" : "ToLeft", Math.Abs(columns));
            return; // PRIMARY path succeeded
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ObjectDisposedException)
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
            using var win = ComDispatch.FromWindow(excel7, NativeMethods.OBJID_NATIVEOM);
            switch (command)
            {
                case ExcelCommand.NextSheet: ActivateSibling(win, "Next"); break;
                case ExcelCommand.PreviousSheet: ActivateSibling(win, "Previous"); break;
                case ExcelCommand.ZoomIn: SetZoom(win, win.GetInt("Zoom") + 10); break;
                case ExcelCommand.ZoomOut: SetZoom(win, win.GetInt("Zoom") - 10); break;
                case ExcelCommand.ZoomReset: SetZoom(win, 100); break;
                case ExcelCommand.ScrollToFirstColumn: win.PutInt("ScrollColumn", 1); break;
                case ExcelCommand.ScrollToLastUsedColumn:
                {
                    using var sheet = win.GetObject("ActiveSheet") ?? throw new InvalidOperationException("no active sheet");
                    using var used = sheet.GetObject("UsedRange") ?? throw new InvalidOperationException("no used range");
                    using var cols = used.GetObject("Columns") ?? throw new InvalidOperationException("no columns");
                    var last = used.GetInt("Column") + cols.GetInt("Count") - 1;
                    win.PutInt("ScrollColumn", Math.Clamp(last, 1, 16384));
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or ObjectDisposedException)
        {
            _log.Info($"COM command {command} failed ({(IsBusy(ex) ? "busy" : ex.GetType().Name)})");
            // Keyboard fallback for sheet switching (Excel's own shortcuts).
            if (command is ExcelCommand.NextSheet or ExcelCommand.PreviousSheet
                && ChordParser.TryParse(command == ExcelCommand.NextSheet ? "Ctrl+PageDown" : "Ctrl+PageUp", out var keys))
                KeyInjector.SendChord(keys);
        }
    }

    private static void ActivateSibling(ComDispatch win, string which)
    {
        using var sheet = win.GetObject("ActiveSheet") ?? throw new InvalidOperationException("no active sheet");
        using var sibling = sheet.GetObject(which);
        sibling?.Call("Activate")?.Dispose();
    }

    private static void SetZoom(ComDispatch win, int z) => win.PutInt("Zoom", Math.Clamp(z, 10, 400));

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
}
