namespace Slender.Engine.Macros;

/// <summary>What one macro step does.</summary>
public enum MacroStepKind
{
    KeyDown,    // Text = one key name ("Ctrl", "A", "F5"): held until a KeyUp or the macro ends
    KeyUp,
    Tap,        // Text = a chord ("Ctrl+C") or a comma sequence ("Enter, Ctrl+W"): pressed and released
    Type,       // Text = text typed as Unicode input
    MouseDown,  // Text = "Left", "Right", "Middle", "Back", "Forward"
    MouseUp,
    Click,
    Move,       // X/Y = pixels; Absolute false = from where the pointer is now, true = virtual-desktop position
    Wheel,      // Y = vertical notches (positive = away from the user), X = sideways notches (positive = right)
    Launch,     // Text = target, Arguments = optional
    Pause,      // Ms = how long to wait (PauseMinMs..PauseMaxMs)
}

/// <summary>Once = one pass. WhileHeld = repeat while the control is held (a pass is never cut short). Toggle = repeat until pressed again.</summary>
public enum MacroRunMode { Once, WhileHeld, Toggle }

/// <summary>One step of a macro. Only the fields its Kind names are used; <see cref="Normalized"/> drops the rest.</summary>
public sealed record MacroStep(MacroStepKind Kind, string? Text = null, int X = 0, int Y = 0, int Ms = 0, bool Absolute = false, string? Arguments = null)
{
    public const int PauseMinMs = 10, PauseMaxMs = 60_000, MoveLimit = 20_000, WheelLimit = 50, TextMax = 2000;

    public static MacroStep KeyDown(string key) => new(MacroStepKind.KeyDown, key);
    public static MacroStep KeyUp(string key) => new(MacroStepKind.KeyUp, key);
    public static MacroStep Tap(string chord) => new(MacroStepKind.Tap, chord);
    public static MacroStep Type(string text) => new(MacroStepKind.Type, text);
    public static MacroStep MouseDown(string button) => new(MacroStepKind.MouseDown, button);
    public static MacroStep MouseUp(string button) => new(MacroStepKind.MouseUp, button);
    public static MacroStep Click(string button) => new(MacroStepKind.Click, button);
    public static MacroStep MoveBy(int dx, int dy) => new(MacroStepKind.Move, X: dx, Y: dy);
    public static MacroStep MoveTo(int x, int y) => new(MacroStepKind.Move, X: x, Y: y, Absolute: true);
    public static MacroStep Wheel(int vertical, int horizontal = 0) => new(MacroStepKind.Wheel, X: horizontal, Y: vertical);
    public static MacroStep Launch(string target, string? arguments = null) => new(MacroStepKind.Launch, target, Arguments: arguments);
    public static MacroStep Pause(int ms) => new(MacroStepKind.Pause, Ms: ms);

    public static readonly string[] Buttons = ["Left", "Right", "Middle", "Back", "Forward"];

    public static string? CanonicalButton(string? name) =>
        Buttons.FirstOrDefault(b => string.Equals(b, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static int ClampPause(int ms) => Math.Clamp(ms, PauseMinMs, PauseMaxMs);

    /// <summary>The step with every value inside its limits and unused fields cleared; null when it cannot run (unknown key, empty text, ...).</summary>
    public MacroStep? Normalized()
    {
        switch (Kind)
        {
            case MacroStepKind.KeyDown or MacroStepKind.KeyUp:
                return ChordParser.TryParseKey(Text, out _) ? new MacroStep(Kind, Text!.Trim()) : null;
            case MacroStepKind.Tap:
                return ChordParser.TryParseSequence(Text, out _) ? new MacroStep(Kind, Text!.Trim()) : null;
            case MacroStepKind.Type:
                return string.IsNullOrEmpty(Text) ? null : new MacroStep(Kind, Text.Length > TextMax ? Text[..TextMax] : Text);
            case MacroStepKind.MouseDown or MacroStepKind.MouseUp or MacroStepKind.Click:
                return CanonicalButton(Text) is { } b ? new MacroStep(Kind, b) : null;
            case MacroStepKind.Move:
                return new MacroStep(Kind, X: Math.Clamp(X, -MoveLimit, MoveLimit), Y: Math.Clamp(Y, -MoveLimit, MoveLimit), Absolute: Absolute);
            case MacroStepKind.Wheel:
                var (h, v) = (Math.Clamp(X, -WheelLimit, WheelLimit), Math.Clamp(Y, -WheelLimit, WheelLimit));
                return h == 0 && v == 0 ? null : new MacroStep(Kind, X: h, Y: v);
            case MacroStepKind.Launch:
                return string.IsNullOrWhiteSpace(Text) ? null : new MacroStep(Kind, Text.Trim(), Arguments: string.IsNullOrWhiteSpace(Arguments) ? null : Arguments);
            case MacroStepKind.Pause:
                return new MacroStep(Kind, Ms: ClampPause(Ms));
            default:
                return null;
        }
    }
}

/// <summary>A named list of steps in the config's macro library. <see cref="ActionSpec.Macro"/> refers to it by Id.</summary>
public sealed record MacroDef(string Id, string Name, IReadOnlyList<MacroStep> Steps, MacroRunMode Mode = MacroRunMode.Once, int CapSeconds = MacroDef.CapDefaultSeconds)
{
    public const int MaxMacros = 100, MaxSteps = 500, NameMax = 60, CapMinSeconds = 1, CapMaxSeconds = 3600, CapDefaultSeconds = 60;

    /// <summary>Value equality including the steps (a record compares lists by reference otherwise).</summary>
    public bool Equals(MacroDef? other) =>
        other is not null && Id == other.Id && Name == other.Name && Mode == other.Mode && CapSeconds == other.CapSeconds && Steps.SequenceEqual(other.Steps);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Mode, CapSeconds, Steps.Count);

    /// <summary>A fresh short id for a new macro.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Total of the pauses, ms: what one pass waits at the least.</summary>
    public long PauseTotalMs => Steps.Where(s => s.Kind == MacroStepKind.Pause).Sum(s => (long)s.Ms);

    /// <summary>Same macro with every step normalised (bad steps dropped, at most <see cref="MaxSteps"/>), the name trimmed and the cap clamped; null without a usable id.</summary>
    public MacroDef? Normalized()
    {
        if (string.IsNullOrWhiteSpace(Id)) return null;
        var name = (Name ?? "").Trim();
        if (name.Length > NameMax) name = name[..NameMax].TrimEnd();
        var steps = (Steps ?? []).Select(s => s?.Normalized()).Where(s => s is not null).Cast<MacroStep>().Take(MaxSteps).ToList();
        return new MacroDef(Id.Trim(), name.Length == 0 ? "Macro" : name, steps, Enum.IsDefined(Mode) ? Mode : MacroRunMode.Once,
            Math.Clamp(CapSeconds, CapMinSeconds, CapMaxSeconds));
    }
}
