using Slender.Engine;
using Slender.Engine.Devices;
using Slender.GalaxyBuds.Windows;
using Slender.Tray;

namespace Slender.Tests.GalaxyBuds;

public sealed class BudsPresenceAndTrayTests
{
    private sealed class Source(params HidDeviceKey[] keys) : IDevicePresenceSource
    {
        public bool Started, Disposed, FailStart, FailSnapshot;
        public event EventHandler? Changed;
        public void Start() { if (FailStart) throw new InvalidOperationException("no"); Started = true; }
        public IReadOnlySet<HidDeviceKey> Snapshot() =>
            FailSnapshot ? throw new InvalidOperationException("no") : new HashSet<HidDeviceKey>(keys);
        public void Dispose() => Disposed = true;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void CompositeMergesSnapshotsForwardsChangesAndStartsAll()
    {
        var hid = new Source(new HidDeviceKey(0x6993, 0xB0AE)) { FailStart = true };
        var bt = new Source(new HidDeviceKey(0x0075, 0xA013));
        using var c = new CompositeDevicePresence(null, hid, bt);
        int changed = 0;
        c.Changed += (_, _) => changed++;
        c.Start();
        Assert.True(bt.Started); // a source that fails to start does not stop the others
        Assert.Equal(2, c.Snapshot().Count);
        Assert.Contains(new HidDeviceKey(0x0075, 0xA013), c.Snapshot());
        bt.Raise();
        hid.Raise();
        Assert.Equal(2, changed);
        c.Dispose();
        Assert.True(hid.Disposed && bt.Disposed);
        bt.Raise();
        Assert.Equal(2, changed);
    }

    [Fact]
    public void BluetoothIdsGiveKeyAndAddress()
    {
        const string id = @"BTHENUM\{0000110C-0000-1000-8000-00805F9B34FB}_VID&00010075_PID&A013\7&ABC123&0&AABBCCDDEEFF_C00000000";
        Assert.Equal(new HidDeviceKey(0x0075, 0xA013), BluetoothClassicPresence.ParseKey(id));
        Assert.Equal(0xAABBCCDDEEFFUL, BluetoothClassicPresence.ParseAddress(id));
        Assert.True(DeviceCatalog.All.First(d => d.Id == DeviceCatalog.GalaxyBuds4Pro).Matches(BluetoothClassicPresence.ParseKey(id)!.Value));
        Assert.Null(BluetoothClassicPresence.ParseKey(@"BTHENUM\DEV_AABBCCDDEEFF\7&1&0&BLUETOOTHDEVICE_AABBCCDDEEFF"));
        Assert.Null(BluetoothClassicPresence.ParseKey(@"USB\VID_6993&PID_B0AE\1"));
    }

    [Fact]
    public void TrayBatteryLineAndLowestBud()
    {
        var b = new BudsState(true, 100, 98, 95, PlacementLeft: "in case", PlacementRight: "in case");
        Assert.Equal("L 100% · R 98% · Case 95%", TrayText.BudsBattery(b));
        Assert.Equal(98, TrayText.BudsLowest(b));
        Assert.Equal(100, TrayText.BudsLowest(b with { PlacementRight = "disconnected" }));
        Assert.Null(TrayText.BudsBattery(new BudsState(true)));
        Assert.Null(TrayText.BudsLowest(null));
    }

    [Fact]
    public void TrayLowestLeavesOutADisconnectedLeftBud()
    {
        var b = new BudsState(true, 40, 98, 95, PlacementLeft: "disconnected", PlacementRight: "in case");
        Assert.Equal(98, TrayText.BudsLowest(b));
        Assert.Null(TrayText.BudsLowest(b with { PlacementRight = "disconnected" }));
    }

    [Fact]
    public void SettingsOpensAtTheBudsPage()
    {
        var info = SettingsLauncher.StartInfo(@"C:\x\Slender.Settings.exe", popover: false, DeviceCatalog.GalaxyBuds4Pro);
        Assert.Equal(["--device", "galaxy-buds4-pro"], info.ArgumentList);
    }

    [Fact]
    public void CompositeSkipsASourceWhoseSnapshotFails()
    {
        var keyB = new HidDeviceKey(0x0075, 0xA013);
        var a = new Source(new HidDeviceKey(0x6993, 0xB0AE)) { FailSnapshot = true };
        var b = new Source(keyB);
        using var c = new CompositeDevicePresence(null, a, b);
        var snap = c.Snapshot();
        Assert.Single(snap);
        Assert.Contains(keyB, snap);
    }

    [Fact]
    public void MalformedBluetoothIdsGiveNoAddress()
    {
        Assert.Null(BluetoothClassicPresence.ParseAddress("NOSLASH&AABBCCDDEEFF_C00000000"));
        Assert.Null(BluetoothClassicPresence.ParseAddress(@"BTHENUM\DEV&1\NOAMPERSANDHERE"));
        Assert.Null(BluetoothClassicPresence.ParseAddress(@"BTHENUM\X\7&0&AABB"));
        Assert.Null(BluetoothClassicPresence.ParseAddress(@"BTHENUM\X\7&0&ZZZZZZZZZZZZ_C00000000"));
        Assert.Null(BluetoothClassicPresence.ParseAddress(@"BTHENUM\X\7&0&000000000000_C00000000"));
    }

    private sealed class Tracked : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void ConnectHintDisposesWhatItMadeWhenTheSecondWatcherFails()
    {
        var first = new Tracked();
        int calls = 0;
        var result = BudsConnectHint.Create(() => { }, (_, _) =>
        {
            if (++calls == 2) throw new InvalidOperationException("no");
            return first;
        });
        Assert.Null(result);
        Assert.True(first.Disposed);

        var a = new Tracked();
        var b = new Tracked();
        var made = new Queue<Tracked>([a, b]);
        var ok = BudsConnectHint.Create(() => { }, (_, _) => made.Dequeue());
        Assert.NotNull(ok);
        Assert.False(a.Disposed || b.Disposed);
        ok.Dispose();
        Assert.True(a.Disposed && b.Disposed);
    }
}
