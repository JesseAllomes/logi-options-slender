namespace Slender.Engine;

/// <summary>Low-level mouse hook surface. Handlers run on the hook thread and must return fast.</summary>
public interface IInputHook : IDisposable
{
    /// <summary>WM_MOUSEHWHEEL. Delta is in WHEEL_DELTA units (120 per notch), positive = right.</summary>
    event EventHandler<HookWheelEventArgs>? HorizontalWheel;

    /// <summary>Middle / XButton1 (back) / XButton2 (forward) down and up. Set Handled to swallow both halves.</summary>
    event EventHandler<HookButtonEventArgs>? ButtonChanged;

    /// <summary>
    /// WM_MOUSEMOVE, raised only while move capture is on (<see cref="SetMoveCapture"/>). Dx/Dy are the movement of
    /// that event from the cursor position captured when capture began; set Handled to swallow it (the cursor stays put).
    /// </summary>
    event EventHandler<HookMoveEventArgs>? MouseMoved { add { } remove { } }

    /// <summary>Turns move reporting on (anchoring at the current cursor position) or off. Off by default.</summary>
    void SetMoveCapture(bool on) { }

    void Start();
}

public sealed class HookMoveEventArgs(int dx, int dy) : EventArgs
{
    public int Dx { get; } = dx;
    public int Dy { get; } = dy;

    /// <summary>Set true to swallow the move.</summary>
    public bool Handled { get; set; }
}

public sealed class HookWheelEventArgs(int delta) : EventArgs
{
    public int Delta { get; } = delta;

    /// <summary>Set true to swallow the event.</summary>
    public bool Handled { get; set; }
}

/// <summary>Standard mouse buttons the hook can see without HID++. Values match the MX Master 4 control IDs.</summary>
public enum HookButton : ushort
{
    Middle = Slender.Hidpp.ControlId.Middle,
    Back = Slender.Hidpp.ControlId.Back,
    Forward = Slender.Hidpp.ControlId.Forward,
}

public sealed class HookButtonEventArgs(HookButton button, bool pressed) : EventArgs
{
    public HookButton Button { get; } = button;
    public bool Pressed { get; } = pressed;

    /// <summary>Set true to swallow the event. A swallowed press must also swallow its release.</summary>
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
    /// <summary>
    /// Opens something via the shell: an .exe path, a file or folder, a URL, or a Store app as "shell:AppsFolder\{AUMID}".
    /// Arguments only apply to executables.
    /// </summary>
    public sealed record Launch(string Target, string? Arguments = null) : ActionSpec;
    /// <summary>Turns the control off: the press is swallowed and nothing runs (unlike None, which passes the native input through).</summary>
    public sealed record Disabled : ActionSpec;
}

/// <summary>Executes actions in the foreground app.</summary>
public interface IActionExecutor
{
    void Execute(ActionSpec action, bool pressed);
}

/// <summary>Bindings for one app. AppProcess null = default profile.</summary>
/// <summary>
/// Bindings for one app. AppProcess null = default profile ("All apps"). Bindings = mouse controls (MX Master 4 CIDs);
/// KeyBindings = keyboard keys (ERGO K860 CIDs). Missing entries follow the default profile.
/// </summary>
public sealed record Profile(string? AppProcess, IReadOnlyDictionary<ushort, ActionSpec> Bindings)
{
    public IReadOnlyDictionary<ushort, ActionSpec> KeyBindings { get; init; } = new Dictionary<ushort, ActionSpec>();

    /// <summary>Display name for the app strip (e.g. "Excel"); null = derive from the process name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Full path of the app's executable, used to extract its icon; null = resolve from the running process.</summary>
    public string? ExePath { get; init; }

    /// <summary>
    /// Gesture button held while the mouse moves. Missing directions follow the default profile; a release without
    /// movement runs Bindings[ControlId.Gesture] as before.
    /// </summary>
    public IReadOnlyDictionary<GestureDirection, ActionSpec> GestureSwipes { get; init; } = new Dictionary<GestureDirection, ActionSpec>();

    /// <summary>
    /// Actions Ring slots, clockwise from the top (RingSlotCount entries; ActionSpec.None = empty slot).
    /// Null = follow the default profile.
    /// </summary>
    public IReadOnlyList<ActionSpec>? RingSlots { get; init; }

    public const int RingSlotCount = 8;
}

public enum GestureDirection
{
    Up,
    Down,
    Left,
    Right,
}

public sealed record HapticSettings(bool Enabled = true, int Level = 2, bool OnButtonPress = false, bool OnActionsRing = true);

public sealed record ExcelSettings(bool ThumbWheelScrollsColumns = true, int ColumnsPerNotch = 1);

/// <summary>Pointer and wheel settings applied over HID++ (0x2201 DPI, 0x2111 SmartShift, 0x2121/0x2150 direction).</summary>
public sealed record PointerSettings(
    int? Dpi = null,
    bool SmartShiftEnabled = true,
    int SmartShiftThreshold = 22, // matches the value Options+ left on this user's MX Master 4 (2026-10-15)
    bool InvertWheel = false,
    bool InvertThumbWheel = true, // true matches the Options+ setting found on this user's MX Master 4 (2026-10-15)
    bool SmoothScroll = false); // 0x2121 hi-res native reports; off until the user opts in

public sealed record KeyboardSettings(bool FnLock = false);

/// <summary>Low-battery notification: once per discharge when a device drops to the threshold while not charging; re-armed by charging or rising above it.</summary>
public sealed record BatteryAlertSettings(bool Enabled = true, int MouseThresholdPercent = 15, int KeyboardThresholdPercent = 10);

/// <summary>Persisted at %APPDATA%\Slender\config.json. Schema changes bump Version (2 adds Pointer, Keyboard, KeyBindings, DisplayName, ExePath).</summary>
public sealed record SlenderConfig(
    int Version,
    IReadOnlyList<Profile> Profiles,
    ExcelSettings Excel,
    HapticSettings Haptics,
    bool LaunchAtLogin)
{
    public const int CurrentVersion = 2;

    public PointerSettings Pointer { get; init; } = new();
    public KeyboardSettings Keyboard { get; init; } = new();
    public BatteryAlertSettings BatteryAlerts { get; init; } = new();

    public static SlenderConfig Default { get; } = new(
        CurrentVersion,
        [new Profile(null, new Dictionary<ushort, ActionSpec>()) { DisplayName = "All apps" },
         new Profile("excel", new Dictionary<ushort, ActionSpec>
         {
             [Slender.Hidpp.ControlId.Back] = new ActionSpec.Excel(ExcelCommand.PreviousSheet),
             [Slender.Hidpp.ControlId.Forward] = new ActionSpec.Excel(ExcelCommand.NextSheet),
         }) { DisplayName = "Excel" }],
        new ExcelSettings(),
        new HapticSettings(),
        false);
}

/// <summary>Loads and saves SlenderConfig (Slender.Engine.Config.JsonConfigStore). Version 1 files load and are saved as the current version.</summary>
public interface IConfigStore
{
    SlenderConfig Load();
    void Save(SlenderConfig config);
}

/// <summary>One device as the tray last saw it.</summary>
public sealed record DeviceState(string Id, string Name, bool Connected, int? BatteryPercent, bool Charging, string? BatteryText = null);

/// <summary>
/// Written by the tray to %LOCALAPPDATA%\Slender\status.json whenever it changes (atomic replace); read by the settings app.
/// Device ids: "mx-master-4", "ergo-k860".
/// </summary>
public sealed record SlenderStatus(IReadOnlyList<DeviceState> Devices, bool OptionsPlusRunning, string? ForegroundApp, DateTimeOffset UpdatedAt);

/// <summary>Well-known locations. Config roams (%APPDATA%); status, assets and logs stay local (%LOCALAPPDATA%).</summary>
public static class SlenderPaths
{
    public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slender");
    public static string LocalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slender");
    public static string StatusFile => Path.Combine(LocalDirectory, "status.json");
    public static string AssetsDirectory => Path.Combine(LocalDirectory, "assets");
    public static string LogsDirectory => Path.Combine(LocalDirectory, "logs");
}
