namespace Slender.Engine;

/// <summary>Low-level mouse hook surface. Handlers run on the hook thread and must return fast.</summary>
public interface IInputHook : IDisposable
{
    /// <summary>WM_MOUSEHWHEEL. Delta is in WHEEL_DELTA units (120 per notch), positive = right.</summary>
    event EventHandler<HookWheelEventArgs>? HorizontalWheel;

    void Start();
}

public sealed class HookWheelEventArgs(int delta) : EventArgs
{
    public int Delta { get; } = delta;

    /// <summary>Set true to swallow the event.</summary>
    public bool Handled { get; set; }
}

/// <summary>Foreground application tracking.</summary>
public interface IForegroundApp
{
    /// <summary>Process name without extension, lower case (e.g. "excel"), or null.</summary>
    string? ProcessName { get; }

    /// <summary>Foreground window handle.</summary>
    nint WindowHandle { get; }

    event EventHandler<string?>? Changed;
}

/// <summary>Excel automation via COM, bound to the foreground Excel window only.</summary>
public interface IExcelBridge
{
    /// <summary>Scrolls the active Excel window by whole columns (positive = right). False if not possible.</summary>
    bool TryScrollColumns(nint excelWindow, int columns);

    /// <summary>Runs an Excel command. False if not possible.</summary>
    bool TryRunCommand(nint excelWindow, ExcelCommand command);
}

public enum ExcelCommand
{
    NextSheet,
    PreviousSheet,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    ScrollToFirstColumn,
    ScrollToLastUsedColumn,
}

/// <summary>What a control does. Exactly one variant per binding.</summary>
public abstract record ActionSpec
{
    public sealed record None : ActionSpec;
    /// <summary>Key chord, e.g. "Ctrl+Shift+P", "Alt+Left", "Ctrl+PageDown".</summary>
    public sealed record Keys(string Chord) : ActionSpec;
    public sealed record Excel(ExcelCommand Command) : ActionSpec;
    /// <summary>Mouse button: "Left", "Right", "Middle", "Back", "Forward".</summary>
    public sealed record MouseButton(string Button) : ActionSpec;
    public sealed record ActionsRing : ActionSpec;
}

/// <summary>Executes actions in the foreground app.</summary>
public interface IActionExecutor
{
    void Execute(ActionSpec action, bool pressed);
}

/// <summary>Bindings for one app. AppProcess null = default profile.</summary>
public sealed record Profile(string? AppProcess, IReadOnlyDictionary<ushort, ActionSpec> Bindings);

public sealed record HapticSettings(bool Enabled = true, int Level = 2, bool OnButtonPress = false, bool OnActionsRing = true);

public sealed record ExcelSettings(bool ThumbWheelScrollsColumns = true, int ColumnsPerNotch = 1);

/// <summary>Persisted at %APPDATA%\Slender\config.json. Schema changes bump Version.</summary>
public sealed record SlenderConfig(
    int Version,
    IReadOnlyList<Profile> Profiles,
    ExcelSettings Excel,
    HapticSettings Haptics,
    bool LaunchAtLogin)
{
    public static SlenderConfig Default { get; } = new(
        1,
        [new Profile(null, new Dictionary<ushort, ActionSpec>()),
         new Profile("excel", new Dictionary<ushort, ActionSpec>
         {
             [Slender.Hidpp.ControlId.Back] = new ActionSpec.Excel(ExcelCommand.PreviousSheet),
             [Slender.Hidpp.ControlId.Forward] = new ActionSpec.Excel(ExcelCommand.NextSheet),
         })],
        new ExcelSettings(),
        new HapticSettings(),
        false);
}

/// <summary>Loads and saves SlenderConfig. Implemented in Slender.Tray.</summary>
public interface IConfigStore
{
    SlenderConfig Load();
    void Save(SlenderConfig config);
}
