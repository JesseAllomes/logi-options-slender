using Slender.Engine;
using Slender.Engine.Devices;
using Slender.GalaxyBuds.Windows;
using Slender.Tray;

namespace Slender.Tests.GalaxyBuds;

public sealed class BudsPresenceAndTrayTests
{
    private sealed class Source(params HidDeviceKey[] keys) : IDevicePresenceSource
    {
        public bool Started, Disposed, FailStart;
        public event EventHandler? Changed;
        public void Start() { if (FailStart) throw new InvalidOperationException("no"); Started = true; }
        public IReadOnlySet<HidDeviceKey> Snapshot() => new HashSet<HidDeviceKey>(keys);
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
    public void SettingsOpensAtTheBudsPage()
    {
        var info = SettingsLauncher.StartInfo(@"C:\x\Slender.Settings.exe", popover: false, DeviceCatalog.GalaxyBuds4Pro);
        Assert.Equal(["--device", "galaxy-buds4-pro"], info.ArgumentList);
    }
}
