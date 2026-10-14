namespace Slender.Engine;

/// <summary>SendInput-based executor. Excel actions go to the bridge with the foreground window.</summary>
public sealed class ActionExecutor : IActionExecutor
{
    private readonly IExcelBridge _excel;
    private readonly IForegroundApp _foreground;
    private readonly ILog _log;

    public ActionExecutor(IExcelBridge excel, IForegroundApp foreground, ILog? log = null)
    {
        _excel = excel;
        _foreground = foreground;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised on press of an ActionsRing action (UI arrives in m6).</summary>
    public event EventHandler? ActionsRingRequested;

    public void Execute(ActionSpec action, bool pressed)
    {
        try
        {
            switch (action)
            {
                case ActionSpec.Keys k:
                    // One-shot: fire on press only.
                    if (pressed)
                    {
                        if (!ChordParser.TryParse(k.Chord, out var keys)) _log.Error($"bad chord '{k.Chord}'");
                        else if (!KeyInjector.SendChord(keys)) _log.Error($"SendInput failed for '{k.Chord}'");
                    }
                    break;
                case ActionSpec.MouseButton m:
                    if (!KeyInjector.SendMouseButton(m.Button, pressed)) _log.Error($"mouse button '{m.Button}' failed");
                    break;
                case ActionSpec.Excel e:
                    if (pressed && !_excel.TryRunCommand(_foreground.WindowHandle, e.Command)) _log.Info($"excel command {e.Command} not possible");
                    break;
                case ActionSpec.ActionsRing:
                    if (pressed) ActionsRingRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex) { _log.Error("action failed", ex); }
    }
}
