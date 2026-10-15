using System.Globalization;
using Slender.Engine;
using Slender.Engine.Import;

namespace Slender.Settings.Logic;

public enum ImportStatus
{
    Ready,
    NotFound,
    ReadError,
}

/// <summary>One report group shown in the preview (Unmapped, Approximated or Skipped).</summary>
public sealed record ImportReportGroup(ImportNoteKind Kind, string Title, IReadOnlyList<string> Lines);

/// <summary>What an import would do, worked out without changing anything.</summary>
public sealed record ImportPreview(
    ImportStatus Status,
    string Message,
    ImportResult? Result,
    int ProfileCount,
    int BindingCount,
    IReadOnlyList<ImportReportGroup> Groups)
{
    public bool CanImport => Status == ImportStatus.Ready && Result is not null;

    /// <summary>"3 profiles and 18 bindings will be set."</summary>
    public string Summary
    {
        get
        {
            string profiles = ProfileCount == 1 ? "1 profile" : $"{ProfileCount} profiles";
            string bindings = BindingCount == 1 ? "1 binding" : $"{BindingCount} bindings";
            return $"{profiles} and {bindings} will be set.";
        }
    }
}

/// <summary>
/// Logic behind "Import from Options+": builds the preview from Options+'s databases (always through <see cref="OptionsPlusDb"/>'s
/// temp-copy read) and backs the current config file up before an import is saved. No UI types, so it is unit tested.
/// </summary>
public static class OptionsPlusImportFlow
{
    /// <summary>Reads both databases and runs the importer against <paramref name="current"/>. Never throws.</summary>
    public static ImportPreview Prepare(SlenderConfig current, string? settingsDb = null, string? macrosDb = null, Func<string, string?>? readJson = null)
    {
        readJson ??= path => OptionsPlusDb.ReadJson(path);
        settingsDb ??= OptionsPlusDb.SettingsPath;
        macrosDb ??= OptionsPlusDb.MacrosPath;

        if (!File.Exists(settingsDb))
        {
            return Failed(ImportStatus.NotFound, "Options+ settings were not found on this PC. Install and set up Logi Options+ first, or there is nothing to import.");
        }

        string? settings, macros;
        try
        {
            settings = readJson(settingsDb);
            macros = readJson(macrosDb);
        }
        catch (Exception e)
        {
            return Failed(ImportStatus.ReadError, "Options+ settings could not be read: " + e.Message);
        }

        if (string.IsNullOrWhiteSpace(settings))
        {
            return Failed(ImportStatus.ReadError, "Options+ settings could not be read (the file may be empty, locked or from an unsupported Options+ version). Nothing was changed.");
        }

        ImportResult result;
        try
        {
            result = OptionsPlusImporter.Import(settings, macros, current);
        }
        catch (Exception e)
        {
            return Failed(ImportStatus.ReadError, "Options+ settings were not in a format Slender understands: " + e.Message);
        }

        (int profiles, int bindings) = CountChanges(current, result.Config);
        return new ImportPreview(ImportStatus.Ready, "", result, profiles, bindings, Group(result.Notes));
    }

    private static ImportPreview Failed(ImportStatus status, string message) => new(status, message, null, 0, 0, []);

    /// <summary>Profiles whose bindings differ from <paramref name="before"/>, and how many bindings those profiles hold.</summary>
    public static (int Profiles, int Bindings) CountChanges(SlenderConfig before, SlenderConfig after)
    {
        int profiles = 0, bindings = 0;
        foreach (Profile p in after.Profiles)
        {
            Profile? old = before.Profiles.FirstOrDefault(o => string.Equals(o.AppProcess, p.AppProcess, StringComparison.OrdinalIgnoreCase));
            if (old is not null && Same(old.Bindings, p.Bindings) && Same(old.KeyBindings, p.KeyBindings))
            {
                continue;
            }

            int count = p.Bindings.Count + p.KeyBindings.Count;
            if (count == 0)
            {
                continue;
            }

            profiles++;
            bindings += count;
        }

        return (profiles, bindings);
    }

    private static bool Same(IReadOnlyDictionary<ushort, ActionSpec> a, IReadOnlyDictionary<ushort, ActionSpec> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out ActionSpec? other) && Equals(kv.Value, other));

    /// <summary>The report as the preview shows it: Not imported, Imported with changes, Skipped. "For the record" lines are left out.</summary>
    public static IReadOnlyList<ImportReportGroup> Group(IEnumerable<ImportNote> notes)
    {
        var groups = new List<ImportReportGroup>();
        foreach ((ImportNoteKind kind, string title) in new[]
        {
            (ImportNoteKind.Unmapped, "Not imported"),
            (ImportNoteKind.Approximated, "Imported with changes"),
            (ImportNoteKind.Skipped, "Skipped"),
        })
        {
            List<string> lines = notes.Where(n => n.Kind == kind).Select(n => $"{n.Where}: {n.Message}").ToList();
            if (lines.Count > 0)
            {
                groups.Add(new ImportReportGroup(kind, $"{title} ({lines.Count})", lines));
            }
        }

        return groups;
    }

    /// <summary>
    /// Copies <paramref name="configFile"/> to config.backup-yyyyMMdd-HHmmss.json next to it and returns the backup path,
    /// or null when there is no config file yet (nothing to lose). Never overwrites an earlier backup.
    /// </summary>
    public static string? BackupConfig(string configFile, DateTime now)
    {
        if (!File.Exists(configFile))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(configFile) ?? ".";
        string stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string target = Path.Combine(directory, $"config.backup-{stamp}.json");
        for (int n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(directory, $"config.backup-{stamp}-{n}.json");
        }

        File.Copy(configFile, target);
        return target;
    }
}
