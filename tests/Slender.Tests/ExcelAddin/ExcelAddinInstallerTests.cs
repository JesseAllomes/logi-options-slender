using Slender.Engine.ExcelAddin;
using Slender.Settings.Logic;

namespace Slender.Tests.ExcelAddin;

internal sealed class FakeRegistry : IExcelAddinRegistry
{
    public Dictionary<string, string> Values { get; } = [];
    public List<string> Writes { get; } = [];
    public bool ThrowOnWrite;

    public IReadOnlyList<KeyValuePair<string, string>> ReadOpenValues() => [.. Values];

    public void WriteOpenValue(string name, string data)
    {
        if (ThrowOnWrite) throw new UnauthorizedAccessException("denied");
        Writes.Add(name);
        Values[name] = data;
    }

    public void DeleteOpenValue(string name) => Values.Remove(name);
}

internal sealed class FakeFiles : IExcelAddinFiles
{
    public HashSet<string> Present { get; } = [];
    public List<(string From, string To)> Copies { get; } = [];
    public bool LockedTarget;
    public bool LockedDelete;

    public bool Exists(string path) => Present.Contains(path);

    public void Copy(string source, string destination)
    {
        if (LockedTarget) throw new IOException("in use");
        Copies.Add((source, destination));
        Present.Add(destination);
    }

    public void Delete(string path)
    {
        if (LockedDelete) throw new IOException("in use");
        Present.Remove(path);
    }
}

internal sealed class FakeExcelProbe : IExcelProcessProbe
{
    public bool Running;
    public bool IsExcelRunning() => Running;
}

public class ExcelAddinInstallerTests
{
    private const string Source = @"C:\App\ExcelAddin\" + ExcelAddinInstaller.XllFileName;
    private const string Target = @"C:\Data\ExcelAddin\" + ExcelAddinInstaller.XllFileName;
    private static readonly string OurValue = "/R \"" + Target + "\"";

    private sealed class Rig
    {
        public FakeRegistry Registry { get; } = new();
        public FakeFiles Files { get; } = new();
        public FakeExcelProbe Excel { get; } = new();
        public ExcelAddinInstaller Installer { get; }

        public Rig(bool sourcePresent = true)
        {
            if (sourcePresent) Files.Present.Add(Source);
            Installer = new ExcelAddinInstaller(Registry, Files, Excel, Source, Target);
        }
    }

    [Fact]
    public void ConstructingAndAskingChangesNothing()
    {
        var r = new Rig();
        Assert.False(r.Installer.IsInstalled());
        Assert.True(r.Installer.IsAvailable());
        Assert.Empty(r.Registry.Writes);
        Assert.Empty(r.Files.Copies);
    }

    [Fact]
    public void InstallCopiesTheFileAndWritesTheFirstOpenValue()
    {
        var r = new Rig();
        var result = r.Installer.Install();
        Assert.Equal(AddinInstallStatus.Installed, result.Status);
        Assert.Equal([(Source, Target)], r.Files.Copies);
        Assert.Equal(OurValue, r.Registry.Values["OPEN"]);
        Assert.True(r.Installer.IsInstalled());
    }

    [Fact]
    public void InstallTakesTheFirstFreeSlotAfterOtherAddins()
    {
        var r = new Rig();
        r.Registry.Values["OPEN"] = "/R \"C:\\Other\\a.xll\"";
        r.Registry.Values["OPEN1"] = "/R \"C:\\Other\\b.xll\"";
        r.Installer.Install();
        Assert.Equal(OurValue, r.Registry.Values["OPEN2"]);
        Assert.Equal("/R \"C:\\Other\\a.xll\"", r.Registry.Values["OPEN"]);
        Assert.Equal(["OPEN2"], r.Registry.Writes); // nobody else's value is touched
    }

    [Fact]
    public void InstallFillsAGapSoExcelStillReadsIt()
    {
        var r = new Rig();
        r.Registry.Values["OPEN"] = "x";
        r.Registry.Values["OPEN2"] = "y"; // OPEN1 missing: Excel stops at the gap
        r.Installer.Install();
        Assert.Equal(OurValue, r.Registry.Values["OPEN1"]);
    }

    [Fact]
    public void InstallingTwiceIsHarmless()
    {
        var r = new Rig();
        r.Installer.Install();
        r.Registry.Writes.Clear();
        var again = r.Installer.Install();
        Assert.Equal(AddinInstallStatus.AlreadyInstalled, again.Status);
        Assert.Empty(r.Registry.Writes);
        Assert.Single(r.Registry.Values);
    }

    [Fact]
    public void InstallRepairsAnEntryThatPointsElsewhere()
    {
        var r = new Rig();
        r.Registry.Values["OPEN"] = "/R \"D:\\Old\\" + ExcelAddinInstaller.XllFileName + "\"";
        var result = r.Installer.Install();
        Assert.Equal(AddinInstallStatus.Installed, result.Status);
        Assert.Equal(OurValue, r.Registry.Values["OPEN"]);
        Assert.Single(r.Registry.Values);
    }

    [Fact]
    public void WithoutTheShippedFileNothingIsWritten()
    {
        var r = new Rig(sourcePresent: false);
        Assert.False(r.Installer.IsAvailable());
        var result = r.Installer.Install();
        Assert.Equal(AddinInstallStatus.SourceMissing, result.Status);
        Assert.Empty(r.Registry.Values);
        Assert.Empty(r.Files.Copies);
    }

    [Fact]
    public void InstallWhileExcelRunsSaysSoAndStillSucceedsWhenTheOldCopyIsLocked()
    {
        var r = new Rig();
        r.Installer.Install();
        r.Registry.Values.Clear();
        r.Excel.Running = true;
        r.Files.LockedTarget = true; // Excel has the old copy open
        var result = r.Installer.Install();
        Assert.Equal(AddinInstallStatus.Installed, result.Status);
        Assert.True(result.ExcelWasRunning);
        Assert.Contains("close Excel", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LockedFileWithNoOldCopyFailsAndWritesNothing()
    {
        var r = new Rig();
        r.Files.LockedTarget = true;
        var result = r.Installer.Install();
        Assert.Equal(AddinInstallStatus.Failed, result.Status);
        Assert.Empty(r.Registry.Values);
    }

    [Fact]
    public void RegistryRefusalIsReportedNotThrown()
    {
        var r = new Rig();
        r.Registry.ThrowOnWrite = true;
        Assert.Equal(AddinInstallStatus.Failed, r.Installer.Install().Status);
    }

    [Fact]
    public void UninstallRemovesOursAndClosesTheGap()
    {
        var r = new Rig();
        r.Registry.Values["OPEN"] = "a";
        r.Installer.Install(); // OPEN1
        r.Registry.Values["OPEN2"] = "c";
        var result = r.Installer.Uninstall();
        Assert.Equal(AddinInstallStatus.Removed, result.Status);
        Assert.Equal("a", r.Registry.Values["OPEN"]);
        Assert.Equal("c", r.Registry.Values["OPEN1"]); // moved down so Excel still reads it
        Assert.False(r.Registry.Values.ContainsKey("OPEN2"));
        Assert.False(r.Installer.IsInstalled());
        Assert.DoesNotContain(Target, r.Files.Present);
    }

    [Fact]
    public void UninstallOfTheOnlyEntryLeavesNoOpenValue()
    {
        var r = new Rig();
        r.Installer.Install();
        r.Installer.Uninstall();
        Assert.Empty(r.Registry.Values);
    }

    [Fact]
    public void UninstallWhenOffDoesNothing()
    {
        var r = new Rig();
        r.Registry.Values["OPEN"] = "other";
        var result = r.Installer.Uninstall();
        Assert.Equal(AddinInstallStatus.NotInstalled, result.Status);
        Assert.Equal("other", r.Registry.Values["OPEN"]);
        Assert.Empty(r.Registry.Writes);
    }

    [Fact]
    public void UninstallWhileExcelRunsKeepsGoingWhenTheFileIsLocked()
    {
        var r = new Rig();
        r.Installer.Install();
        r.Excel.Running = true;
        r.Files.LockedDelete = true;
        var result = r.Installer.Uninstall();
        Assert.Equal(AddinInstallStatus.Removed, result.Status);
        Assert.True(result.ExcelWasRunning);
        Assert.Empty(r.Registry.Values);
    }

    [Theory]
    [InlineData("OPEN", "OPEN1")]   // a real first slot: ours follows it
    [InlineData("open", "OPEN1")]   // registry names are case-insensitive
    [InlineData("OPENX", "OPEN")]   // not a slot name: OPEN is still free
    [InlineData("OPEN0", "OPEN")]
    [InlineData("OPEN1", "OPEN")]   // OPEN1 without OPEN: Excel would not read it, so slot 0 is free
    public void OnlyRealOpenSlotsCount(string existing, string ours)
    {
        var r = new Rig();
        r.Registry.Values[existing] = "z";
        r.Installer.Install();
        Assert.Equal(OurValue, r.Registry.Values[ours]);
    }

    [Fact]
    public void RegistryValueFormatIsExcelsOpenSwitch() => Assert.Equal("/R \"C:\\x\\a.xll\"", ExcelAddinInstaller.ValueFor(@"C:\x\a.xll"));

    [Fact]
    public void TextTellsTheUserToRestartExcel()
    {
        Assert.Contains("close it", ExcelAddinText.Describe(new(AddinInstallStatus.Installed, true, "")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next time Excel starts", ExcelAddinText.Describe(new(AddinInstallStatus.Installed, false, "")));
        Assert.Equal("boom", ExcelAddinText.Describe(new(AddinInstallStatus.Failed, false, "boom")));
    }
}
