using System.Text.Json;

namespace Slender.Sync;

/// <summary>
/// Syncs config.json with the user's OneDrive app folder. Offline and signed-out are normal states, never exceptions.
/// Sign-in, Graph and the file are behind interfaces; tests use fakes. One pass at a time per process, and across
/// processes (tray and settings app) through a lock file.
/// </summary>
public sealed class SyncService
{
    public const string ConflictMessage = "Settings were changed on another PC; kept this PC's version.";

    private readonly SyncSettings _settings;
    private readonly ISyncAuth? _auth;
    private readonly ISyncGraph? _graph;
    private readonly IConfigFile _file;
    private readonly string _manifestPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(SyncSettings settings, ISyncAuth? auth, ISyncGraph? graph, IConfigFile file, string manifestPath, Func<DateTimeOffset>? clock = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _auth = auth;
        _graph = graph;
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>False until the user has put an app registration client ID in slender.sync.json.</summary>
    public bool IsConfigured => _settings.IsConfigured && _auth is not null && _graph is not null;

    public SyncSettings Settings => _settings;

    public SyncManifest Manifest => SyncManifest.Load(_manifestPath);

    public async Task<SyncAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            return await _auth!.GetAccountAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Opens the browser, then runs a first sync pass. Throws only if sign-in itself fails.</summary>
    public async Task<SyncResult> SignInAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new SyncResult(SyncOutcome.NotConfigured, "Sign-in isn't set up yet.");
        }

        try
        {
            await _auth!.SignInAsync(ct).ConfigureAwait(false);
        }
        catch (SyncOfflineException)
        {
            return new SyncResult(SyncOutcome.Offline, "No connection to Microsoft. Try again when you are online.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new SyncResult(SyncOutcome.Failed, "Sign-in failed: " + e.Message);
        }

        return await SyncNowAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Forgets the account and the sync baseline. Local settings and the cloud copy are left alone.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        if (_auth is not null)
        {
            await _auth.SignOutAsync(ct).ConfigureAwait(false);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            new SyncManifest().Save(_manifestPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void DismissConflictNotice()
    {
        SyncManifest m = Manifest;
        if (m.ConflictNotice is not null)
        {
            m.ConflictNotice = null;
            m.Save(_manifestPath);
        }
    }

    /// <summary>One pull/push pass following the decision table.</summary>
    public async Task<SyncResult> SyncNowAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new SyncResult(SyncOutcome.NotConfigured, "Sign-in isn't set up yet.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using FileStream? processLock = TryLockFile();
            if (processLock is null)
            {
                return new SyncResult(SyncOutcome.UpToDate, "Another Slender process is syncing.");
            }

            return await RunAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        try
        {
            SyncAccount? account = await _auth!.GetAccountAsync(ct).ConfigureAwait(false);
            if (account is null)
            {
                return new SyncResult(SyncOutcome.NotSignedIn, "Not signed in.");
            }

            SyncManifest manifest = SyncManifest.Load(_manifestPath);
            if (manifest.AccountId != account.Id)
            {
                manifest = new SyncManifest { AccountId = account.Id, ConflictNotice = manifest.ConflictNotice };
            }

            RemoteFile? remote = await _graph!.GetConfigAsync(ct).ConfigureAwait(false);
            byte[]? local = _file.Read();
            string? localHash = local is null ? null : SyncManifest.Hash(local);

            SyncAction action = SyncDecision.Decide(localHash, remote?.ETag, manifest);
            if (action != SyncAction.None && remote is not null && localHash is not null && SyncManifest.Hash(remote.Content) == localHash)
            {
                // Same content on both sides (second PC with identical settings): adopt the remote eTag, move nothing.
                Record(manifest, remote.ETag, localHash);
                return new SyncResult(SyncOutcome.UpToDate, "Settings are in sync.");
            }

            switch (action)
            {
                case SyncAction.None:
                    return new SyncResult(SyncOutcome.UpToDate, "Settings are in sync.");

                case SyncAction.Pull:
                    if (!IsJsonObject(remote!.Content))
                    {
                        return new SyncResult(SyncOutcome.Failed, "The settings in OneDrive could not be read; left this PC's copy alone.");
                    }

                    _file.Write(remote.Content);
                    Record(manifest, remote.ETag, SyncManifest.Hash(remote.Content));
                    return new SyncResult(SyncOutcome.Pulled, "Settings updated from OneDrive.");

                case SyncAction.Push:
                    string pushed = await _graph.PutConfigAsync(local!, remote?.ETag, ct).ConfigureAwait(false);
                    Record(manifest, pushed, localHash!);
                    return new SyncResult(SyncOutcome.Pushed, "Settings saved to OneDrive.");

                default: // Conflict
                    string stamp = _clock().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
                    await _graph.PutBackupAsync($"config.conflict-{stamp}.json", remote!.Content, ct).ConfigureAwait(false);
                    string etag = await _graph.PutConfigAsync(local!, remote.ETag, ct).ConfigureAwait(false);
                    manifest.ConflictNotice = ConflictMessage;
                    Record(manifest, etag, localHash!);
                    return new SyncResult(SyncOutcome.ConflictKeptLocal, ConflictMessage);
            }
        }
        catch (SyncOfflineException)
        {
            return new SyncResult(SyncOutcome.Offline, "Offline. Slender will sync when it can.");
        }
        catch (SyncSignInRequiredException e)
        {
            return new SyncResult(SyncOutcome.NotSignedIn, e.Message);
        }
        catch (SyncPreconditionFailedException)
        {
            return new SyncResult(SyncOutcome.Failed, "Settings changed on another PC while syncing. Slender will retry.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new SyncResult(SyncOutcome.Failed, "Sync failed: " + e.Message);
        }
    }

    private void Record(SyncManifest manifest, string eTag, string hash)
    {
        manifest.RemoteETag = eTag;
        manifest.LastSyncedHash = hash;
        manifest.LastSyncedUtc = _clock();
        manifest.Save(_manifestPath);
    }

    private static bool IsJsonObject(byte[] content)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private FileStream? TryLockFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
            return new FileStream(_manifestPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
