namespace Slender.Engine;

/// <summary>Low-level mouse hook surface. Handlers run on the hook thread and must return fast.</summary>
public interface IInputHook : IDisposable
{
    /// <summary>WM_MOUSEHWHEEL. Delta is in WHEEL_DELTA units (120 per notch), positive = right.</summary>
    event EventHandler<HookWheelEventArgs>? HorizontalWheel;

    /// <summary>WM_MOUSEWHEEL from the user's own mouse (not injected, not taken by the ring). Positive = away from the user.</summary>
    event EventHandler<HookWheelEventArgs>? VerticalWheel { add { } remove { } }

    /// <summary>Middle / XButton1 (back) / XButton2 (forward) down and up. Set Handled to swallow both halves.</summary>
    event EventHandler<HookButtonEventArgs>? ButtonChanged;

    /// <summary>
    /// WM_MOUSEMOVE, raised only while move capture is on (<see cref="SetMoveCapture"/>). Dx/Dy are the movement of
    /// that event from the cursor position captured when capture began; set Handled to swallow it (the cursor stays put).
    /// </summary>
    event EventHandler<HookMoveEventArgs>? MouseMoved { add { } remove { } }

    /// <summary>Turns move reporting on (anchoring at the current cursor position) or off. Off by default.</summary>
    void SetMoveCapture(bool on) { }

    /// <summary>Left/right presses and the vertical wheel while ring capture is on (<see cref="SetRingCapture"/>); always swallowed.</summary>
    event EventHandler<HookRingEventArgs>? RingInput { add { } remove { } }

    /// <summary>While on, the Actions Ring owns the left/right buttons and the vertical wheel. Off by default.</summary>
    void SetRingCapture(bool on) { }

    /// <summary>
    /// How far (pixels, the larger axis) the pointer has been from where it is now at any point in the last
    /// <paramref name="windowMs"/> ms, from the user's own mouse moves. 0 when unknown.
    /// </summary>
    int PointerTravel(int windowMs) => 0;

    void Start();
}

public sealed class HookMoveEventArgs(int dx, int dy) : EventArgs
{
    public int Dx { get; } = dx;
    public int Dy { get; } = dy;

    /// <summary>Set true to swallow the move.</summary>
    public bool Handled { get; set; }
}

public enum HookRingKind { LeftDown, RightDown, Wheel, ThumbWheel }

/// <summary>An input the Actions Ring consumed. Delta (wheel) is in WHEEL_DELTA units, positive = up.</summary>
public sealed class HookRingEventArgs(HookRingKind kind, int delta) : EventArgs
{
    public HookRingKind Kind { get; } = kind;
    public int Delta { get; } = delta;
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

    /// <summary>Runs a named VBA macro in the foreground Excel (needs the Slender Excel add-in). False if not possible.</summary>
    bool TryRunMacro(nint excelWindow, string macroName) => false;
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
    /// <summary>Needs the Slender Excel add-in: next table style for the table at the active cell.</summary>
    CycleTableStyle,
    /// <summary>Needs the Slender Excel add-in: next chart type for the selected chart.</summary>
    CycleChartType,
}

/// <summary>What a control does. Exactly one variant per binding.</summary>
public abstract record ActionSpec
{
    public sealed record None : ActionSpec;
    /// <summary>Key chord, e.g. "Ctrl+Shift+P", "Alt+Left", "Ctrl+PageDown".</summary>
    public sealed record Keys(string Chord) : ActionSpec;
    public sealed record Excel(ExcelCommand Command) : ActionSpec;
    /// <summary>Runs a named VBA macro in Excel through the Slender Excel add-in (e.g. "Module1.Tidy"); does nothing without the add-in.</summary>
    public sealed record ExcelMacro(string Name) : ActionSpec;
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
    /// <summary>Changes the brightness of the monitor under the cursor by StepPercent points (negative = dimmer).</summary>
    public sealed record Brightness(int StepPercent) : ActionSpec;
    /// <summary>Types the text as Unicode input into the foreground app (a saved snippet).</summary>
    public sealed record Text(string Value) : ActionSpec;
    /// <summary>
    /// Runs a macro from the config's library (<see cref="SlenderConfig.Macros"/>) by id: press starts it (or ends it if it is
    /// running), release ends a repeat-while-held macro. A missing id does nothing.
    /// </summary>
    public sealed record Macro(string Id) : ActionSpec;
    /// <summary>
    /// One control, two actions: a release before <see cref="HoldMs"/> runs <paramref name="Tap"/> (press then release);
    /// still down at <see cref="HoldMs"/> starts <paramref name="Hold"/> as a press, released with the physical release.
    /// Neither may itself be a TapHold, and a TapHold is not allowed in a ring slot, a gesture swipe or the thumb wheel.
    /// </summary>
    public sealed record TapHold(ActionSpec Tap, ActionSpec Hold) : ActionSpec
    {
        /// <summary>How long a press must last before it counts as a hold.</summary>
        public const int HoldMs = 350;
    }
}

/// <summary>Executes actions in the foreground app.</summary>
public interface IActionExecutor
{
    void Execute(ActionSpec action, bool pressed);

    /// <summary>True when <see cref="SendKeyEvents"/> works; the thumb wheel's held-modifier path needs it.</summary>
    bool SupportsKeyEvents => false;

    /// <summary>Sends raw key downs and ups in one batch (the thumb wheel holding Ctrl across tab steps). False on failure.</summary>
    bool SendKeyEvents(IReadOnlyList<KeyEvent> events) => false;

    /// <summary>Sends one native sideways scroll (WHEEL_DELTA units, positive = right). False when unsupported or failed.</summary>
    bool SendHorizontalWheel(int delta) => false;

    /// <summary>Sends one native vertical scroll (WHEEL_DELTA units, positive = away from the user). False when unsupported or failed.</summary>
    bool SendVerticalWheel(int delta) => false;
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
    /// Actions Ring slots, clockwise from the top (RingSlotCount entries; a slot whose Action is None and has no Folder is empty).
    /// Null = follow the default profile.
    /// </summary>
    public IReadOnlyList<RingSlot>? RingSlots { get; init; }

    public const int RingSlotCount = 8;

    /// <summary>
    /// The thumb wheel's second action, run while the top button is held. Null = follow the default profile (nothing by
    /// default). Enabled = false turns it off for this app.
    /// </summary>
    public ThumbWheelSecond? ThumbWheelAlt { get; init; }

    /// <summary>Thumb wheel side scroll speed in this app, percent (25-400); null = All apps' (then PointerSettings.SideScrollSpeed).</summary>
    public int? SideScrollSpeed { get; init; }

    /// <summary>Main (vertical) wheel speed in this app, percent (25-400); null = All apps' (then 100 = the mouse's own scroll).</summary>
    public int? WheelSpeed { get; init; }

    /// <summary>Thumb wheel flick actions in this app; null = All apps' (then none).</summary>
    public ThumbWheelFlick? ThumbFlick { get; init; }

    /// <summary>Pointer speed (0x2201 DPI) while this app is in front; null = All apps' (then PointerSettings.Dpi, else the mouse's own).</summary>
    public int? Dpi { get; init; }
}

/// <summary>
/// One Actions Ring slot. Folder non-null makes it a folder (Action is ignored): up to Profile.RingSlotCount children,
/// one level deep (children never have folders). Name null = derive from the action. Icon null = automatic (app or site icon,
/// else the action's glyph); "glyph:E8C8" = a Segoe Fluent Icons code point; "file:C:\path\app.exe" = that file's icon.
/// </summary>
public sealed record RingSlot(ActionSpec Action)
{
    public string? Name { get; init; }
    public string? Icon { get; init; }
    public IReadOnlyList<RingSlot>? Folder { get; init; }

    public bool IsFolder => Folder is not null;
    public bool IsEmpty => Folder is null && Action is ActionSpec.None;

    public static RingSlot Empty { get; } = new(new ActionSpec.None());
}

/// <summary>
/// The thumb wheel's second role: <paramref name="Forward"/> runs once per notch forward (right),
/// <paramref name="Back"/> once per notch back (left). Any action; ActionSpec.None does nothing for that direction.
/// </summary>
public sealed record ThumbWheelSecond(bool Enabled, ActionSpec Forward, ActionSpec Back)
{
    /// <summary>Steps per amount of travel, percent (25-400); null = PointerSettings.SecondActionSpeed.</summary>
    public int? Speed { get; init; }
}

public enum RingStyle { Bezel, Compass, Keypad }

public enum RingSize { Small, Medium, Large }

public enum RingLabels { Always, SelectedOnly }

public enum RingPosition { AtCursor, MonitorCentre }

/// <summary>How the ring opens and closes. Off (or Windows animations off) is a 90 ms fade.</summary>
public enum RingOpening { Off, Pop, Bloom, FlyIn }

/// <summary>
/// Ring animation speed. Quickest = the base timings (Pop 240/180 ms, Bloom 440/330 ms, Fly in 400/300 ms open/close);
/// each step slower adds 0.4x: Quick 1.4x, Normal 1.8x, Slow 2.2x, Slowest 2.6x (user 2026-10-19).
/// </summary>
public enum RingAnimationSpeed { Slowest, Slow, Normal, Quick, Quickest }

/// <summary>How the Actions Ring looks. Global, not per profile. Accent is "#RRGGBB" (copper by default).</summary>
public sealed record RingAppearance(
    RingStyle Style = RingStyle.Bezel,
    RingSize Size = RingSize.Medium,
    string Accent = "#D9925F",
    int OpacityPercent = 92,
    RingLabels Labels = RingLabels.Always,
    RingPosition Position = RingPosition.AtCursor,
    RingOpening Opening = RingOpening.Bloom,
    RingAnimationSpeed AnimationSpeed = RingAnimationSpeed.Normal);

public enum GestureDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Strength (0-100) overrides Level when set; null keeps the 4-notch Level mapping (0-3 to device 25/50/75/100).
/// Persisted loosely: old configs have no Strength.
/// </summary>
public sealed record HapticSettings(bool Enabled = true, int Level = 2, bool OnButtonPress = false, bool OnActionsRing = true, int? Strength = null)
{
    /// <summary>Per-event buzz overrides (user 2026-10-20); an event missing here uses HapticCues.Default for it.</summary>
    public IReadOnlyDictionary<HapticEvent, HapticCue> Cues { get; init; } = new Dictionary<HapticEvent, HapticCue>();

    /// <summary>
    /// Named alerts other programs can trigger through Slender's local buzz pipe ("build", "claude", ...). Names are
    /// case-insensitive letters, digits, '-' or '_'; an unknown name plays the Alert event's cue.
    /// </summary>
    public IReadOnlyDictionary<string, HapticCue> Alerts { get; init; } = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Buzz on Windows notifications, per app (roadmap m45); off by default.</summary>
    public NotifyHapticSettings Notify { get; init; } = new();

    /// <summary>The user's named patterns (waveform, cycles, gap, repeats); a cue plays one by naming it in HapticCue.Custom. Names are case-insensitive.</summary>
    public IReadOnlyDictionary<string, HapticPattern> Patterns { get; init; } = new Dictionary<string, HapticPattern>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Value equality including the cue, alert and pattern maps (a record compares dictionaries by reference otherwise).</summary>
    public bool Equals(HapticSettings? other) =>
        other is not null && Enabled == other.Enabled && Level == other.Level && OnButtonPress == other.OnButtonPress
        && OnActionsRing == other.OnActionsRing && Strength == other.Strength
        && SameMap(Cues, other.Cues) && SameMap(Alerts, other.Alerts) && SameMap(Patterns, other.Patterns) && Notify.Equals(other.Notify);

    public override int GetHashCode() => HashCode.Combine(Enabled, Level, OnButtonPress, OnActionsRing, Strength, Cues.Count, Alerts.Count, Patterns.Count);

    private static bool SameMap<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> a, IReadOnlyDictionary<TKey, TValue> b) where TValue : IEquatable<TValue> =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v.Equals(kv.Value));

    /// <summary>Device haptic level 0-100: Strength when set, else the Level notch mapped to 25/50/75/100.</summary>
    public int EffectiveStrength => Strength is { } s ? Math.Clamp(s, 0, 100) : (Math.Clamp(Level, 0, 3) + 1) * 25;

    /// <summary>The notch (0-3) nearest a 0-100 strength, so the quick presets stay lit sensibly.</summary>
    public static int NotchFor(int strength) => Math.Clamp((strength + 12) / 25 - 1, 0, 3);
}

/// <summary>Everything Slender can buzz the mouse for (user 2026-10-20: every task, alert and function customisable).</summary>
public enum HapticEvent
{
    ButtonPress,      // any bound button or key action runs (was HapticSettings.OnButtonPress)
    RingOpen,         // the Actions Ring opens (was OnActionsRing)
    RingHover,        // the ring's highlight moves to another slot
    RingRun,          // a ring slot runs
    HoldStart,        // a press-and-hold reaches its hold
    TopHoldEngage,    // the top button is held and the thumb wheel's second action takes over
    TouchRest,        // thumb wheel rest fired
    TouchTap,         // thumb wheel tap fired
    TouchDoubleTap,   // thumb wheel double tap fired
    WheelModeChange,  // the scroll wheel switched between ratchet and free-spin
    BatteryLow,       // a device went under its battery alert threshold
    Alert,            // the buzz pipe was asked for a name it does not know, or for no name
}

/// <summary>
/// One buzz: a built-in waveform (0-14, Solaar's names in HapticCues.PatternNames), played Pulses times SpacingMs apart (a
/// longer buzz is several pulses close together). Strength 0-100 is how hard the mouse buzzes (Light 33, Normal 66, Strong
/// 100, see HapticCues); null plays at Normal. Custom names one of the user's patterns (HapticSettings.Patterns), whose
/// waveform, pulses, spacing and repeats replace this cue's own while HapticCues.Expand runs; Enabled and Strength stay the
/// cue's. A name that no longer exists leaves the cue's own fields in force. The whole group of
/// pulses plays Repeats times with RepeatGapMs of quiet between groups (user 2026-10-20: "6 taps, 1 sec gap, 6 taps"), never
/// longer than MaxTotalMs in all.
/// </summary>
public sealed record HapticCue(bool Enabled = true, int Pattern = 1, int Pulses = 1, int SpacingMs = 150, int? Strength = null,
    int Repeats = 1, int RepeatGapMs = 1000, string? Custom = null)
{
    public const int PatternMax = 14, PulsesMax = 6, SpacingMin = 40, SpacingMax = 1000;
    public const int RepeatsMax = 10, RepeatGapMin = 200, RepeatGapMax = 5000, MaxTotalMs = 15_000;

    /// <summary>First pulse to last, in ms.</summary>
    public int TotalMs => Repeats * (Pulses - 1) * SpacingMs + (Repeats - 1) * RepeatGapMs;

    /// <summary>The same cue with every field inside its limits; repeats are cut so the whole cue lasts at most MaxTotalMs.</summary>
    public HapticCue Clamped()
    {
        var c = this with
        {
            Pattern = Math.Clamp(Pattern, 0, PatternMax),
            Pulses = Math.Clamp(Pulses, 1, PulsesMax),
            SpacingMs = Math.Clamp(SpacingMs, SpacingMin, SpacingMax),
            Strength = Strength is { } s ? Math.Clamp(s, 0, 100) : null,
            Repeats = Math.Clamp(Repeats, 1, RepeatsMax),
            RepeatGapMs = Math.Clamp(RepeatGapMs, RepeatGapMin, RepeatGapMax),
        };
        while (c.Repeats > 1 && c.TotalMs > MaxTotalMs) c = c with { Repeats = c.Repeats - 1 };
        return c;
    }
}

/// <summary>A named pattern the user built: a waveform played Pulses times SpacingMs apart, the group Repeats times RepeatGapMs apart. Same limits as <see cref="HapticCue"/>.</summary>
public sealed record HapticPattern(int Pattern = 1, int Pulses = 1, int SpacingMs = 150, int Repeats = 1, int RepeatGapMs = 1000)
{
    /// <summary>The pattern as a cue (on, no strength, no reference), not yet clamped.</summary>
    public HapticCue ToCue() => new(true, Pattern, Pulses, SpacingMs, null, Repeats, RepeatGapMs);

    public static HapticPattern From(HapticCue c) => new(c.Pattern, c.Pulses, c.SpacingMs, c.Repeats, c.RepeatGapMs);

    /// <summary>The same pattern with every field inside the cue limits (including the 15 s cap on repeats).</summary>
    public HapticPattern Clamped() => From(ToCue().Clamped());

    /// <summary>For the saved list, e.g. "6 x Knock, 3 repeats".</summary>
    public string Summary
    {
        get
        {
            var c = Clamped();
            string text = $"{c.Pulses} x {HapticCues.PatternName(c.Pattern)}";
            return c.Repeats > 1 ? $"{text}, {c.Repeats} repeats" : text;
        }
    }
}

/// <summary>
/// Thumb wheel flick (user 2026-10-20): a fast spin of the thumb wheel runs its own action instead of scrolling, e.g. jump to
/// the first or last column. Null actions = a flick scrolls like any turn.
/// </summary>
public sealed record ThumbWheelFlick(bool Enabled, ActionSpec Right, ActionSpec Left);

public sealed record ExcelSettings(bool ThumbWheelScrollsColumns = true, int ColumnsPerNotch = 1);

/// <summary>Pointer and wheel settings applied over HID++ (0x2201 DPI, 0x2111 SmartShift, 0x2121/0x2150 direction).</summary>
public sealed record PointerSettings(
    int? Dpi = null,
    bool SmartShiftEnabled = true,
    int SmartShiftThreshold = 22, // matches the value Options+ left on this user's MX Master 4 (2026-10-15)
    bool InvertWheel = false,
    bool InvertThumbWheel = true, // true matches the Options+ setting found on this user's MX Master 4 (2026-10-15)
    bool SmoothScroll = false, // 0x2121 hi-res native reports; off until the user opts in
    int? WheelResistance = null, // 0x2111 tunable torque 0-100; null leaves the device alone
    int? SensePanelForce = null, // 0x19C0 force threshold in device units; null leaves the device alone
    int? SensePanelBaseline = null, // the first reading Slender saw from the mouse; the safe band for SensePanelForce is 50%-200% of it
    int SideScrollSpeed = 100,  // thumb wheel side scroll, percent of normal (25-400); 100 leaves the native scroll alone
    int SecondActionSpeed = 100, // second action steps per amount of thumb wheel travel (top button held), percent (25-400)
    int TouchRestMs = 250,       // thumb resting still on the wheel this long fires the rest action (100-1000)
    int DoubleTapMs = 300,       // longest gap between the two taps of a double tap (150-600)
    int GrabBeforePx = 150,      // pointer travel in the 400 ms before a touch above which it is a grab (30-600; touch calibration)
    int GrabMovePx = 24)         // pointer travel during a touch above which it is the hand moving the mouse (8-120; touch calibration)
{
    public const int SpeedMin = 25, SpeedMax = 400;
    public const int RestMinMs = 100, RestMaxMs = 1000, DoubleTapMinMs = 150, DoubleTapMaxMs = 600;
    public const int GrabBeforeMin = 30, GrabBeforeMax = 600, GrabMoveMin = 8, GrabMoveMax = 120;

    /// <summary>Largest grab value a config file may hold; anything from 1 to this loads unchanged (calibration's own limits are above).</summary>
    public const int GrabSanityMax = 10_000;
}

/// <summary>
/// ERGO K860 settings. DisabledKeys is a <see cref="DisabledKeys"/> mask (0x4521, applied on the keyboard itself). ShowLockKeyChanges
/// shows a short cursor label when Caps Lock or Num Lock flips. FnLockChannel1..3 are Fn inversion per Easy-Switch channel
/// (0x40A3); null leaves that channel alone. IdentifySpareKeys is set only while Settings' "Identify" panel is open: the tray
/// then diverts the three spare keys and reports which one was pressed.
/// </summary>
public sealed record KeyboardSettings(
    bool FnLock = false,
    byte DisabledKeys = 0,
    bool ShowLockKeyChanges = true,
    bool? FnLockChannel1 = null,
    bool? FnLockChannel2 = null,
    bool? FnLockChannel3 = null,
    bool IdentifySpareKeys = false)
{
    /// <summary>The three per-channel values in channel order (0-2).</summary>
    public IReadOnlyList<bool?> FnLockChannels => [FnLockChannel1, FnLockChannel2, FnLockChannel3];

    /// <summary>A copy with channel <paramref name="index"/> (0-2) set.</summary>
    public KeyboardSettings WithChannel(int index, bool? value) => index switch
    {
        0 => this with { FnLockChannel1 = value },
        1 => this with { FnLockChannel2 = value },
        2 => this with { FnLockChannel3 = value },
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

/// <summary>Low-battery notification: once per discharge when a device drops to the threshold while not charging; re-armed by charging or rising above it.</summary>
public sealed record BatteryAlertSettings(bool Enabled = true, int MouseThresholdPercent = 15, int KeyboardThresholdPercent = 10);

/// <summary>Persisted at %APPDATA%\Slender\config.json. Schema changes bump Version (2 adds Pointer, Keyboard, KeyBindings, DisplayName, ExePath).</summary>
/// <summary>
/// One of the user's own shortcuts: a name, the chord (ChordParser form, e.g. "Ctrl+Shift+V") and the app it belongs to
/// (process name without ".exe"); null App = shown for every app.
/// </summary>
public sealed record CustomShortcut(string Name, string Chord, string? App = null);

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
    public RingAppearance Ring { get; init; } = new();

    /// <summary>The macro library: <see cref="ActionSpec.Macro"/> refers to an entry by id.</summary>
    public IReadOnlyList<Slender.Engine.Macros.MacroDef> Macros { get; init; } = [];

    /// <summary>The user's own named shortcuts for the picker's Shortcuts tab ("My shortcuts"), in the order added.</summary>
    public IReadOnlyList<CustomShortcut> MyShortcuts { get; init; } = [];

    /// <summary>K860 remaps the user chose to store ON the keyboard (CID to chord). Written to the keyboard only when Settings asks the tray to apply a saved change, never on connect or reload.</summary>
    public IReadOnlyList<OnboardRemap> OnboardRemaps { get; init; } = [];

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
public sealed record DeviceState(string Id, string Name, bool Connected, int? BatteryPercent, bool Charging, string? BatteryText = null)
{
    /// <summary>Current pointer speed read from the mouse (0x2201); null when unknown or not a mouse.</summary>
    public int? Dpi { get; init; }
    /// <summary>
    /// The DPI values the sensor accepts (0x2201 fn1, read once per connection by the engine): Min/Max/Step, plus the explicit
    /// values only when they are not evenly stepped. All null until the tray has read them (mouse not connected yet).
    /// </summary>
    public int? DpiMin { get; init; }
    public int? DpiMax { get; init; }
    public int? DpiStep { get; init; }
    public IReadOnlyList<int>? DpiValues { get; init; }
    /// <summary>Days-left sentence from the battery history ("About 12 days left", "Not enough data yet", "Charging"); null when not known.</summary>
    public string? BatteryEstimate { get; init; }
    /// <summary>Current scroll wheel resistance 0-100 (0x2111 torque); null when unknown.</summary>
    public int? WheelResistance { get; init; }
    /// <summary>Current Sense Panel force reading in device units (0x19C0); null when unknown.</summary>
    public int? SensePanelForce { get; init; }
    /// <summary>Safe band for SensePanelForce (50%-200% of the first reading); null when unknown.</summary>
    public int? SensePanelForceMin { get; init; }
    public int? SensePanelForceMax { get; init; }
    /// <summary>Current haptic strength 0-100 (0x19B0); null when unknown.</summary>
    public int? HapticStrength { get; init; }
    /// <summary>Running firmware, e.g. "RBM 27.03.0019" (0x0003 reads); null when not read yet.</summary>
    public string? Firmware { get; init; }

    /// <summary>Easy-Switch channel (0-2) the keyboard is on, when it could be told; null when unknown or not a keyboard.</summary>
    public int? Channel { get; init; }

    /// <summary>Scroll wheel mode of the mouse: "ratchet" or "free-spin" (0x2111); null when unknown or not a mouse.</summary>
    public string? WheelMode { get; init; }

    /// <summary>Easy-Switch channels read from 0x1814/0x1815 (display only); null when not read or unsupported.</summary>
    public IReadOnlyList<HostState>? Hosts { get; init; }

    /// <summary>"active", "sleeping" or "disconnected" from the device registry (m52); null from an older tray or for a never-installed device.</summary>
    public string? Presence { get; init; }
}

/// <summary>One Easy-Switch channel as the device reports it. Name is the host's friendly name, when the device stores one.</summary>
public sealed record HostState(int Index, string? Name, bool Paired, bool Current);

/// <summary>
/// Written by the tray to %LOCALAPPDATA%\Slender\status.json whenever it changes (atomic replace); read by the settings app.
/// Device ids: "mx-master-4", "ergo-k860". The update fields are null/false when the tray has no update information
/// (older tray, or not an installed copy): CurrentVersion is the running version, AvailableVersion a newer one that was
/// found (downloading until UpdateReady), UpdateReady true once it is downloaded and "Restart and update" will apply it.
/// </summary>
public sealed record SlenderStatus(
    IReadOnlyList<DeviceState> Devices,
    bool OptionsPlusRunning,
    string? ForegroundApp,
    DateTimeOffset UpdatedAt,
    string? CurrentVersion = null,
    string? AvailableVersion = null,
    bool UpdateReady = false);

/// <summary>
/// How the settings app asks the tray to do update work: it posts these registered window messages (RegisterWindowMessage)
/// to the tray's hidden window, found by class name. The tray does all the Velopack work.
/// </summary>
public static class UpdateMessages
{
    public const string TrayWindowClass = "Slender.Tray.Window";
    public const string CheckName = "Slender.UpdateCheck";
    public const string ApplyName = "Slender.UpdateApply";
}

/// <summary>Well-known locations. Config roams (%APPDATA%); status, assets and logs stay local (%LOCALAPPDATA%).</summary>
public static class SlenderPaths
{
    public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slender");
    public static string LocalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slender");
    public static string StatusFile => Path.Combine(LocalDirectory, "status.json");
    public static string AssetsDirectory => Path.Combine(LocalDirectory, "assets");
    /// <summary>Second copy of the device pictures, beside the config: Options+ may be gone, so these copies are all there is.</summary>
    public static string PictureBackupDirectory => Path.Combine(ConfigDirectory, "device-pictures");
    public static string LogsDirectory => Path.Combine(LocalDirectory, "logs");
}

/// <summary>The control bound to the Actions Ring went down or up.</summary>
public sealed class RingTriggerEventArgs(bool pressed, bool holdOnly = false) : EventArgs
{
    public bool Pressed { get; } = pressed;

    /// <summary>The press came from a TapHold hold: the ring opens in hold mode only (a quick release cancels instead of leaving it open).</summary>
    public bool HoldOnly { get; } = holdOnly;
}
