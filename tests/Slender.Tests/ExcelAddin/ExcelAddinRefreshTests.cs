using Slender.Engine.ExcelAddin;

namespace Slender.Tests.ExcelAddin;

public class ExcelAddinRefreshTests
{
    private const string Source = @"C:\App\ExcelAddin\" + ExcelAddinInstaller.XllFileName;
    private const string Target = @"C:\Data\ExcelAddin\" + ExcelAddinInstaller.XllFileName;

    private sealed class Rig
    {
        public FakeRegistry Registry { get; } = new();
        public FakeFiles Files { get; } = new();
        public FakeExcelProbe Excel { get; } = new();
        public ExcelAddinInstaller Installer { get; }

        public Rig(bool installed = true, AddinFileStamp? have = null)
        {
            Files.Present.Add(Source);
            Files.Stamps[Source] = new AddinFileStamp(100, 5);
            if (have is { } h)
            {
                Files.Present.Add(Target);
                Files.Stamps[Target] = h;
            }
            if (installed) Registry.Values["OPEN"] = ExcelAddinInstaller.ValueFor(Target);
            Installer = new ExcelAddinInstaller(Registry, Files, Excel, Source, Target);
        }
    }

    [Fact]
    public void Not_installed_does_nothing()
    {
        var r = new Rig(installed: false, have: new AddinFileStamp(1, 1));
        Assert.Equal(AddinRefreshStatus.NotInstalled, r.Installer.RefreshInstalledCopy());
        Assert.Empty(r.Files.Replaces);
    }

    [Fact]
    public void Someone_elses_open_entry_is_left_alone()
    {
        var r = new Rig(installed: false, have: new AddinFileStamp(1, 1));
        r.Registry.Values["OPEN"] = "/R \"C:\\Other\\" + ExcelAddinInstaller.XllFileName + "\"";
        Assert.Equal(AddinRefreshStatus.NotInstalled, r.Installer.RefreshInstalledCopy());
        Assert.Empty(r.Files.Replaces);
    }

    [Fact]
    public void Same_size_and_time_is_up_to_date_without_hashing()
    {
        var r = new Rig(have: new AddinFileStamp(100, 5));
        r.Excel.Running = true;
        Assert.Equal(AddinRefreshStatus.UpToDate, r.Installer.RefreshInstalledCopy());
        Assert.Equal(0, r.Files.HashCalls);
        Assert.Empty(r.Files.Replaces);
    }

    [Fact]
    public void Same_size_other_time_hashes_and_keeps_an_identical_copy()
    {
        var r = new Rig(have: new AddinFileStamp(100, 9));
        r.Files.SameBytes = true;
        Assert.Equal(AddinRefreshStatus.UpToDate, r.Installer.RefreshInstalledCopy());
        Assert.Equal(1, r.Files.HashCalls);
        Assert.Empty(r.Files.Replaces);
    }

    [Fact]
    public void Same_size_other_time_with_different_bytes_is_replaced()
    {
        var r = new Rig(have: new AddinFileStamp(100, 9));
        Assert.Equal(AddinRefreshStatus.Refreshed, r.Installer.RefreshInstalledCopy());
        Assert.Equal([(Source, Target)], r.Files.Replaces);
    }

    [Fact]
    public void Different_size_is_replaced_without_hashing_and_never_writes_the_registry()
    {
        var r = new Rig(have: new AddinFileStamp(90, 1));
        Assert.Equal(AddinRefreshStatus.Refreshed, r.Installer.RefreshInstalledCopy());
        Assert.Equal(0, r.Files.HashCalls);
        Assert.Empty(r.Registry.Writes);
    }

    [Fact]
    public void Excel_running_defers_and_a_later_call_with_excel_closed_refreshes()
    {
        var r = new Rig(have: new AddinFileStamp(90, 1));
        r.Excel.Running = true;
        Assert.Equal(AddinRefreshStatus.Deferred, r.Installer.RefreshInstalledCopy());
        Assert.Empty(r.Files.Replaces);
        r.Excel.Running = false;
        Assert.Equal(AddinRefreshStatus.Refreshed, r.Installer.RefreshInstalledCopy());
    }

    [Fact]
    public void A_locked_target_defers_and_leaves_the_old_copy()
    {
        var r = new Rig(have: new AddinFileStamp(90, 1));
        r.Files.LockedReplace = true;
        Assert.Equal(AddinRefreshStatus.Deferred, r.Installer.RefreshInstalledCopy());
        Assert.Equal(new AddinFileStamp(90, 1), r.Files.Stamps[Target]);
    }

    [Fact]
    public void A_missing_installed_copy_is_restored_even_with_excel_running()
    {
        var r = new Rig(have: null);
        r.Excel.Running = true;
        Assert.Equal(AddinRefreshStatus.Refreshed, r.Installer.RefreshInstalledCopy());
    }

    [Fact]
    public void A_build_without_the_shipped_file_reports_it()
    {
        var r = new Rig(have: new AddinFileStamp(90, 1));
        r.Files.Stamps.Remove(Source);
        Assert.Equal(AddinRefreshStatus.SourceMissing, r.Installer.RefreshInstalledCopy());
    }
}
