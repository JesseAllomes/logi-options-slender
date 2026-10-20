using System.Runtime.InteropServices;
using ExcelDna.Integration;
using Slender.Engine.ExcelAddin;

namespace Slender.ExcelAddin;

/// <summary>
/// What the add-in does in Excel. Runs on Excel's main thread only (queued through ExcelAsyncUtil.QueueAsMacro). Late-bound COM
/// ("dynamic"), so no Office interop assemblies ship. The choices (next visible sheet, zoom presets, cycles) are in ExcelAddinLogic.
/// </summary>
internal sealed class ExcelActions
{
    private const int XlSheetVisible = -1;

    public AddinReply Run(AddinRequest request)
    {
        try
        {
            dynamic app = ExcelDnaUtil.Application;
            return request.Verb switch
            {
                AddinVerb.NextSheet => Reply(request, StepSheets(app, 1)),
                AddinVerb.PreviousSheet => Reply(request, StepSheets(app, -1)),
                AddinVerb.ZoomIn => Reply(request, Zoom(app, direction: 1)),
                AddinVerb.ZoomOut => Reply(request, Zoom(app, direction: -1)),
                AddinVerb.ZoomReset => Reply(request, Zoom(app, direction: 0)),
                AddinVerb.TableStyle => Reply(request, CycleTableStyle(app)),
                AddinVerb.ChartType => Reply(request, CycleChartType(app)),
                AddinVerb.Wheel => Reply(request, Wheel(app, int.Parse(request.Arg!, System.Globalization.CultureInfo.InvariantCulture))),
                _ => new AddinReply(request.Id, AddinStatus.BadRequest),
            };
        }
        catch (COMException ex) when (IsBusy(ex)) { return new AddinReply(request.Id, AddinStatus.Busy); }
        catch { return new AddinReply(request.Id, AddinStatus.Failed); }
    }

    /// <summary>Starts the macro. Errors (no such macro, the macro's own failure) stay in Excel; Slender was already told it started.</summary>
    public void RunMacro(string name)
    {
        try
        {
            dynamic app = ExcelDnaUtil.Application;
            app.Run(name);
        }
        catch { }
    }

    private static AddinReply Reply(AddinRequest request, bool done) =>
        new(request.Id, done ? AddinStatus.Ok : AddinStatus.NotApplicable);

    /// <summary>Next or previous visible sheet (hidden and very hidden skipped). False when there is nowhere to go.</summary>
    private static bool StepSheets(dynamic app, int step, int count = 1)
    {
        dynamic? book = app.ActiveWorkbook;
        if (book is null) return false;
        dynamic sheets = book.Sheets;
        int n = sheets.Count;
        var visible = new List<bool>(n);
        for (var i = 1; i <= n; i++) visible.Add((int)sheets[i].Visible == XlSheetVisible);
        var current = (int)app.ActiveSheet.Index - 1;
        var moved = false;
        for (var k = 0; k < count; k++)
        {
            var next = ExcelAddinLogic.NextVisibleSheet(visible, current, step);
            if (next < 0) break;
            current = next;
            moved = true;
        }
        if (!moved) return false;
        sheets[current + 1].Activate();
        return true;
    }

    private static bool Zoom(dynamic app, int direction)
    {
        dynamic? window = app.ActiveWindow;
        if (window is null) return false;
        window.Zoom = direction == 0 ? 100 : ExcelAddinLogic.NextZoom((int)window.Zoom, direction);
        return true;
    }

    /// <summary>Next built-in style for the table (ListObject) holding the active cell. Not applicable outside a table.</summary>
    private static bool CycleTableStyle(dynamic app)
    {
        dynamic? cell = app.ActiveCell;
        dynamic? table = cell?.ListObject;
        if (table is null) return false;
        string current;
        try { current = (string)table.TableStyle.Name; } catch { current = string.Empty; }
        table.TableStyle = ExcelAddinLogic.NextInCycle(ExcelAddinLogic.TableStyles, current);
        return true;
    }

    /// <summary>Next chart type for the selected chart (embedded or chart sheet). Not applicable when no chart is selected.</summary>
    private static bool CycleChartType(dynamic app)
    {
        dynamic? chart = app.ActiveChart;
        if (chart is null) return false;
        chart.ChartType = ExcelAddinLogic.NextInCycle(ExcelAddinLogic.ChartTypes, (int)chart.ChartType);
        return true;
    }

    /// <summary>Cells selected: scroll columns. A chart or shape selected: step through sheets.</summary>
    private static bool Wheel(dynamic app, int columns)
    {
        var target = ExcelAddinLogic.WheelTargetFor(SelectionIsRange(app));
        if (target == WheelTarget.Sheets) return StepSheets(app, columns > 0 ? 1 : -1, Math.Min(Math.Abs(columns), 5));
        dynamic? window = app.ActiveWindow;
        if (window is null) return false;
        if (columns > 0) window.SmallScroll(ToRight: columns);
        else window.SmallScroll(ToLeft: -columns);
        return true;
    }

    private static bool SelectionIsRange(dynamic app)
    {
        try
        {
            dynamic? selection = app.Selection;
            if (selection is null) return false;
            _ = selection.Address; // only a Range has an Address; charts, shapes and pictures do not
            return true;
        }
        catch { return false; }
    }

    /// <summary>RPC_E_CALLREJECTED, RPC_E_SERVERCALL_RETRYLATER, VBA_E_IGNORE: Excel is in edit mode or showing a dialog.</summary>
    private static bool IsBusy(COMException ex) => (uint)ex.HResult is 0x80010001 or 0x8001010A or 0x800AC472;
}
