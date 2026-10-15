using System.Text;

namespace Slender.Engine.Import;

public enum ImportNoteKind
{
    /// <summary>Nothing was imported for this item; the user has to recreate it by hand if they want it.</summary>
    Unmapped,
    /// <summary>Imported, but not exactly: something was changed or lost on the way.</summary>
    Approximated,
    /// <summary>Deliberately ignored (device not owned, setting not needed).</summary>
    Skipped,
    /// <summary>For the record: an Options+ detail that needs no action.</summary>
    Info,
}

/// <summary>One line of the import report. Where = "Profile / control"; Message says what was found and why it was not (fully) mapped.</summary>
public sealed record ImportNote(ImportNoteKind Kind, string Where, string Message);

public sealed record ImportResult(SlenderConfig Config, IReadOnlyList<ImportNote> Notes)
{
    /// <summary>Plain-text report, grouped by kind, for showing or saving next to the config.</summary>
    public string FormatReport()
    {
        var sb = new StringBuilder();
        foreach (var kind in new[] { ImportNoteKind.Unmapped, ImportNoteKind.Approximated, ImportNoteKind.Skipped, ImportNoteKind.Info })
        {
            var group = Notes.Where(n => n.Kind == kind).ToList();
            if (group.Count == 0) continue;
            sb.Append(kind switch
            {
                ImportNoteKind.Unmapped => "Not imported",
                ImportNoteKind.Approximated => "Imported with changes",
                ImportNoteKind.Skipped => "Skipped",
                _ => "For the record",
            }).Append(" (").Append(group.Count).AppendLine("):");
            foreach (var n in group) sb.Append("  - ").Append(n.Where).Append(": ").AppendLine(n.Message);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
