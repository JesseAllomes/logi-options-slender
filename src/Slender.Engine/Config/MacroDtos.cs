using Slender.Engine.Macros;

namespace Slender.Engine.Config;

/// <summary>Mutable shape of one macro in config.json (Mode and step Kind are written as names).</summary>
internal sealed class MacroDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Mode { get; set; }
    public int CapSeconds { get; set; } = MacroDef.CapDefaultSeconds;
    public List<MacroStepDto>? Steps { get; set; }
}

internal sealed class MacroStepDto
{
    public string? Kind { get; set; }
    public string? Text { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Ms { get; set; }
    public bool Absolute { get; set; }
    public string? Arguments { get; set; }
}

/// <summary>Converts the macro library to and from its DTOs. Reading is forgiving: a bad step or macro is dropped, never the whole file.</summary>
internal static class MacroDtos
{
    public static List<MacroDef> ToMacros(List<MacroDto>? raw)
    {
        var result = new List<MacroDef>();
        if (raw is null) return result;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in raw)
        {
            if (d is null || result.Count >= MacroDef.MaxMacros) continue;
            var steps = new List<MacroStep>();
            foreach (var s in d.Steps ?? [])
                if (s is not null && Enum.TryParse(s.Kind, ignoreCase: true, out MacroStepKind kind) && Enum.IsDefined(kind))
                    steps.Add(new MacroStep(kind, s.Text, s.X, s.Y, s.Ms, s.Absolute, s.Arguments));
            var mode = Enum.TryParse(d.Mode, ignoreCase: true, out MacroRunMode m) && Enum.IsDefined(m) ? m : MacroRunMode.Once;
            if (new MacroDef(d.Id ?? "", d.Name ?? "", steps, mode, d.CapSeconds).Normalized() is { } macro && ids.Add(macro.Id))
                result.Add(macro);
        }
        return result;
    }

    public static List<MacroDto> FromMacros(IReadOnlyList<MacroDef> macros) =>
        macros.Select(m => new MacroDto
        {
            Id = m.Id,
            Name = m.Name,
            Mode = m.Mode.ToString(),
            CapSeconds = m.CapSeconds,
            Steps = m.Steps.Select(s => new MacroStepDto
            {
                Kind = s.Kind.ToString(), Text = s.Text, X = s.X, Y = s.Y, Ms = s.Ms, Absolute = s.Absolute, Arguments = s.Arguments,
            }).ToList(),
        }).ToList();
}
