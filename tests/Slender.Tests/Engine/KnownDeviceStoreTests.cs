using Slender.Engine.Devices;

namespace Slender.Tests.Engine;

public sealed class KnownDeviceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-kds-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MissingFileReadsEmpty()
    {
        Assert.Empty(KnownDeviceStore.Read(_dir));
        Assert.Empty(new KnownDeviceStore(_dir).Load());
    }

    [Fact]
    public void RoundTripsAndKeepsFirstSeen()
    {
        var t0 = new DateTimeOffset(2026, 10, 22, 1, 2, 3, TimeSpan.Zero);
        var store = new KnownDeviceStore(_dir);
        store.Upsert("mx-master-4", t0, t0);
        store.Upsert("mx-master-4", t0.AddDays(5), t0.AddHours(1));

        var read = KnownDeviceStore.Read(_dir);
        Assert.Equal((t0, t0.AddHours(1)), read["mx-master-4"]);
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void DamagedLinesAreSkipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(Path.Combine(_dir, KnownDeviceStore.FileName),
        [
            "good\t100\t200",
            "BadCase\t1\t2",
            "two\tfields",
            "neg\t-1\t2",
            "text\tabc\t2",
            "good\t5\t6",
            new string('a', 65) + "\t1\t2",
            "ok-2\t300\t400",
        ]);

        var read = KnownDeviceStore.Read(_dir);
        Assert.Equal(["good", "ok-2"], read.Keys.Order());
        Assert.Equal(100, read["good"].First.UtcTicks);
    }

    [Fact]
    public void RemoveDeletesAndReportsWhetherItExisted()
    {
        var t = DateTimeOffset.UtcNow;
        var store = new KnownDeviceStore(_dir);
        store.Upsert("a", t, t);
        Assert.True(store.Remove("a"));
        Assert.False(store.Remove("a"));
        Assert.Empty(KnownDeviceStore.Read(_dir));
    }

    [Fact]
    public void InvalidIdIsIgnored()
    {
        var store = new KnownDeviceStore(_dir);
        store.Upsert("Bad Id", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.Empty(KnownDeviceStore.Read(_dir));
    }

    [Fact]
    public void IoErrorIsSwallowed()
    {
        Directory.CreateDirectory(_dir);
        // A directory squatting on the file name makes the final move fail.
        Directory.CreateDirectory(Path.Combine(_dir, KnownDeviceStore.FileName));
        var store = new KnownDeviceStore(_dir);
        store.Upsert("a", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
}
