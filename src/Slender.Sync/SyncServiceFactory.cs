using System.Text;

namespace Slender.Sync;

/// <summary>config.json on disk, written atomically (temp file then move), as JsonConfigStore does.</summary>
public sealed class FileConfigFile : IConfigFile
{
    private readonly string _path;

    public FileConfigFile(string path)
    {
        _path = path;
    }

    public byte[]? Read()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllBytes(_path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".sync.tmp";
        File.WriteAllBytes(temp, content);
        File.Move(temp, _path, overwrite: true);
    }
}

public static class SyncServiceFactory
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The real service for this PC. Without a client ID it is created unconfigured (IsConfigured is false).</summary>
    public static SyncService Create()
    {
        SyncSettings settings = SyncSettings.LoadDefault();
        var file = new FileConfigFile(SyncPaths.ConfigFile);
        if (!settings.IsConfigured)
        {
            return new SyncService(settings, null, null, file, SyncPaths.ManifestFile);
        }

        var auth = new MsalSyncAuth(settings.ClientId!, SyncPaths.StateDirectory);
        var graph = new GraphSyncClient(Http, auth.GetTokenAsync);
        return new SyncService(settings, auth, graph, file, SyncPaths.ManifestFile);
    }

    /// <summary>docs/sync.md, copied next to the exe by scripts/Build-Setup.ps1. Null when it is not there (a dev build).</summary>
    public static string? SetupDocsPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "docs", "sync.md");
        return File.Exists(path) ? path : null;
    }
}
