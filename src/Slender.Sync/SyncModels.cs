using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slender.Sync;

/// <summary>What one sync pass did.</summary>
public enum SyncOutcome
{
    NotConfigured,
    NotSignedIn,
    Offline,
    UpToDate,
    Pulled,
    Pushed,
    ConflictKeptLocal,
    Failed,
}

public sealed record SyncResult(SyncOutcome Outcome, string Message)
{
    public bool IsError => Outcome is SyncOutcome.Failed or SyncOutcome.Offline;
}

/// <summary>The signed-in Microsoft account.</summary>
public sealed record SyncAccount(string Id, string Name);

/// <summary>The remote config.json: its eTag and bytes.</summary>
public sealed record RemoteFile(string ETag, byte[] Content);

/// <summary>Raised by the Graph and auth layers when there is no network. The service reports Offline.</summary>
public sealed class SyncOfflineException : Exception
{
    public SyncOfflineException(Exception inner)
        : base("No connection to Microsoft.", inner)
    {
    }
}

/// <summary>Raised when the saved sign-in no longer works (expired or revoked). The user must sign in again.</summary>
public sealed class SyncSignInRequiredException : Exception
{
    public SyncSignInRequiredException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>The remote copy changed between our read and our write.</summary>
public sealed class SyncPreconditionFailedException : Exception
{
    public SyncPreconditionFailedException()
        : base("The remote settings changed while uploading.")
    {
    }
}

/// <summary>Sign-in. Tokens are cached DPAPI-encrypted under %LOCALAPPDATA%\Slender.</summary>
public interface ISyncAuth
{
    /// <summary>The cached account, or null when signed out. Never prompts.</summary>
    Task<SyncAccount?> GetAccountAsync(CancellationToken ct);

    /// <summary>Opens the system browser. Returns the account.</summary>
    Task<SyncAccount> SignInAsync(CancellationToken ct);

    Task SignOutAsync(CancellationToken ct);

    /// <summary>A bearer token for Graph, refreshed silently. Throws SyncSignInRequiredException when it cannot.</summary>
    Task<string> GetTokenAsync(CancellationToken ct);
}

/// <summary>The Graph calls sync needs. All paths are inside the OneDrive app folder (special/approot).</summary>
public interface ISyncGraph
{
    /// <summary>null when the file does not exist yet.</summary>
    Task<RemoteFile?> GetConfigAsync(CancellationToken ct);

    /// <summary>Uploads config.json. When ifMatchETag is set and the remote changed, throws SyncPreconditionFailedException. Returns the new eTag.</summary>
    Task<string> PutConfigAsync(byte[] content, string? ifMatchETag, CancellationToken ct);

    /// <summary>Uploads a side copy (for example config.conflict-20261015T101500Z.json).</summary>
    Task PutBackupAsync(string fileName, byte[] content, CancellationToken ct);
}

/// <summary>The local config.json as bytes. Writing is atomic.</summary>
public interface IConfigFile
{
    byte[]? Read();

    void Write(byte[] content);
}

/// <summary>What the last sync left behind, stored in %LOCALAPPDATA%\Slender\sync.json.</summary>
public sealed class SyncManifest
{
    public string? AccountId { get; set; }

    public string? RemoteETag { get; set; }

    /// <summary>SHA-256 (hex) of the config.json bytes at the last sync. Local is "changed" when its hash differs.</summary>
    public string? LastSyncedHash { get; set; }

    public DateTimeOffset? LastSyncedUtc { get; set; }

    /// <summary>Set after a conflict until the user dismisses it in the settings app.</summary>
    public string? ConflictNotice { get; set; }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    public string ToJson() => JsonSerializer.Serialize(this, SyncJsonContext.Default.SyncManifest);

    public static SyncManifest FromJson(string json) => JsonSerializer.Deserialize(json, SyncJsonContext.Default.SyncManifest) ?? new SyncManifest();

    /// <summary>Missing or unreadable file gives an empty manifest (as if never synced).</summary>
    public static SyncManifest Load(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new SyncManifest();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SyncManifest();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Source-generated JSON metadata for sync.json (no reflection; NativeAOT safe).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SyncManifest))]
internal sealed partial class SyncJsonContext : JsonSerializerContext
{
}
