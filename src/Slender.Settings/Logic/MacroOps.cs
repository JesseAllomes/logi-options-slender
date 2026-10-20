using Slender.Engine;
using Slender.Engine.Macros;

namespace Slender.Settings.Logic;

/// <summary>Editing the macro library and the steps of one macro, and the words for them. Pure; every edit returns a new value.</summary>
public static class MacroOps
{
    public static MacroDef? Find(SlenderConfig config, string? id) => config.Macros.FirstOrDefault(m => m.Id == id);

    /// <summary>A new empty macro named "Macro 1", "Macro 2"... (the first name not in use), with a fresh id.</summary>
    public static MacroDef NewMacro(SlenderConfig config)
    {
        int n = 1;
        while (config.Macros.Any(m => string.Equals(m.Name, $"Macro {n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        return new MacroDef(MacroDef.NewId(), $"Macro {n}", []);
    }

    /// <summary>Replaces the macro with the same id, or adds it (at most <see cref="MacroDef.MaxMacros"/>). The macro is normalised first.</summary>
    public static SlenderConfig Upsert(SlenderConfig config, MacroDef macro)
    {
        if (macro.Normalized() is not { } clean)
        {
            return config;
        }

        var list = config.Macros.ToList();
        int i = list.FindIndex(m => m.Id == clean.Id);
        if (i >= 0)
        {
            list[i] = clean;
        }
        else if (list.Count < MacroDef.MaxMacros)
        {
            list.Add(clean);
        }
        else
        {
            return config;
        }

        return config with { Macros = list };
    }

    /// <summary>Removes a macro. Controls still bound to it do nothing (the labels show them as "Macro").</summary>
    public static SlenderConfig Remove(SlenderConfig config, string id) =>
        config.Macros.Any(m => m.Id == id) ? config with { Macros = config.Macros.Where(m => m.Id != id).ToList() } : config;

    // ---- steps ----

    /// <summary>Inserts at <paramref name="index"/> (clamped to the ends). Unchanged when the macro is full.</summary>
    public static MacroDef Insert(MacroDef macro, int index, MacroStep step)
    {
        if (macro.Steps.Count >= MacroDef.MaxSteps)
        {
            return macro;
        }

        var list = macro.Steps.ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), step);
        return macro with { Steps = list };
    }

    /// <summary>Inserts several steps in order, as many as fit.</summary>
    public static MacroDef InsertRange(MacroDef macro, int index, IReadOnlyList<MacroStep> steps)
    {
        var list = macro.Steps.ToList();
        list.InsertRange(Math.Clamp(index, 0, list.Count), steps.Take(Math.Max(0, MacroDef.MaxSteps - list.Count)));
        return macro with { Steps = list };
    }

    public static MacroDef Replace(MacroDef macro, int index, MacroStep step)
    {
        if (index < 0 || index >= macro.Steps.Count)
        {
            return macro;
        }

        var list = macro.Steps.ToList();
        list[index] = step;
        return macro with { Steps = list };
    }

    public static MacroDef Delete(MacroDef macro, int index)
    {
        if (index < 0 || index >= macro.Steps.Count)
        {
            return macro;
        }

        var list = macro.Steps.ToList();
        list.RemoveAt(index);
        return macro with { Steps = list };
    }

    /// <summary>Moves a step so it ends up at index <paramref name="to"/>. Out-of-range indexes change nothing.</summary>
    public static MacroDef Move(MacroDef macro, int from, int to)
    {
        if (from < 0 || from >= macro.Steps.Count || to < 0 || to >= macro.Steps.Count || from == to)
        {
            return macro;
        }

        var list = macro.Steps.ToList();
        MacroStep step = list[from];
        list.RemoveAt(from);
        list.Insert(to, step);
        return macro with { Steps = list };
    }

    public static MacroDef Duplicate(MacroDef macro, int index) =>
        index < 0 || index >= macro.Steps.Count ? macro : Insert(macro, index + 1, macro.Steps[index]);

    /// <summary>Sets a pause step's length, clamped to the allowed range. Other kinds are unchanged.</summary>
    public static MacroDef SetPause(MacroDef macro, int index, int ms) =>
        index >= 0 && index < macro.Steps.Count && macro.Steps[index].Kind == MacroStepKind.Pause
            ? Replace(macro, index, MacroStep.Pause(MacroStep.ClampPause(ms)))
            : macro;

    // ---- words ----

    public static IReadOnlyList<(MacroStepKind Kind, string Title)> AddChoices { get; } =
    [
        (MacroStepKind.Pause, "Pause"),
        (MacroStepKind.Tap, "Press a shortcut"),
        (MacroStepKind.Type, "Type text"),
        (MacroStepKind.Click, "Click"),
        (MacroStepKind.Move, "Move the pointer"),
        (MacroStepKind.Wheel, "Scroll"),
        (MacroStepKind.KeyDown, "Hold a key down"),
        (MacroStepKind.KeyUp, "Let a key up"),
        (MacroStepKind.MouseDown, "Hold a mouse button down"),
        (MacroStepKind.MouseUp, "Let a mouse button up"),
        (MacroStepKind.Launch, "Open an app or file"),
    ];

    /// <summary>A sensible new step of a kind: a 500 ms pause, Ctrl+C, a left click...</summary>
    public static MacroStep Default(MacroStepKind kind) => kind switch
    {
        MacroStepKind.Pause => MacroStep.Pause(500),
        MacroStepKind.Tap => MacroStep.Tap("Ctrl+C"),
        MacroStepKind.Type => MacroStep.Type("Text"),
        MacroStepKind.Click => MacroStep.Click("Left"),
        MacroStepKind.MouseDown => MacroStep.MouseDown("Left"),
        MacroStepKind.MouseUp => MacroStep.MouseUp("Left"),
        MacroStepKind.Move => MacroStep.MoveBy(100, 0),
        MacroStepKind.Wheel => MacroStep.Wheel(-1),
        MacroStepKind.KeyDown => MacroStep.KeyDown("Shift"),
        MacroStepKind.KeyUp => MacroStep.KeyUp("Shift"),
        _ => MacroStep.Launch("notepad.exe"),
    };

    /// <summary>One line for the step list: "Pause 500 ms", "Press Ctrl+C", "Type 'Hello'"...</summary>
    public static string Describe(MacroStep s) => s.Kind switch
    {
        MacroStepKind.Pause => "Pause " + PauseText(s.Ms),
        MacroStepKind.KeyDown => $"Hold {s.Text}",
        MacroStepKind.KeyUp => $"Let go of {s.Text}",
        MacroStepKind.Tap => $"Press {s.Text}",
        MacroStepKind.Type => ActionCatalog.TextName(s.Text ?? string.Empty),
        MacroStepKind.MouseDown => $"Hold {s.Text} button",
        MacroStepKind.MouseUp => $"Let go of {s.Text} button",
        MacroStepKind.Click => $"{s.Text} click",
        MacroStepKind.Move when s.Absolute => $"Move pointer to {s.X}, {s.Y}",
        MacroStepKind.Move => $"Move pointer by {s.X:+0;-0;0}, {s.Y:+0;-0;0}",
        MacroStepKind.Wheel => WheelText(s),
        MacroStepKind.Launch => ActionCatalog.LaunchName(s.Text ?? string.Empty),
        _ => s.Kind.ToString(),
    };

    private static string WheelText(MacroStep s)
    {
        var parts = new List<string>();
        if (s.Y != 0)
        {
            parts.Add($"scroll {(s.Y < 0 ? "down" : "up")} {Math.Abs(s.Y)}");
        }

        if (s.X != 0)
        {
            parts.Add($"scroll {(s.X < 0 ? "left" : "right")} {Math.Abs(s.X)}");
        }

        string text = string.Join(", ", parts);
        return text.Length == 0 ? "Scroll" : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>"250 ms", "1.5 s", "12 s", "1 min 5 s".</summary>
    public static string PauseText(long ms)
    {
        if (ms < 1000)
        {
            return $"{ms} ms";
        }

        if (ms < 10_000)
        {
            return $"{ms / 1000.0:0.#} s";
        }

        if (ms < 60_000)
        {
            return $"{(int)Math.Round(ms / 1000.0)} s";
        }

        long s = (long)Math.Round(ms / 1000.0);
        return s % 60 == 0 ? $"{s / 60} min" : $"{s / 60} min {s % 60} s";
    }

    public static string ModeName(MacroRunMode mode) => mode switch
    {
        MacroRunMode.WhileHeld => "Repeat while held",
        MacroRunMode.Toggle => "Repeat until pressed again",
        _ => "Run once",
    };

    /// <summary>"12 steps · 2.5 s of pauses · repeat while held".</summary>
    public static string Summary(MacroDef macro)
    {
        string steps = macro.Steps.Count == 1 ? "1 step" : $"{macro.Steps.Count} steps";
        string pauses = macro.PauseTotalMs > 0 ? $" · {PauseText(macro.PauseTotalMs)} of pauses" : string.Empty;
        string mode = macro.Mode == MacroRunMode.Once ? string.Empty : " · " + ModeName(macro.Mode).ToLowerInvariant();
        return steps + pauses + mode;
    }
}
