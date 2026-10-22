using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Slender.Engine.ExcelAddin;

namespace Slender.Engine.Ringer;

/// <summary>HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.slender.ringer: its default value is the path of the host manifest.</summary>
public interface IRingerRegistry
{
    /// <summary>The default value, or null when the key or value is missing.</summary>
    string? ReadHostManifestPath();
    void WriteHostManifestPath(string path);
    /// <summary>Deletes the whole key; a missing key is fine.</summary>
    void DeleteHostKey();
}

public interface IRingerFiles
{
    bool Exists(string path);
    string? ReadAllText(string path);
    /// <summary>Writes the text (UTF-8, no byte order mark), creating the folder and replacing any file.</summary>
    void WriteAllText(string path, string text);
    /// <summary>Copies over any existing file, creating the folder. Throws IOException when the target is locked.</summary>
    void Copy(string source, string destination);
    /// <summary>Copies to a temporary name beside the destination, then moves it over in one step. Throws IOException when the destination is locked.</summary>
    void ReplaceAtomically(string source, string destination);
    /// <summary>File names (not paths) directly inside the folder; empty when it is missing.</summary>
    IReadOnlyList<string> ListFiles(string directory);
    /// <summary>Deletes the folder and everything in it; a missing folder is fine.</summary>
    void DeleteDirectory(string directory);
    /// <summary>Size and last-write time, or null when the file is missing. A stat only.</summary>
    AddinFileStamp? Stamp(string path);
}

public enum RingerInstallStatus { Done, InvalidOrigin, SourceMissing, Failed, NotSetUp, Removed }

public sealed record RingerInstallResult(RingerInstallStatus Status, string Message);

public enum RingerRefreshStatus { NotSetUp, SourceMissing, UpToDate, Refreshed, Deferred }

/// <summary>
/// Sets up and removes the 3CX ringer for the current user (docs/3cx-ringer.md): writes the extension folder (manifest with the 3CX
/// origin filled in), copies the native host out of Slender's own updated install folder, writes the host manifest and points Edge's
/// native-messaging key at it. Per user, no admin. Whatever value the key held before is saved first and put back by <see cref="Remove"/>.
/// Nothing here runs unless the user presses the button (or the tray refreshes the host copy after an update).
/// </summary>
public sealed class RingerInstaller
{
    public const string HostName = "com.slender.ringer";
    public const string HostExeName = "Slender.RingerHost.exe";
    public const string OriginPlaceholder = "__ORIGIN__";

    private readonly IRingerRegistry _registry;
    private readonly IRingerFiles _files;
    private readonly string _sourceExtension; // folder shipped with Slender
    private readonly string _sourceHost;      // Slender.RingerHost.exe shipped with Slender
    private readonly string _root;            // %LOCALAPPDATA%\Slender\edge-ringer

    public RingerInstaller(IRingerRegistry registry, IRingerFiles files, string sourceExtensionDirectory, string sourceHostExe, string rootDirectory)
    {
        _registry = registry;
        _files = files;
        _sourceExtension = sourceExtensionDirectory;
        _sourceHost = sourceHostExe;
        _root = rootDirectory;
    }

    /// <summary>The installer wired to the real registry and files. Used by Settings and the tray; tests use fakes.</summary>
    public static RingerInstaller CreateDefault() => new(new RegistryRingerRegistry(), new FileSystemRingerFiles(),
        Path.Combine(AppContext.BaseDirectory, "EdgeRinger", "extension"), Path.Combine(AppContext.BaseDirectory, HostExeName), DefaultRoot());

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slender", "edge-ringer");

    /// <summary>The folder the user picks in Edge's "Load unpacked".</summary>
    public string ExtensionDirectory => Path.Combine(_root, "extension");

    private string InstalledHost => Path.Combine(_root, HostExeName);
    private string HostManifest => Path.Combine(_root, HostName + ".json");
    private string StateFile => Path.Combine(_root, "state.json");

    /// <summary>True when the shipped extension and host are in this Slender install (a build without them cannot offer Set up).</summary>
    public bool IsAvailable() => _files.Exists(Path.Combine(_sourceExtension, "manifest.json")) && _files.Exists(_sourceHost);

    /// <summary>True when Edge's key points at our host manifest and the manifest is there.</summary>
    public bool IsSetUp() => SamePath(_registry.ReadHostManifestPath(), HostManifest) && _files.Exists(HostManifest);

    public RingerInstallResult SetUp(string? address)
    {
        if (RingerOrigin.Normalize(address) is not { } origin)
            return new(RingerInstallStatus.InvalidOrigin, "Type the 3CX web address as https://your-pbx-address (https only, no path).");
        if (!IsAvailable())
            return new(RingerInstallStatus.SourceMissing, "This Slender install does not include the 3CX ringer.");
        try
        {
            // Extension folder, rebuilt from the shipped files with the origin in the manifest.
            _files.DeleteDirectory(ExtensionDirectory);
            foreach (string name in _files.ListFiles(_sourceExtension))
            {
                string from = Path.Combine(_sourceExtension, name), to = Path.Combine(ExtensionDirectory, name);
                if (name == "manifest.json") _files.WriteAllText(to, WithOrigin(_files.ReadAllText(from) ?? string.Empty, origin));
                else _files.Copy(from, to);
            }

            _files.Copy(_sourceHost, InstalledHost);
            _files.WriteAllText(HostManifest, HostManifestJson(InstalledHost, ExtensionId()));

            // The value Edge's key held before Slender is kept once; setting up again must not record our own value as "previous".
            string? current = _registry.ReadHostManifestPath();
            bool ours = SamePath(current, HostManifest);
            if (!ours || _files.ReadAllText(StateFile) is null) _files.WriteAllText(StateFile, StateJson(ours ? null : current));
            _registry.WriteHostManifestPath(HostManifest);
            return new(RingerInstallStatus.Done, "Set up. Now load the extension in Edge (steps below).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(RingerInstallStatus.Failed, "Could not set up the 3CX ringer: " + ex.Message);
        }
    }

    /// <summary>Undoes <see cref="SetUp"/>: restores what Edge's key held before, then deletes Slender's ringer folder.</summary>
    public RingerInstallResult Remove()
    {
        try
        {
            bool had = _files.Exists(StateFile) || _files.Exists(HostManifest) || SamePath(_registry.ReadHostManifestPath(), HostManifest);
            if (!had) return new(RingerInstallStatus.NotSetUp, "The 3CX ringer is not set up.");
            // Only touch the key while it still points at us; another program may have taken the name since.
            if (SamePath(_registry.ReadHostManifestPath(), HostManifest))
            {
                if (PreviousFromState() is { } previous) _registry.WriteHostManifestPath(previous);
                else _registry.DeleteHostKey();
            }
            _files.DeleteDirectory(_root);
            return new(RingerInstallStatus.Removed, "Removed. In Edge, remove the extension on edge://extensions too.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(RingerInstallStatus.Failed, "Could not remove the 3CX ringer: " + ex.Message);
        }
    }

    /// <summary>
    /// After a Slender update: when set up and the host that now ships differs from the copy Edge runs, replaces the copy. Two stats on the
    /// usual path; never touches the registry. A host that is running at that moment (it lives a moment per call) makes this Deferred until the next start.
    /// </summary>
    public RingerRefreshStatus RefreshHostCopy()
    {
        try
        {
            if (!IsSetUp()) return RingerRefreshStatus.NotSetUp;
            if (_files.Stamp(_sourceHost) is not { } shipped) return RingerRefreshStatus.SourceMissing;
            if (_files.Stamp(InstalledHost) is { } have && have == shipped) return RingerRefreshStatus.UpToDate;
            _files.ReplaceAtomically(_sourceHost, InstalledHost);
            return RingerRefreshStatus.Refreshed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return RingerRefreshStatus.Deferred; }
    }

    /// <summary>The ID Edge gives the unpacked extension, from the key in the shipped manifest.</summary>
    public string ExtensionId() => ExtensionIdFromManifest(_files.ReadAllText(Path.Combine(_sourceExtension, "manifest.json")) ?? string.Empty);

    /// <summary>Reads "key" out of manifest text and turns it into the extension ID.</summary>
    public static string ExtensionIdFromManifest(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        return RingerOrigin.ExtensionId(doc.RootElement.GetProperty("key").GetString()!);
    }

    internal static string WithOrigin(string manifestTemplate, string origin)
    {
        if (!manifestTemplate.Contains(OriginPlaceholder, StringComparison.Ordinal)) throw new IOException("The shipped manifest has no origin placeholder.");
        return manifestTemplate.Replace(OriginPlaceholder, origin, StringComparison.Ordinal);
    }

    internal static string HostManifestJson(string hostExePath, string extensionId)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("name", HostName);
            w.WriteString("description", "Slender 3CX ringer");
            w.WriteString("path", hostExePath);
            w.WriteString("type", "stdio");
            w.WriteStartArray("allowed_origins");
            w.WriteStringValue($"chrome-extension://{extensionId}/");
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string StateJson(string? previous)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            if (previous is null) w.WriteNull("previousHostManifest"); else w.WriteString("previousHostManifest", previous);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string? PreviousFromState()
    {
        try
        {
            if (_files.ReadAllText(StateFile) is not { } text) return null;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("previousHostManifest", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class RegistryRingerRegistry : IRingerRegistry
{
    private const string KeyPath = @"Software\Microsoft\Edge\NativeMessagingHosts\" + RingerInstaller.HostName;

    public string? ReadHostManifestPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(null) as string;
    }

    public void WriteHostManifestPath(string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(null, path, RegistryValueKind.String);
    }

    public void DeleteHostKey() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}

public sealed class FileSystemRingerFiles : IRingerFiles
{
    public bool Exists(string path) => File.Exists(path);

    public string? ReadAllText(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

    public void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    public void ReplaceAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + ".new";
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception) { }
            throw;
        }
    }

    public IReadOnlyList<string> ListFiles(string directory) =>
        Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory).Select(f => Path.GetFileName(f))] : [];

    public void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    public AddinFileStamp? Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new AddinFileStamp(info.Length, info.LastWriteTimeUtc.Ticks) : null;
    }
}
