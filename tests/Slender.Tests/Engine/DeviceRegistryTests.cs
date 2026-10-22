using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.Tests.Engine;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    public readonly List<FakeTimer> Timers = [];

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new FakeTimer(this, callback, state) { Due = _now + dueTime };
        Timers.Add(t);
        return t;
    }

    public int ArmedTimers => Timers.Count(t => !t.Disposed && t.Due is not null);

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var t in Timers.ToArray())
            if (!t.Disposed && t.Due is { } d && d <= _now)
            {
                t.Due = null;
                t.Callback(t.State);
            }
    }

    internal sealed class FakeTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public readonly TimerCallback Callback = callback;
        public readonly object? State = state;
        public DateTimeOffset? Due;
        public bool Disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = owner._now + dueTime;
            return true;
        }

        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}

internal sealed class FakePresence : IDevicePresenceSource
{
    private readonly HashSet<HidDeviceKey> _keys = [];
    public int Starts;
    public bool Disposed;
    public event EventHandler? Changed;

    public void Start() => Starts++;
    public IReadOnlySet<HidDeviceKey> Snapshot() { lock (_keys) return new HashSet<HidDeviceKey>(_keys); }
    public void Dispose() => Disposed = true;

    public void Set(params HidDeviceKey[] keys)
    {
        lock (_keys) { _keys.Clear(); _keys.UnionWith(keys); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetQuiet(params HidDeviceKey[] keys)
    {
        lock (_keys) { _keys.Clear(); _keys.UnionWith(keys); }
    }
}

internal sealed class FakeModule(string id) : IDeviceModule
{
    public string DeviceId => id;
    public int Starts, Stops, Wakes;
    public Exception? ThrowOnStart, ThrowOnWake;
    public Action? OnStart;

    public void Start() { Starts++; OnStart?.Invoke(); if (ThrowOnStart is { } e) throw e; }
    public void Stop() => Stops++;
    public void Wake() { Wakes++; if (ThrowOnWake is { } e) throw e; }
}

internal sealed class RecordingLog : ILog
{
    public List<string> Errors { get; } = [];
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { lock (Errors) Errors.Add(message); }
}

public sealed class DeviceRegistryTests : IDisposable
{
    private static readonly SupportedDevice Usb = new("usb", "USB", 0x1, [1], ReportsLink: false);
    private static readonly SupportedDevice Ble = new("ble", "BLE", 0x1, [2], ReportsLink: true);
    private static readonly HidDeviceKey UsbKey = new(0x1, 1);
    private static readonly HidDeviceKey BleKey = new(0x1, 2);
    private static readonly TimeSpan Grace = DeviceRegistry.SleepGrace;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-reg-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly FakePresence _presence = new();
    private readonly RecordingLog _log = new();
    private readonly FakeModule _usbMod = new("usb");
    private readonly FakeModule _bleMod = new("ble");
    private readonly List<DeviceRegistry> _made = [];

    public void Dispose()
    {
        foreach (var r in _made) r.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private DeviceRegistry Make(bool start = true)
    {
        var r = new DeviceRegistry([Usb, Ble], _presence, new KnownDeviceStore(_dir), _log, _time);
        _made.Add(r);
        r.Register(_usbMod);
        r.Register(_bleMod);
        if (start) r.Start();
        return r;
    }

    private static DevicePresence P(DeviceRegistry r, string id) => r.Devices.Single(d => d.Id == id).Presence;

    [Fact]
    public void HiddenUntilFirstAlive_ThenKnown()
    {
        var r = Make();
        Assert.Equal([Usb.Id, Ble.Id], r.Devices.Select(d => d.Id));
        Assert.Equal(DevicePresence.Hidden, P(r, "usb"));
        Assert.Empty(KnownDeviceStore.Read(_dir));

        _presence.Set(UsbKey);
        Assert.Equal(DevicePresence.Active, P(r, "usb"));
        Assert.True(KnownDeviceStore.Read(_dir).ContainsKey("usb"));
        Assert.Equal(DevicePresence.Hidden, P(r, "ble"));
    }

    [Fact]
    public void BleListedButLinkNeverUp_StaysHiddenYetModuleRuns()
    {
        var r = Make();
        _presence.Set(BleKey);
        Assert.Equal(DevicePresence.Hidden, P(r, "ble"));
        Assert.Equal(1, _bleMod.Starts);
        r.ReportLink("ble", true);
        Assert.Equal(DevicePresence.Active, P(r, "ble"));
        Assert.Equal(1, _bleMod.Starts);
    }

    [Fact]
    public void UsbUnlistedSleepsThenDisconnectsAfterGrace()
    {
        var r = Make();
        _presence.Set(UsbKey);
        _presence.Set();
        Assert.Equal(DevicePresence.Sleeping, P(r, "usb"));
        Assert.Equal(0, _usbMod.Stops);
        Assert.Equal(1, _time.ArmedTimers);

        _time.Advance(Grace - TimeSpan.FromSeconds(1));
        Assert.Equal(DevicePresence.Sleeping, P(r, "usb"));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(DevicePresence.Disconnected, P(r, "usb"));
        Assert.Equal(1, _usbMod.Starts);
        Assert.Equal(1, _usbMod.Stops);
        Assert.Equal(0, _time.ArmedTimers);
    }

    [Fact]
    public void BleLinkDownSleepsThenDisconnects_LinkUpReturnsToActive()
    {
        var r = Make();
        _presence.Set(BleKey);
        r.ReportLink("ble", true);
        r.ReportLink("ble", false);
        Assert.Equal(DevicePresence.Sleeping, P(r, "ble"));
        Assert.Equal(1, _bleMod.Starts); // still listed, module keeps running
        r.ReportLink("ble", true);
        Assert.Equal(DevicePresence.Active, P(r, "ble"));
        Assert.Equal(0, _time.ArmedTimers);

        r.ReportLink("ble", false);
        _time.Advance(Grace);
        Assert.Equal(DevicePresence.Disconnected, P(r, "ble"));
        Assert.Equal(0, _bleMod.Stops); // interfaces still listed, so the module keeps trying
        _presence.Set();
        Assert.Equal(1, _bleMod.Stops);
    }

    [Fact]
    public void ReportDisconnected_IsImmediateAndClearedByNextLinkUp()
    {
        var r = Make();
        _presence.Set(BleKey);
        r.ReportLink("ble", true);
        r.ReportDisconnected("ble");
        Assert.Equal(DevicePresence.Disconnected, P(r, "ble"));
        Assert.Equal(0, _time.ArmedTimers);
        r.ReportLink("ble", true);
        Assert.Equal(DevicePresence.Active, P(r, "ble"));
    }

    [Fact]
    public void ModuleStartStopOncePerTransition()
    {
        var r = Make();
        _presence.Set(UsbKey);
        _presence.Set(UsbKey, BleKey); // unrelated change
        _presence.Set(UsbKey);
        Assert.Equal((1, 0), (_usbMod.Starts, _usbMod.Stops));
        _presence.Set();
        _time.Advance(Grace);
        Assert.Equal((1, 1), (_usbMod.Starts, _usbMod.Stops));
        _presence.Set(UsbKey);
        Assert.Equal((2, 1), (_usbMod.Starts, _usbMod.Stops));
        Assert.Equal(DevicePresence.Active, P(r, "usb"));
    }

    [Fact]
    public void WakeOnRearrival_AfterStartIfJustStarted()
    {
        Make();
        _presence.Set(UsbKey);
        Assert.Equal((1, 1), (_usbMod.Starts, _usbMod.Wakes));
        _presence.Set();
        _presence.Set(UsbKey);
        Assert.Equal((1, 2), (_usbMod.Starts, _usbMod.Wakes)); // still sleeping, so already running
        _presence.Set(UsbKey, BleKey);
        Assert.Equal(2, _usbMod.Wakes);
    }

    [Fact]
    public void FaultFromStartDisablesOnlyThatModule()
    {
        var r = Make();
        _usbMod.ThrowOnStart = new InvalidOperationException("boom");
        _presence.Set(UsbKey, BleKey);
        r.ReportLink("ble", true);

        Assert.True(r.Devices.Single(d => d.Id == "usb").Faulted);
        Assert.Equal(DevicePresence.Active, P(r, "usb")); // presence still computed
        Assert.Equal(1, _usbMod.Stops);
        Assert.Single(_log.Errors);
        Assert.False(r.Devices.Single(d => d.Id == "ble").Faulted);
        Assert.Equal(DevicePresence.Active, P(r, "ble"));

        _presence.Set(BleKey);
        _presence.Set(UsbKey, BleKey);
        Assert.Equal(1, _usbMod.Starts); // never restarted
        Assert.Single(_log.Errors);
    }

    [Fact]
    public void ReportFaultStopsModuleOnce()
    {
        var r = Make();
        _presence.Set(UsbKey);
        r.ReportFault("usb", new Exception("x"));
        r.ReportFault("usb", new Exception("y"));
        Assert.Equal(1, _usbMod.Stops);
        Assert.Single(_log.Errors);
        Assert.True(r.Devices[0].Faulted);
    }

    [Fact]
    public void WakeFaultIsContained()
    {
        var r = Make();
        _presence.Set(UsbKey);
        _usbMod.ThrowOnWake = new Exception("w");
        _presence.Set();
        _presence.Set(UsbKey);
        Assert.True(r.Devices[0].Faulted);
    }

    [Fact]
    public async Task ReportLinkFromInsideStartDoesNotDeadlock()
    {
        var r = Make();
        _bleMod.OnStart = () => r.ReportLink("ble", true);
        var task = Task.Run(() => _presence.Set(BleKey));
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Equal(DevicePresence.Active, P(r, "ble"));
        Assert.Equal(1, _bleMod.Starts);
    }

    [Fact]
    public void ForgetOnlyWhenDisconnected()
    {
        var r = Make();
        _presence.Set(UsbKey);
        Assert.False(r.Forget("usb"));
        _presence.Set();
        Assert.False(r.Forget("usb"));
        _time.Advance(Grace);
        Assert.True(r.Forget("usb"));
        Assert.Equal(DevicePresence.Hidden, P(r, "usb"));
        Assert.Empty(KnownDeviceStore.Read(_dir));
        Assert.False(r.Forget("usb"));
        Assert.False(r.Forget("nope"));
    }

    [Fact]
    public void NoTimerWhenNothingSleeps()
    {
        var r = Make();
        Assert.Equal(0, _time.ArmedTimers);
        _presence.Set(UsbKey);
        Assert.Equal(0, _time.ArmedTimers);
        Assert.Empty(_time.Timers);
        _presence.Set();
        Assert.Equal(1, _time.ArmedTimers);
        _presence.Set(UsbKey);
        Assert.Equal(0, _time.ArmedTimers);
        Assert.Equal(DevicePresence.Active, P(r, "usb"));
    }

    [Fact]
    public void KnownDeviceFromStoreSleepsAfterRestartThenDisconnects()
    {
        var r1 = Make();
        _presence.Set(UsbKey);
        _time.Advance(TimeSpan.FromMinutes(10));
        r1.Dispose(); // persists last seen = now

        _time.Advance(TimeSpan.FromMinutes(5));
        _presence.SetQuiet();
        var r2 = new DeviceRegistry([Usb, Ble], _presence, new KnownDeviceStore(_dir), _log, _time);
        _made.Add(r2);
        r2.Start();
        Assert.Equal(DevicePresence.Sleeping, P(r2, "usb"));
        _time.Advance(Grace);
        Assert.Equal(DevicePresence.Disconnected, P(r2, "usb"));
    }

    [Fact]
    public void KnownListedDeviceAfterLongOff_SleepsAtStartInsteadOfFlashingDisconnected()
    {
        var r1 = Make();
        _presence.Set(BleKey);
        r1.ReportLink("ble", true);
        r1.Dispose();

        _time.Advance(TimeSpan.FromHours(12)); // computer off overnight; Windows still lists the Bluetooth interfaces
        var r2 = new DeviceRegistry([Usb, Ble], _presence, new KnownDeviceStore(_dir), _log, _time);
        _made.Add(r2);
        r2.Start();
        Assert.Equal(DevicePresence.Sleeping, P(r2, "ble"));
        r2.ReportLink("ble", true);
        Assert.Equal(DevicePresence.Active, P(r2, "ble"));
    }

    [Fact]
    public void ChangedRaisedOutsideLockOnChange()
    {
        var r = Make();
        int raised = 0;
        r.Changed += (_, _) => { raised++; _ = r.Devices; Assert.True(Task.Run(() => r.Forget("x")).Wait(TimeSpan.FromSeconds(5))); };
        _presence.Set(UsbKey);
        Assert.Equal(1, raised);
        _presence.Set(UsbKey);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void RegisterRules()
    {
        var r = Make(start: false);
        Assert.Throws<ArgumentException>(() => r.Register(new FakeModule("nope")));
        Assert.Throws<ArgumentException>(() => r.Register(new FakeModule("usb")));
        r.Start();
        Assert.Throws<InvalidOperationException>(() => r.Register(new FakeModule("usb")));
    }

    [Fact]
    public void DisposeStopsRunningModulesAndLeavesPresenceUndisposed()
    {
        var r = Make();
        _presence.Set(UsbKey);
        r.Dispose();
        Assert.Equal(1, _usbMod.Stops);
        Assert.False(_presence.Disposed);
        _presence.Set(); // no longer subscribed
        Assert.Equal(1, _usbMod.Stops);
    }

    [Fact]
    public void CatalogHasBothDevices()
    {
        Assert.Equal([DeviceCatalog.MxMaster4, DeviceCatalog.ErgoK860], DeviceCatalog.All.Select(d => d.Id));
        Assert.True(DeviceCatalog.All[0].Matches(new HidDeviceKey(0x046D, 0xB042)));
        Assert.False(DeviceCatalog.All[0].Matches(new HidDeviceKey(0x046D, 0xB359)));
    }
}
