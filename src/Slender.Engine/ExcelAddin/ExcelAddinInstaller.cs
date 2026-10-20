using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace Slender.Engine.ExcelAddin;

/// <summary>The "OPEN" values of HKCU\Software\Microsoft\Office\16.0\Excel\Options. Excel opens every file named there at start.</summary>
public interface IExcelAddinRegistry
{
    /// <summary>Every value whose name is OPEN or OPEN followed by digits, with its string data.</summary>
    IReadOnlyList<KeyValuePair<string, string>> ReadOpenValues();
    void WriteOpenValue(string name, string data);
    void DeleteOpenValue(string name);
}

public interface IExcelAddinFiles
{
    bool Exists(string path);
    /// <summary>Copies over any existing file, creating the folder. Throws IOException when the target is locked.</summary>
    void Copy(string source, string destination);
    /// <summary>Deletes the file; a missing file is fine.</summary>
    void Delete(string path);
    /// <summary>Size and last-write time of the file, or null when it is missing. A stat only; the file is not opened.</summary>
    AddinFileStamp? Stamp(string path);
    /// <summary>True when both files hold the same bytes (hashes them; callers do this only when the stamps differ in time).</summary>
    bool SameContent(string a, string b);
    /// <summary>
    /// Copies to a temporary name beside <paramref name="destination"/>, then moves it over the destination in one step, so the
    /// destination is always whole. Throws IOException or UnauthorizedAccessException when the destination is locked; the temp file is removed.
    /// </summary>
    void ReplaceAtomically(string source, string destination);
}

/// <summary>What a stat says about a file: byte length and last write (UTC ticks).</summary>
public readonly record struct AddinFileStamp(long Length, long WriteTicksUtc);

public interface IExcelProcessProbe
{
    bool IsExcelRunning();
}

public enum AddinRefreshStatus { NotInstalled, SourceMissing, UpToDate, Refreshed, Deferred, Failed }

public enum AddinInstallStatus { Installed, AlreadyInstalled, Removed, NotInstalled, SourceMissing, Failed }

/// <param name="ExcelWasRunning">Excel was open when this ran. Excel reads its add-ins only at start, so it must be closed and opened again.</param>
public sealed record AddinInstallResult(AddinInstallStatus Status, bool ExcelWasRunning, string Message);

/// <summary>
/// Turns the Slender Excel add-in on and off for the current user. Per user, no admin, no COM registration: the add-in is an .xll
/// that Excel opens because of an OPEN value under HKCU. Slender copies the .xll out of its own (updated) install folder to a stable
/// folder first, so Slender updates never fight Excel for a locked file. Nothing here runs unless the user flips the switch.
/// </summary>
public sealed class ExcelAddinInstaller
{
    /// <summary>The 64-bit packed add-in as built by src/Slender.ExcelAddin and shipped in the install folder's ExcelAddin folder.</summary>
    public const string XllFileName = "Slender.ExcelAddin64-packed.xll";

    private const string OpenName = "OPEN";

    private readonly IExcelAddinRegistry _registry;
    private readonly IExcelAddinFiles _files;
    private readonly IExcelProcessProbe _excel;
    private readonly string _sourceXll;
    private readonly string _installedXll;

    public ExcelAddinInstaller(IExcelAddinRegistry registry, IExcelAddinFiles files, IExcelProcessProbe excel, string sourceXll, string installedXll)
    {
        _registry = registry;
        _files = files;
        _excel = excel;
        _sourceXll = sourceXll;
        _installedXll = installedXll;
    }

    /// <summary>The installer wired to the real registry, files and processes. Used by Settings; tests use fakes.</summary>
    public static ExcelAddinInstaller CreateDefault() => new(new RegistryExcelAddinRegistry(), new FileSystemExcelAddinFiles(),
        new ExcelProcessProbe(), Path.Combine(AppContext.BaseDirectory, "ExcelAddin", XllFileName), DefaultInstalledPath());

    public static string DefaultInstalledPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slender", "ExcelAddin", XllFileName);

    /// <summary>True when an OPEN value points at Slender's add-in.</summary>
    public bool IsInstalled() => FindOurs(ReadSlots()) >= 0;

    /// <summary>True when the add-in file shipped with this Slender exists (a build without it cannot offer the switch).</summary>
    public bool IsAvailable() => _files.Exists(_sourceXll);

    public AddinInstallResult Install()
    {
        var running = _excel.IsExcelRunning();
        if (!_files.Exists(_sourceXll))
            return new(AddinInstallStatus.SourceMissing, running, "This Slender install does not include the Excel add-in.");
        try
        {
            var slots = ReadSlots();
            var ours = FindOurs(slots);
            try { _files.Copy(_sourceXll, _installedXll); }
            catch (IOException) when (running && _files.Exists(_installedXll)) { } // locked by the open Excel; the copy already there is used
            var value = ValueFor(_installedXll);
            if (ours >= 0)
            {
                if (slots[ours].Value == value) return new(AddinInstallStatus.AlreadyInstalled, running, "The Excel add-in is already on.");
                _registry.WriteOpenValue(slots[ours].Name, value);
                return new(AddinInstallStatus.Installed, running, Done(running));
            }
            _registry.WriteOpenValue(NameFor(FirstFreeSlot(slots)), value);
            return new(AddinInstallStatus.Installed, running, Done(running));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(AddinInstallStatus.Failed, running, running ? "Close Excel and try again." : "Could not turn the Excel add-in on: " + ex.Message);
        }
    }

    /// <summary>
    /// After a Slender update: brings the installed copy up to the .xll that now ships, when the add-in is on and the two differ.
    /// Cheap on the usual path (two stats). Never touches the registry. Never replaces a file Excel may have loaded: when Excel is
    /// running or the replace hits a lock it returns Deferred and the next call (next tray start, or Settings opening) tries again.
    /// </summary>
    public AddinRefreshStatus RefreshInstalledCopy()
    {
        try
        {
            var slots = ReadSlots();
            var ours = FindOurs(slots);
            if (ours < 0 || slots[ours].Value != ValueFor(_installedXll)) return AddinRefreshStatus.NotInstalled;
            if (_files.Stamp(_sourceXll) is not { } shipped) return AddinRefreshStatus.SourceMissing;
            if (_files.Stamp(_installedXll) is { } have && have.Length == shipped.Length
                && (have.WriteTicksUtc == shipped.WriteTicksUtc || _files.SameContent(_sourceXll, _installedXll)))
                return AddinRefreshStatus.UpToDate;
            if (_excel.IsExcelRunning() && _files.Exists(_installedXll)) return AddinRefreshStatus.Deferred; // loaded in Excel, so locked
            _files.ReplaceAtomically(_sourceXll, _installedXll);
            return AddinRefreshStatus.Refreshed;
        }
        catch (IOException) { return AddinRefreshStatus.Deferred; } // locked after all; try again later
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return AddinRefreshStatus.Failed; }
    }

    public AddinInstallResult Uninstall()
    {
        var running = _excel.IsExcelRunning();
        try
        {
            var slots = ReadSlots();
            var ours = FindOurs(slots);
            if (ours < 0) return new(AddinInstallStatus.NotInstalled, running, "The Excel add-in is already off.");
            // Excel stops reading OPEN values at the first missing number, so close the gap by moving the later ones down.
            for (var i = ours; i < slots.Count - 1; i++) _registry.WriteOpenValue(slots[i].Name, slots[i + 1].Value);
            _registry.DeleteOpenValue(slots[^1].Name);
            try { _files.Delete(_installedXll); } catch (IOException) { /* still loaded in the open Excel; removed on the next toggle */ }
            return new(AddinInstallStatus.Removed, running, running ? "The add-in is off. Close Excel; it stays loaded until then." : "The Excel add-in is off.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(AddinInstallStatus.Failed, running, "Could not turn the Excel add-in off: " + ex.Message);
        }
    }

    private static string Done(bool running) =>
        running ? "The add-in is on. Close Excel and open it again to load it." : "The add-in is on. It loads the next time Excel starts.";

    /// <summary>The contiguous run OPEN, OPEN1, OPEN2... in order, as Excel reads it.</summary>
    private List<(string Name, string Value)> ReadSlots()
    {
        var byIndex = new Dictionary<int, (string Name, string Value)>();
        foreach (var (name, data) in _registry.ReadOpenValues())
        {
            if (TryIndex(name, out var index)) byIndex[index] = (name, data);
        }
        var run = new List<(string, string)>();
        while (byIndex.TryGetValue(run.Count, out var slot)) run.Add(slot);
        return run;
    }

    /// <summary>The slot holding Slender's entry, or -1.</summary>
    private int FindOurs(List<(string Name, string Value)> slots)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (IsOurs(slots[i].Value)) return i;
        }
        return -1;
    }

    private static bool IsOurs(string data) => data.Contains(XllFileName, StringComparison.OrdinalIgnoreCase);

    private static int FirstFreeSlot(List<(string Name, string Value)> slots) => slots.Count;

    internal static string ValueFor(string xllPath) => "/R \"" + xllPath + "\"";

    internal static string NameFor(int index) => index == 0 ? OpenName : OpenName + index.ToString(CultureInfo.InvariantCulture);

    private static bool TryIndex(string name, out int index)
    {
        index = 0;
        if (name.Equals(OpenName, StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith(OpenName, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(OpenName.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index) && index > 0;
    }
}

/// <summary>HKCU\Software\Microsoft\Office\16.0\Excel\Options (Office 2016, 2019, 2021 and Microsoft 365 all use 16.0).</summary>
public sealed class RegistryExcelAddinRegistry : IExcelAddinRegistry
{
    private const string KeyPath = @"Software\Microsoft\Office\16.0\Excel\Options";

    public IReadOnlyList<KeyValuePair<string, string>> ReadOpenValues()
    {
        var list = new List<KeyValuePair<string, string>>();
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null) return list;
        foreach (var name in key.GetValueNames())
        {
            if (name.StartsWith("OPEN", StringComparison.OrdinalIgnoreCase) && key.GetValue(name) is string data)
                list.Add(new KeyValuePair<string, string>(name, data));
        }
        return list;
    }

    public void WriteOpenValue(string name, string data)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, data, RegistryValueKind.String);
    }

    public void DeleteOpenValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class FileSystemExcelAddinFiles : IExcelAddinFiles
{
    public bool Exists(string path) => File.Exists(path);

    public void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    public void Delete(string path) => File.Delete(path);

    public AddinFileStamp? Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new AddinFileStamp(info.Length, info.LastWriteTimeUtc.Ticks) : null;
    }

    public bool SameContent(string a, string b)
    {
        using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return System.Security.Cryptography.SHA256.HashData(sa).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(sb));
    }

    public void ReplaceAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".new";
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, destination, overwrite: true); // fails while Excel has the destination loaded
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception) { }
            throw;
        }
    }
}

public sealed class ExcelProcessProbe : IExcelProcessProbe
{
    public bool IsExcelRunning()
    {
        var found = Process.GetProcessesByName("EXCEL");
        try { return found.Length > 0; }
        finally { foreach (var p in found) p.Dispose(); }
    }
}
