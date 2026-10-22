using System.Text.Json;
using Slender.Engine.ExcelAddin;
using Slender.Engine.Ringer;

namespace Slender.Tests.Ringer;

public class RingerInstallerTests
{
    private const string SourceExt = @"C:\app\EdgeRinger\extension";
    private const string SourceHost = @"C:\app\Slender.RingerHost.exe";
    private const string Root = @"C:\data\edge-ringer";
    private const string Key = "AAECAwQFBgcICQoLDA0ODw==";
    private const string Template = "{\"key\":\"" + Key + "\",\"content_scripts\":[{\"matches\":[\"__ORIGIN__/*\"]}]}";

    private sealed class FakeRingerRegistry : IRingerRegistry
    {
        public string? Value;
        public int Writes, Deletes;
        public string? ReadHostManifestPath() => Value;
        public void WriteHostManifestPath(string path) { Value = path; Writes++; }
        public void DeleteHostKey() { Value = null; Deletes++; }
    }

    private sealed class FakeRingerFiles : IRingerFiles
    {
        public Dictionary<string, string> Texts { get; } = [];
        public Dictionary<string, AddinFileStamp> Stamps { get; } = [];
        public HashSet<string> Locked { get; } = [];
        public List<string> Replaced { get; } = [];

        public bool Exists(string path) => Texts.ContainsKey(path) || Stamps.ContainsKey(path);
        public string? ReadAllText(string path) => Texts.GetValueOrDefault(path);
        public void WriteAllText(string path, string text) { Texts[path] = text; Stamps[path] = new(text.Length, 1); }

        public void Copy(string source, string destination)
        {
            if (Locked.Contains(destination)) throw new IOException("in use");
            Texts[destination] = Texts.GetValueOrDefault(source) ?? "binary:" + source;
            Stamps[destination] = Stamps.GetValueOrDefault(source, new(1, 1));
        }

        public void ReplaceAtomically(string source, string destination)
        {
            if (Locked.Contains(destination)) throw new IOException("in use");
            Replaced.Add(destination);
            Stamps[destination] = Stamps[source];
        }

        public IReadOnlyList<string> ListFiles(string directory) =>
            [.. Texts.Keys.Concat(Stamps.Keys).Distinct().Where(p => Path.GetDirectoryName(p) == directory).Select(p => Path.GetFileName(p))];

        public void DeleteDirectory(string directory)
        {
            foreach (string p in Texts.Keys.Concat(Stamps.Keys).Where(p => p.StartsWith(directory + "\\", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                if (Locked.Contains(p)) throw new IOException("in use");
                Texts.Remove(p);
                Stamps.Remove(p);
            }
        }

        public AddinFileStamp? Stamp(string path) => Stamps.TryGetValue(path, out var s) ? s : null;
    }

    private sealed class Rig
    {
        public FakeRingerRegistry Registry = new();
        public FakeRingerFiles Files = new();
        public RingerInstaller Installer;

        public Rig(bool shipped = true)
        {
            if (shipped)
            {
                Files.Texts[SourceExt + @"\manifest.json"] = Template;
                Files.Texts[SourceExt + @"\main.js"] = "main";
                Files.Texts[SourceExt + @"\relay.js"] = "relay";
                Files.Texts[SourceExt + @"\background.js"] = "background";
                Files.Stamps[SourceHost] = new(100, 5);
            }
            Installer = new RingerInstaller(Registry, Files, SourceExt, SourceHost, Root);
        }

        public string HostManifestPath => Root + @"\com.slender.ringer.json";
    }

    [Fact]
    public void Set_up_writes_the_extension_the_host_the_manifest_and_the_registry_value()
    {
        var r = new Rig();
        var result = r.Installer.SetUp("https://pbx.example.com/");

        Assert.Equal(RingerInstallStatus.Done, result.Status);
        // Extension copy: origin filled in everywhere, other files copied as they are.
        string manifest = r.Files.Texts[Root + @"\extension\manifest.json"];
        Assert.DoesNotContain("__ORIGIN__", manifest);
        Assert.Contains("https://pbx.example.com/*", manifest);
        Assert.Equal("main", r.Files.Texts[Root + @"\extension\main.js"]);
        Assert.True(r.Files.Exists(Root + @"\extension\relay.js"));
        Assert.True(r.Files.Exists(Root + @"\extension\background.js"));
        // Host copy beside the manifest, not in the Slender install folder.
        Assert.True(r.Files.Exists(Root + @"\Slender.RingerHost.exe"));

        using var doc = JsonDocument.Parse(r.Files.Texts[r.HostManifestPath]);
        var host = doc.RootElement;
        Assert.Equal("com.slender.ringer", host.GetProperty("name").GetString());
        Assert.Equal(Root + @"\Slender.RingerHost.exe", host.GetProperty("path").GetString());
        Assert.Equal("stdio", host.GetProperty("type").GetString());
        string id = RingerInstaller.ExtensionIdFromManifest(Template);
        Assert.Equal([$"chrome-extension://{id}/"], host.GetProperty("allowed_origins").EnumerateArray().Select(o => o.GetString()));

        Assert.Equal(r.HostManifestPath, r.Registry.Value);
        Assert.True(r.Installer.IsSetUp());
    }

    [Fact]
    public void Remove_deletes_the_key_and_the_folder_when_nothing_was_there_before()
    {
        var r = new Rig();
        r.Installer.SetUp("https://pbx.example.com");
        var result = r.Installer.Remove();

        Assert.Equal(RingerInstallStatus.Removed, result.Status);
        Assert.Null(r.Registry.Value);
        Assert.Equal(1, r.Registry.Deletes);
        Assert.DoesNotContain(r.Files.Texts.Keys.Concat(r.Files.Stamps.Keys), p => p.StartsWith(Root, StringComparison.OrdinalIgnoreCase));
        Assert.True(r.Files.Exists(SourceHost)); // the shipped files are never touched
        Assert.False(r.Installer.IsSetUp());
    }

    [Fact]
    public void Set_up_keeps_the_previous_registry_value_and_remove_puts_it_back()
    {
        var r = new Rig();
        r.Registry.Value = @"C:\other\host.json";
        r.Installer.SetUp("https://pbx.example.com");
        Assert.Equal(r.HostManifestPath, r.Registry.Value);

        r.Installer.SetUp("https://pbx.example.com:8443"); // again: must not record our own value as the previous one
        Assert.Contains("https://pbx.example.com:8443/*", r.Files.Texts[Root + @"\extension\manifest.json"]);

        r.Installer.Remove();
        Assert.Equal(@"C:\other\host.json", r.Registry.Value);
        Assert.Equal(0, r.Registry.Deletes);
    }

    [Fact]
    public void Remove_leaves_the_key_alone_when_another_program_has_taken_it_since()
    {
        var r = new Rig();
        r.Installer.SetUp("https://pbx.example.com");
        r.Registry.Value = @"C:\someone\else.json";
        r.Installer.Remove();
        Assert.Equal(@"C:\someone\else.json", r.Registry.Value);
        Assert.Equal(0, r.Registry.Deletes);
    }

    [Fact]
    public void Remove_when_never_set_up_does_nothing()
    {
        var r = new Rig();
        r.Registry.Value = @"C:\other\host.json";
        Assert.Equal(RingerInstallStatus.NotSetUp, r.Installer.Remove().Status);
        Assert.Equal(@"C:\other\host.json", r.Registry.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://pbx.example.com")]
    [InlineData("https://pbx.example.com/path")]
    [InlineData("pbx.example.com")]
    public void An_invalid_address_is_rejected_and_nothing_is_written(string? address)
    {
        var r = new Rig();
        var before = r.Files.Texts.Count;
        Assert.Equal(RingerInstallStatus.InvalidOrigin, r.Installer.SetUp(address).Status);
        Assert.Equal(0, r.Registry.Writes);
        Assert.Equal(before, r.Files.Texts.Count);
    }

    [Fact]
    public void A_build_without_the_shipped_files_cannot_set_up()
    {
        var r = new Rig(shipped: false);
        Assert.False(r.Installer.IsAvailable());
        Assert.Equal(RingerInstallStatus.SourceMissing, r.Installer.SetUp("https://pbx.example.com").Status);
        Assert.Equal(0, r.Registry.Writes);
    }

    [Fact]
    public void A_locked_host_copy_fails_before_the_registry_is_written()
    {
        var r = new Rig();
        r.Files.Locked.Add(Root + @"\Slender.RingerHost.exe");
        r.Files.Stamps[Root + @"\Slender.RingerHost.exe"] = new(1, 1);
        Assert.Equal(RingerInstallStatus.Failed, r.Installer.SetUp("https://pbx.example.com").Status);
        Assert.Equal(0, r.Registry.Writes);
    }

    [Fact]
    public void The_host_copy_is_refreshed_only_when_set_up_and_different()
    {
        var r = new Rig();
        Assert.Equal(RingerRefreshStatus.NotSetUp, r.Installer.RefreshHostCopy());

        r.Installer.SetUp("https://pbx.example.com");
        Assert.Equal(RingerRefreshStatus.UpToDate, r.Installer.RefreshHostCopy());
        Assert.Empty(r.Files.Replaced);

        r.Files.Stamps[SourceHost] = new(120, 9); // an update shipped a new host
        Assert.Equal(RingerRefreshStatus.Refreshed, r.Installer.RefreshHostCopy());
        Assert.Equal([Root + @"\Slender.RingerHost.exe"], r.Files.Replaced);
        Assert.Equal(RingerRefreshStatus.UpToDate, r.Installer.RefreshHostCopy());

        r.Files.Stamps[SourceHost] = new(130, 10);
        r.Files.Locked.Add(Root + @"\Slender.RingerHost.exe");
        Assert.Equal(RingerRefreshStatus.Deferred, r.Installer.RefreshHostCopy());
        Assert.Equal(1, r.Registry.Writes); // never touches the registry
    }
}
