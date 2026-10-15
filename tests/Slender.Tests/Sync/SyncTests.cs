using System.Text;
using Slender.Sync;

namespace Slender.Tests.Sync;

public sealed class FakeAuth : ISyncAuth
{
    public SyncAccount? Account { get; set; } = new("acct-1", "me@outlook.com");

    public Task<SyncAccount?> GetAccountAsync(CancellationToken ct) => Task.FromResult(Account);

    public Task<SyncAccount> SignInAsync(CancellationToken ct) => Task.FromResult(Account ??= new SyncAccount("acct-1", "me@outlook.com"));

    public Task SignOutAsync(CancellationToken ct)
    {
        Account = null;
        return Task.CompletedTask;
    }

    public Task<string> GetTokenAsync(CancellationToken ct) => Task.FromResult("token");
}

public sealed class FakeGraph : ISyncGraph
{
    private int _version;

    public RemoteFile? Remote { get; private set; }

    public Dictionary<string, byte[]> Backups { get; } = [];

    public bool Offline { get; set; }

    public int Puts { get; private set; }

    public void Reset() => Remote = null;

    public void SetRemote(string text)
    {
        Remote = new RemoteFile("etag-" + ++_version, Encoding.UTF8.GetBytes(text));
    }

    public Task<RemoteFile?> GetConfigAsync(CancellationToken ct)
    {
        if (Offline)
        {
            throw new SyncOfflineException(new HttpRequestException("down"));
        }

        return Task.FromResult(Remote);
    }

    public Task<string> PutConfigAsync(byte[] content, string? ifMatchETag, CancellationToken ct)
    {
        if (Offline)
        {
            throw new SyncOfflineException(new HttpRequestException("down"));
        }

        if (ifMatchETag is not null && Remote?.ETag != ifMatchETag)
        {
            throw new SyncPreconditionFailedException();
        }

        Puts++;
        Remote = new RemoteFile("etag-" + ++_version, content);
        return Task.FromResult(Remote.ETag);
    }

    public Task PutBackupAsync(string fileName, byte[] content, CancellationToken ct)
    {
        Backups[fileName] = content;
        return Task.CompletedTask;
    }
}

public sealed class FakeFile : IConfigFile
{
    public byte[]? Content { get; set; }

    public byte[]? Read() => Content;

    public void Write(byte[] content) => Content = content;

    public void Set(string text) => Content = Encoding.UTF8.GetBytes(text);

    public string? Text => Content is null ? null : Encoding.UTF8.GetString(Content);
}

public sealed class SyncDecisionTests
{
    private static SyncManifest Synced(string hash = "H1", string etag = "E1") => new() { LastSyncedHash = hash, RemoteETag = etag };

    [Fact]
    public void Neither_changed_does_nothing() =>
        Assert.Equal(SyncAction.None, SyncDecision.Decide("H1", "E1", Synced()));

    [Fact]
    public void Remote_newer_and_local_unchanged_pulls() =>
        Assert.Equal(SyncAction.Pull, SyncDecision.Decide("H1", "E2", Synced()));

    [Fact]
    public void Local_changed_and_remote_unchanged_pushes() =>
        Assert.Equal(SyncAction.Push, SyncDecision.Decide("H2", "E1", Synced()));

    [Fact]
    public void Both_changed_is_a_conflict() =>
        Assert.Equal(SyncAction.Conflict, SyncDecision.Decide("H2", "E2", Synced()));

    [Fact]
    public void No_remote_pushes_local() =>
        Assert.Equal(SyncAction.Push, SyncDecision.Decide("H1", null, new SyncManifest()));

    [Fact]
    public void No_local_file_pulls() =>
        Assert.Equal(SyncAction.Pull, SyncDecision.Decide(null, "E1", new SyncManifest()));

    [Fact]
    public void First_sync_with_both_sides_is_a_conflict() =>
        Assert.Equal(SyncAction.Conflict, SyncDecision.Decide("H1", "E1", new SyncManifest()));
}

public sealed class SyncManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-sync-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Manifest_round_trips()
    {
        string path = Path.Combine(_dir, "sync.json");
        var m = new SyncManifest
        {
            AccountId = "a",
            RemoteETag = "e",
            LastSyncedHash = "h",
            LastSyncedUtc = new DateTimeOffset(2026, 10, 15, 1, 2, 3, TimeSpan.Zero),
            ConflictNotice = "n",
        };
        m.Save(path);

        SyncManifest back = SyncManifest.Load(path);
        Assert.Equal("a", back.AccountId);
        Assert.Equal("e", back.RemoteETag);
        Assert.Equal("h", back.LastSyncedHash);
        Assert.Equal(m.LastSyncedUtc, back.LastSyncedUtc);
        Assert.Equal("n", back.ConflictNotice);
    }

    [Fact]
    public void Missing_or_corrupt_manifest_is_empty()
    {
        string path = Path.Combine(_dir, "sync.json");
        Assert.Null(SyncManifest.Load(path).LastSyncedHash);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ not json");
        Assert.Null(SyncManifest.Load(path).LastSyncedHash);
    }

    [Fact]
    public void Missing_client_id_means_not_configured()
    {
        Directory.CreateDirectory(_dir);
        Assert.False(SyncSettings.Load(_dir).IsConfigured);

        File.WriteAllText(Path.Combine(_dir, SyncSettings.FileName), "{ \"clientId\": \"\" }");
        Assert.False(SyncSettings.Load(_dir).IsConfigured);

        File.WriteAllText(Path.Combine(_dir, SyncSettings.FileName), "{ \"clientId\": \"not-a-guid\" }");
        Assert.False(SyncSettings.Load(_dir).IsConfigured);

        File.WriteAllText(Path.Combine(_dir, SyncSettings.FileName), "{ \"clientId\": \"11111111-2222-3333-4444-555555555555\", \"updateSource\": \"C:\\\\feed\" }");
        SyncSettings ok = SyncSettings.Load(_dir);
        Assert.True(ok.IsConfigured);
        Assert.Equal("C:\\feed", ok.UpdateSource);
    }

    [Fact]
    public async Task Unconfigured_service_reports_not_configured_and_never_touches_anything()
    {
        var file = new FileConfigFile(Path.Combine(_dir, "config.json"));
        var svc = new SyncService(new SyncSettings(null, null), null, null, file, Path.Combine(_dir, "sync.json"));

        Assert.False(svc.IsConfigured);
        Assert.Null(await svc.GetAccountAsync());
        Assert.Equal(SyncOutcome.NotConfigured, (await svc.SyncNowAsync()).Outcome);
        Assert.Equal(SyncOutcome.NotConfigured, (await svc.SignInAsync()).Outcome);
        Assert.False(File.Exists(Path.Combine(_dir, "sync.json")));
    }
}

public sealed class SyncServiceTests : IDisposable
{
    private const string Mine = "{\"version\":2,\"who\":\"mine\"}";
    private const string Theirs = "{\"version\":2,\"who\":\"theirs\"}";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-sync-test-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAuth _auth = new();
    private readonly FakeGraph _graph = new();
    private readonly FakeFile _file = new();
    private readonly DateTimeOffset _now = new(2026, 10, 15, 10, 15, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string ManifestPath => Path.Combine(_dir, "sync.json");

    private SyncService Create() => new(
        new SyncSettings("11111111-2222-3333-4444-555555555555", null), _auth, _graph, _file, ManifestPath, () => _now);

    [Fact]
    public async Task Signed_out_does_nothing()
    {
        _auth.Account = null;
        _file.Set(Mine);
        Assert.Equal(SyncOutcome.NotSignedIn, (await Create().SyncNowAsync()).Outcome);
        Assert.Null(_graph.Remote);
    }

    [Fact]
    public async Task First_sync_with_empty_cloud_pushes()
    {
        _file.Set(Mine);
        SyncResult r = await Create().SyncNowAsync();
        Assert.Equal(SyncOutcome.Pushed, r.Outcome);
        Assert.Equal(Mine, Encoding.UTF8.GetString(_graph.Remote!.Content));
        Assert.Equal(_now, SyncManifest.Load(ManifestPath).LastSyncedUtc);
    }

    [Fact]
    public async Task Fresh_pc_takes_the_cloud_copy()
    {
        _graph.SetRemote(Theirs);
        SyncResult r = await Create().SyncNowAsync();
        Assert.Equal(SyncOutcome.Pulled, r.Outcome);
        Assert.Equal(Theirs, _file.Text);
    }

    [Fact]
    public async Task Remote_newer_and_local_unchanged_pulls()
    {
        _file.Set(Mine);
        SyncService svc = Create();
        await svc.SyncNowAsync();
        _graph.SetRemote(Theirs);

        SyncResult r = await svc.SyncNowAsync();
        Assert.Equal(SyncOutcome.Pulled, r.Outcome);
        Assert.Equal(Theirs, _file.Text);

        Assert.Equal(SyncOutcome.UpToDate, (await svc.SyncNowAsync()).Outcome);
    }

    [Fact]
    public async Task Local_changed_and_remote_unchanged_pushes()
    {
        _file.Set(Mine);
        SyncService svc = Create();
        await svc.SyncNowAsync();
        _file.Set("{\"version\":2,\"who\":\"edited\"}");

        Assert.Equal(SyncOutcome.Pushed, (await svc.SyncNowAsync()).Outcome);
        Assert.Contains("edited", Encoding.UTF8.GetString(_graph.Remote!.Content));
        Assert.Empty(_graph.Backups);
    }

    [Fact]
    public async Task Both_changed_keeps_local_backs_up_remote_and_leaves_a_notice()
    {
        _file.Set(Mine);
        SyncService svc = Create();
        await svc.SyncNowAsync();
        _graph.SetRemote(Theirs);
        _file.Set("{\"version\":2,\"who\":\"local-edit\"}");

        SyncResult r = await svc.SyncNowAsync();
        Assert.Equal(SyncOutcome.ConflictKeptLocal, r.Outcome);
        Assert.Equal("Settings were changed on another PC; kept this PC's version.", r.Message);
        Assert.Contains("local-edit", _file.Text);
        Assert.Contains("local-edit", Encoding.UTF8.GetString(_graph.Remote!.Content));

        KeyValuePair<string, byte[]> backup = Assert.Single(_graph.Backups);
        Assert.Equal("config.conflict-20261015T101500Z.json", backup.Key);
        Assert.Equal(Theirs, Encoding.UTF8.GetString(backup.Value));
        Assert.Equal(SyncService.ConflictMessage, svc.Manifest.ConflictNotice);

        svc.DismissConflictNotice();
        Assert.Null(svc.Manifest.ConflictNotice);
    }

    [Fact]
    public async Task Identical_content_on_both_sides_is_adopted_without_a_conflict()
    {
        _file.Set(Mine);
        _graph.SetRemote(Mine);
        SyncResult r = await Create().SyncNowAsync();
        Assert.Equal(SyncOutcome.UpToDate, r.Outcome);
        Assert.Empty(_graph.Backups);
        Assert.Equal(0, _graph.Puts);
    }

    [Fact]
    public async Task Offline_is_reported_and_changes_nothing()
    {
        _file.Set(Mine);
        _graph.Offline = true;
        SyncResult r = await Create().SyncNowAsync();
        Assert.Equal(SyncOutcome.Offline, r.Outcome);
        Assert.Equal(Mine, _file.Text);
        Assert.False(File.Exists(ManifestPath));
    }

    [Fact]
    public async Task Unreadable_remote_is_not_pulled_over_local()
    {
        _graph.SetRemote("not json at all");
        _file.Content = null;
        SyncResult r = await Create().SyncNowAsync();
        Assert.Equal(SyncOutcome.Failed, r.Outcome);
        Assert.Null(_file.Content);
    }

    [Fact]
    public async Task Sign_out_clears_the_baseline_but_not_the_files()
    {
        _file.Set(Mine);
        SyncService svc = Create();
        await svc.SyncNowAsync();
        await svc.SignOutAsync();

        Assert.Null(await svc.GetAccountAsync());
        Assert.Null(svc.Manifest.LastSyncedHash);
        Assert.Equal(Mine, _file.Text);
        Assert.NotNull(_graph.Remote);
    }

    [Fact]
    public async Task A_different_account_starts_from_scratch()
    {
        _file.Set(Mine);
        SyncService svc = Create();
        await svc.SyncNowAsync();

        _auth.Account = new SyncAccount("acct-2", "other@outlook.com");
        // New account, empty cloud: pushes rather than treating the old eTag as a baseline.
        _graph.Reset();
        Assert.Equal(SyncOutcome.Pushed, (await svc.SyncNowAsync()).Outcome);
        Assert.Equal("acct-2", svc.Manifest.AccountId);
    }
}
