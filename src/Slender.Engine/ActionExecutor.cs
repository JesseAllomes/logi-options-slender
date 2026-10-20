namespace Slender.Engine;

/// <summary>Opens a target through the Windows shell. Abstracted so tests never launch anything.</summary>
public interface IShellLauncher
{
    void Open(string target, string? arguments);
}

/// <summary>ShellExecute via Process.Start: exe, file, folder, URL or shell:AppsFolder\AUMID.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void Open(string target, string? arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = WorkingDirectoryFor(target) };
        if (!string.IsNullOrWhiteSpace(arguments)) info.Arguments = arguments;
        using var _ = System.Diagnostics.Process.Start(info);
    }

    /// <summary>
    /// The launched app's own folder, else the user's profile; never Slender's install folder. A child that kept that folder as
    /// its working directory (OneNote opened from a ring slot) blocked every update until it closed (user 2026-10-19).
    /// </summary>
    public static string WorkingDirectoryFor(string target)
    {
        try
        {
            if (Path.IsPathFullyQualified(target) && Path.GetDirectoryName(target) is { Length: > 0 } dir && Directory.Exists(dir)) return dir;
        }
        catch (ArgumentException) { }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}

/// <summary>SendInput-based executor. Excel actions go to the bridge with the foreground window.</summary>
public sealed class ActionExecutor : IActionExecutor
{
    private readonly IExcelBridge _excel;
    private readonly IForegroundApp _foreground;
    private readonly ILog _log;
    private readonly IShellLauncher _shell;
    private readonly IBrightnessController _brightness;
    private readonly ITextSender _text;
    private readonly Func<IReadOnlyList<KeyStroke>, bool> _sendChord;
    private readonly Func<IReadOnlyList<KeyEvent>, bool> _sendEvents;

    public bool SupportsKeyEvents => true;

    public bool SendKeyEvents(IReadOnlyList<KeyEvent> events) => events.Count == 0 || _sendEvents(events);

    public bool SendHorizontalWheel(int delta) => KeyInjector.SendHorizontalWheel(delta);

    public bool SendVerticalWheel(int delta) => KeyInjector.SendVerticalWheel(delta);

    public ActionExecutor(IExcelBridge excel, IForegroundApp foreground, ILog? log = null, IShellLauncher? shell = null, IBrightnessController? brightness = null, ITextSender? text = null,
        Func<IReadOnlyList<KeyStroke>, bool>? sendChord = null, Func<IReadOnlyList<KeyEvent>, bool>? sendEvents = null)
    {
        _sendChord = sendChord ?? KeyInjector.SendChord;
        _sendEvents = sendEvents ?? KeyInjector.SendKeyEvents;
        _text = text ?? new SendInputTextSender();
        _shell = shell ?? new ShellLauncher();
        _brightness = brightness ?? new BrightnessController(new WindowsMonitorBrightness(), log);
        _excel = excel;
        _foreground = foreground;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised on press of an ActionsRing action (UI arrives in m6).</summary>
    public event EventHandler? ActionsRingRequested;

    /// <summary>Pause between the chords of a sequence so the target app sees them as separate key presses.</summary>
    private const int SequenceGapMs = 30;

    private void SendSteps(string chord, IReadOnlyList<IReadOnlyList<KeyStroke>> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0) Thread.Sleep(SequenceGapMs);
            if (!_sendChord(steps[i])) { _log.Error($"SendInput failed for '{chord}'"); return; }
        }
    }

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
                        if (!ChordParser.TryParseSequence(k.Chord, out var steps)) _log.Error($"bad chord '{k.Chord}'");
                        else SendSteps(k.Chord, steps);
                    }
                    break;
                case ActionSpec.MouseButton m:
                    if (!KeyInjector.SendMouseButton(m.Button, pressed)) _log.Error($"mouse button '{m.Button}' failed");
                    break;
                case ActionSpec.Excel e:
                    if (pressed && !_excel.TryRunCommand(_foreground.WindowHandle, e.Command)) _log.Info($"excel command {e.Command} not possible");
                    break;
                case ActionSpec.Launch l:
                    if (pressed)
                    {
                        if (string.IsNullOrWhiteSpace(l.Target)) _log.Error("launch target is empty");
                        else
                        {
                            try { _shell.Open(l.Target, l.Arguments); }
                            catch (Exception ex) { _log.Error($"launch '{l.Target}' failed", ex); }
                        }
                    }
                    break;
                case ActionSpec.Disabled:
                    break; // swallowed by the router; nothing runs
                case ActionSpec.Brightness b:
                    if (pressed) _brightness.Adjust(b.StepPercent); // queued off this thread; presses coalesce
                    break;
                case ActionSpec.Text t:
                    if (pressed && t.Value.Length > 0 && !_text.Send(TextInput.Build(t.Value))) _log.Error("SendInput failed for text snippet");
                    break;
                case ActionSpec.ActionsRing:
                    if (pressed) ActionsRingRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex) { _log.Error("action failed", ex); }
    }
}
