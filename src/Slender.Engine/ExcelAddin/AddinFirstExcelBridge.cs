using Slender.Engine.Native;

namespace Slender.Engine.ExcelAddin;

/// <summary>
/// Excel actions through the Slender add-in when it answers inside its time box, and through the out-of-process bridge (today's
/// COM and key-based behaviour) when it does not. Callers return at once: the pipe round trip runs on this class's own worker, never
/// on the hook or HID thread. Commands only the add-in can do (table style, chart type, macros) have no fallback and report
/// "not possible" in the log.
/// </summary>
public sealed class AddinFirstExcelBridge : IExcelBridge, IDisposable
{
    private readonly IExcelBridge _fallback;
    private readonly ExcelAddinClient _client;
    private readonly Func<nint, int> _processIdOf;
    private readonly IWorkQueue _queue;
    private readonly ILog _log;

    public AddinFirstExcelBridge(IExcelBridge fallback, ExcelAddinClient client, ILog? log = null)
        : this(fallback, client, WindowProcessId, new BoundedWorker("Slender.ExcelAddinPipe", 16, sta: false, log), log) { }

    internal AddinFirstExcelBridge(IExcelBridge fallback, ExcelAddinClient client, Func<nint, int> processIdOf, IWorkQueue queue, ILog? log = null)
    {
        _fallback = fallback;
        _client = client;
        _processIdOf = processIdOf;
        _queue = queue;
        _log = log ?? NullLog.Instance;
    }

    public bool TryScrollColumns(nint excelWindow, int columns)
    {
        if (excelWindow == 0 || columns == 0) return false;
        _queue.Post(() =>
        {
            var arg = Math.Clamp(columns, -ExcelAddinProtocol.MaxWheelColumns, ExcelAddinProtocol.MaxWheelColumns).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_client.Send(_processIdOf(excelWindow), AddinVerb.Wheel, arg) != AddinOutcome.Handled) _fallback.TryScrollColumns(excelWindow, columns);
        });
        return true;
    }

    public bool TryRunCommand(nint excelWindow, ExcelCommand command)
    {
        if (excelWindow == 0) return false;
        var verb = VerbFor(command);
        if (verb is null) return _fallback.TryRunCommand(excelWindow, command); // not an add-in action: the bridge queues it itself
        _queue.Post(() =>
        {
            if (_client.Send(_processIdOf(excelWindow), verb.Value) == AddinOutcome.Handled) return;
            if (!AddinOnly(command)) _fallback.TryRunCommand(excelWindow, command);
            else _log.Info($"excel command {command} needs the Slender Excel add-in, which did not answer");
        });
        return true;
    }

    public bool TryRunMacro(nint excelWindow, string macroName)
    {
        if (excelWindow == 0 || !ExcelAddinProtocol.IsValidMacroName(macroName)) return false;
        _queue.Post(() =>
        {
            if (_client.Send(_processIdOf(excelWindow), AddinVerb.Macro, macroName) != AddinOutcome.Handled)
                _log.Info("excel macro not run: the Slender Excel add-in did not answer");
        });
        return true;
    }

    public void Dispose() => _queue.Dispose();

    /// <summary>The add-in verb for a command, or null when the add-in has no verb for it (first and last column stay with the bridge).</summary>
    internal static AddinVerb? VerbFor(ExcelCommand command) => command switch
    {
        ExcelCommand.NextSheet => AddinVerb.NextSheet,
        ExcelCommand.PreviousSheet => AddinVerb.PreviousSheet,
        ExcelCommand.ZoomIn => AddinVerb.ZoomIn,
        ExcelCommand.ZoomOut => AddinVerb.ZoomOut,
        ExcelCommand.ZoomReset => AddinVerb.ZoomReset,
        ExcelCommand.CycleTableStyle => AddinVerb.TableStyle,
        ExcelCommand.CycleChartType => AddinVerb.ChartType,
        _ => null,
    };

    internal static bool AddinOnly(ExcelCommand command) => command is ExcelCommand.CycleTableStyle or ExcelCommand.CycleChartType;

    private static int WindowProcessId(nint hwnd)
    {
        if (hwnd == 0) return 0;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }
}
