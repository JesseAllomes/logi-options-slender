using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class OptionsPlusImportFlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-flow-" + Guid.NewGuid().ToString("N"));

    public OptionsPlusImportFlowTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string? FixtureReader(string path) =>
        Fixture(Path.GetFileName(path) == "settings.db" ? "optionsplus-settings.json" : "optionsplus-macros.json");

    private string FakeDb(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, "not a real db"); // only existence matters: the reader is injected
        return path;
    }

    [Fact]
    public void PreviewCountsProfilesAndBindingsAndGroupsTheReport()
    {
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, FakeDb("settings.db"), FakeDb("macros.db"), FixtureReader);

        Assert.Equal(ImportStatus.Ready, p.Status);
        Assert.True(p.CanImport);
        Assert.True(p.ProfileCount >= 3);
        Assert.True(p.BindingCount > p.ProfileCount);
        Assert.Contains("profiles", p.Summary);
        Assert.Equal(["Not imported", "Imported with changes", "Skipped"], p.Groups.Select(g => g.Title[..g.Title.IndexOf(" (", StringComparison.Ordinal)]));
        Assert.All(p.Groups, g => Assert.NotEmpty(g.Lines));
    }

    private static readonly string PluginStore = Path.Combine(AppContext.BaseDirectory, "Fixtures", "pluginservice");

    [Fact]
    public void PluginServiceRingWinsOverSettingsDbRingAndIsCounted()
    {
        var ring = Slender.Engine.Import.PluginServiceStore.Read(PluginStore);
        string icons = Path.Combine(_dir, "assets", "ring");
        ImportPreview without = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, FakeDb("settings.db"), FakeDb("macros.db"), FixtureReader);
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, FakeDb("settings.db"), FakeDb("macros.db"), FixtureReader, ring, new PluginServiceIcons(icons, _ => null));

        Assert.True(p.CanImport);
        Assert.Equal("Explore AI", p.Result!.Config.Profiles.Single(x => x.AppProcess is null).RingSlots![0].Name);
        Assert.True(p.BindingCount > without.BindingCount);
        Assert.DoesNotContain(p.Result.Notes, n => n.Where.Contains("radial-menu-virtual-device-", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginServiceRingAloneIsEnoughWhenSettingsDbIsMissing()
    {
        var ring = Slender.Engine.Import.PluginServiceStore.Read(PluginStore);
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, Path.Combine(_dir, "nope.db"), null, _ => throw new InvalidOperationException("must not read"), ring);
        Assert.True(p.CanImport);
        Assert.Equal(3, p.ProfileCount); // All apps, Excel, PowerPoint
    }

    [Fact]
    public void IconsAreCopiedOnlyWhenSavedAndNamedByContent()
    {
        string source = Path.Combine(_dir, "orig");
        string images = Path.Combine(source, "ActionImages");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "act.png"), [1, 2, 3]);
        string target = Path.Combine(_dir, "assets", "ring");
        var reads = new List<string>();
        var icons = new PluginServiceIcons(target, path => { reads.Add(path); return File.Exists(path) ? File.ReadAllBytes(path) : null; });
        var profile = new Slender.Engine.Import.PluginServiceProfile("@_x", "X", "{}", source);

        string? icon = icons.For(profile, "act");
        Assert.NotNull(icon);
        Assert.StartsWith("file:" + target, icon);
        Assert.False(Directory.Exists(target)); // nothing written until Save
        Assert.Null(icons.For(profile, "missing"));

        icons.Save();
        Assert.True(File.Exists(icon!["file:".Length..]));
        Assert.Equal(icon, new PluginServiceIcons(target, _ => [1, 2, 3]).For(profile, "other")); // same bytes, same file
    }

    [Fact]
    public void MissingSettingsDbIsReportedAsNotFound()
    {
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, Path.Combine(_dir, "nope.db"), null, _ => throw new InvalidOperationException("must not read"));
        Assert.Equal(ImportStatus.NotFound, p.Status);
        Assert.False(p.CanImport);
        Assert.Contains("not found", p.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void UnreadableOrBrokenSettingsGiveAReadError(string? json)
    {
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, FakeDb("settings.db"), FakeDb("macros.db"), _ => json);
        Assert.Equal(ImportStatus.ReadError, p.Status);
        Assert.Null(p.Result);
    }

    [Fact]
    public void ReaderExceptionIsCaught()
    {
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, FakeDb("settings.db"), null, _ => throw new IOException("locked"));
        Assert.Equal(ImportStatus.ReadError, p.Status);
        Assert.Contains("locked", p.Message);
    }

    [Fact]
    public void CountChangesIgnoresUnchangedProfiles()
    {
        var a = new Profile(null, new Dictionary<ushort, ActionSpec> { [1] = new ActionSpec.Keys("F1") });
        var b = new Profile("x", new Dictionary<ushort, ActionSpec> { [2] = new ActionSpec.None() });
        SlenderConfig before = SlenderConfig.Default with { Profiles = [a, b] };
        SlenderConfig after = before with { Profiles = [a, b with { Bindings = new Dictionary<ushort, ActionSpec> { [2] = new ActionSpec.Keys("F2"), [3] = new ActionSpec.None() } }] };
        Assert.Equal((1, 2), OptionsPlusImportFlow.CountChanges(before, after));
    }

    [Fact]
    public void Swipes_count_as_changes_in_the_preview()
    {
        SlenderConfig before = SlenderConfig.Default;
        SlenderConfig after = before with
        {
            Profiles = [.. before.Profiles.Select(p => p.AppProcess is null
                ? p with { GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = new ActionSpec.Keys("Win+Tab") } }
                : p)],
        };
        Assert.Equal((1, 1), OptionsPlusImportFlow.CountChanges(before, after));
    }

    [Fact]
    public void BackupCopiesConfigWithTimestampAndNeverOverwrites()
    {
        string config = Path.Combine(_dir, "config.json");
        File.WriteAllText(config, "{\"v\":1}");
        var now = new DateTime(2026, 10, 15, 9, 5, 7);

        string? first = OptionsPlusImportFlow.BackupConfig(config, now);
        Assert.Equal(Path.Combine(_dir, "config.backup-20261015-090507.json"), first);
        Assert.Equal("{\"v\":1}", File.ReadAllText(first!));

        File.WriteAllText(config, "{\"v\":2}");
        string? second = OptionsPlusImportFlow.BackupConfig(config, now);
        Assert.NotEqual(first, second);
        Assert.Equal("{\"v\":1}", File.ReadAllText(first!));
        Assert.Equal("{\"v\":2}", File.ReadAllText(second!));
    }

    [Fact]
    public void BackupWithoutConfigFileReturnsNull() =>
        Assert.Null(OptionsPlusImportFlow.BackupConfig(Path.Combine(_dir, "config.json"), DateTime.Now));

    [Fact]
    public void BackupKeepsOldConfigWhileStoreSavesImportedOne()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default);
        string? backup = OptionsPlusImportFlow.BackupConfig(store.FilePath, DateTime.Now);
        ImportPreview p = OptionsPlusImportFlow.Prepare(SlenderConfig.Default, FakeDb("settings.db"), FakeDb("macros.db"), FixtureReader);

        store.Save(p.Result!.Config);

        Assert.NotNull(backup);
        Assert.NotEqual(File.ReadAllText(backup!), File.ReadAllText(store.FilePath));
        Assert.Equal(p.Result.Config.Profiles.Count, store.Load().Profiles.Count);
    }

    [Fact]
    public void ExplicitNoneInAppIsPassThroughInTheSettingsModel()
    {
        var def = new Profile(null, new Dictionary<ushort, ActionSpec> { [Slender.Hidpp.ControlId.ModeShift] = new ActionSpec.Launch("https://chatgpt.com/") });
        var teams = new Profile("ms-teams", new Dictionary<ushort, ActionSpec> { [Slender.Hidpp.ControlId.ModeShift] = new ActionSpec.None() });
        SlenderConfig c = SlenderConfig.Default with { Profiles = [def, teams] };

        Assert.True(ProfileOps.IsPassThrough(c, "ms-teams", DeviceKind.Mouse, Slender.Hidpp.ControlId.ModeShift));
        Assert.Equal((null, false), ProfileOps.Effective(c, "ms-teams", DeviceKind.Mouse, Slender.Hidpp.ControlId.ModeShift));
        Assert.Equal(1, ProfileOps.ChangedCount(c, "ms-teams", DeviceKind.Mouse));
        Assert.False(ProfileOps.IsPassThrough(c, null, DeviceKind.Mouse, Slender.Hidpp.ControlId.ModeShift));

        // Clearing ("same as All apps") removes the entry, so it follows the default again.
        SlenderConfig cleared = ProfileOps.SetBinding(c, "ms-teams", DeviceKind.Mouse, Slender.Hidpp.ControlId.ModeShift, null);
        Assert.False(ProfileOps.IsPassThrough(cleared, "ms-teams", DeviceKind.Mouse, Slender.Hidpp.ControlId.ModeShift));
        Assert.True(ProfileOps.Effective(cleared, "ms-teams", DeviceKind.Mouse, Slender.Hidpp.ControlId.ModeShift).Inherited);
    }
}
