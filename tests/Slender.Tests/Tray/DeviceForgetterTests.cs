using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class DeviceForgetterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));
    private readonly string _config;
    private int _seconds;

    public DeviceForgetterTests()
    {
        Directory.CreateDirectory(_dir);
        _config = Path.Combine(_dir, "config.json");
        File.WriteAllText(_config, "{\"original\":true}");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private DeviceForgetter Create(bool forgets, List<SlenderConfig> applied, SlenderConfig? current = null) => new(
        _ => forgets, _config, () => current ?? SlenderConfig.Default, c => { applied.Add(c); return true; }, NullLog.Instance,
        () => new DateTime(2026, 10, 22, 9, 0, 0).AddSeconds(_seconds++));

    private string[] Backups(string id = "mx-master-4") => Directory.GetFiles(_dir, $"config.before-remove-{id}-*.json");

    [Fact]
    public void Backs_up_then_resets()
    {
        var applied = new List<SlenderConfig>();
        var cfg = SlenderConfig.Default with { Pointer = new PointerSettings(Dpi: 1600) };

        Create(true, applied, cfg).Forget(0);

        string backup = Assert.Single(Backups());
        Assert.EndsWith("config.before-remove-mx-master-4-20261022-090000.json", backup);
        Assert.Equal("{\"original\":true}", File.ReadAllText(backup));
        Assert.Null(Assert.Single(applied).Pointer.Dpi);
    }

    [Fact]
    public void Keyboard_index_uses_its_own_backup_name()
    {
        Create(true, []).Forget(1);

        Assert.Single(Backups("ergo-k860"));
    }

    [Fact]
    public void Nothing_happens_when_the_registry_refuses()
    {
        var applied = new List<SlenderConfig>();

        Create(false, applied).Forget(0);

        Assert.Empty(applied);
        Assert.Empty(Backups());
    }

    [Fact]
    public void Out_of_range_index_is_ignored()
    {
        var applied = new List<SlenderConfig>();
        var f = Create(true, applied);

        f.Forget(-1);
        f.Forget(DeviceCatalog.All.Count);

        Assert.Empty(applied);
        Assert.Empty(Backups());
    }

    [Fact]
    public void No_reset_when_the_backup_fails()
    {
        var applied = new List<SlenderConfig>();
        // A directory sits where the backup file must go, so the copy cannot be made.
        Directory.CreateDirectory(Path.Combine(_dir, "config.before-remove-mx-master-4-20261022-090000.json"));

        Create(true, applied).Forget(0);

        Assert.Empty(applied);
    }

    [Fact]
    public void Keeps_the_newest_five_backups_per_device()
    {
        var f = Create(true, []);
        f.Forget(1);

        for (int i = 0; i < 7; i++) f.Forget(0);

        string[] names = [.. Backups().Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal)];
        Assert.Equal(5, names.Length);
        Assert.Contains("20261022-090003", names[0]); // the first two mouse backups were deleted
        Assert.Single(Backups("ergo-k860"));
    }
}
