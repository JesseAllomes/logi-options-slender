using System.Text.Json;

namespace Slender.Sync;

/// <summary>
/// slender.sync.json, filled in once by the user: clientId (the app registration) and optionally updateSource
/// (a folder or a GitHub repo URL for self-update). Looked up in %LOCALAPPDATA%\Slender first (it survives updates),
/// then next to the exe. Neither value is a secret, and neither lives in the repository.
/// </summary>
public sealed record SyncSettings(string? ClientId, string? UpdateSource)
{
    public const string FileName = "slender.sync.json";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && Guid.TryParse(ClientId, out _);

    public static SyncSettings Load(params string[] directories)
    {
        foreach (string dir in directories)
        {
            string path = Path.Combine(dir, FileName);
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                JsonElement root = doc.RootElement;
                return new SyncSettings(Read(root, "clientId"), Read(root, "updateSource"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable file counts as "not set up yet".
            }
        }

        return new SyncSettings(null, null);
    }

    /// <summary>The standard lookup: state folder, then the exe folder.</summary>
    public static SyncSettings LoadDefault() => Load(SyncPaths.StateDirectory, AppContext.BaseDirectory);

    private static string? Read(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in root.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                {
                    string? v = p.Value.GetString();
                    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                }
            }
        }

        return null;
    }
}

/// <summary>Folders. SLENDER_CONFIG_DIR redirects both, the same test hook the settings app uses.</summary>
public static class SyncPaths
{
    public const string ConfigDirEnvVar = "SLENDER_CONFIG_DIR";

    public static string? Override => Environment.GetEnvironmentVariable(ConfigDirEnvVar) is { Length: > 0 } d ? d : null;

    public static string ConfigDirectory => Override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slender");

    public static string StateDirectory => Override
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slender");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static string ManifestFile => Path.Combine(StateDirectory, "sync.json");
}
