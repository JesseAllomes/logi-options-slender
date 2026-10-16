using Slender.Hidpp;
using Slender.Hidpp.Transport;

namespace Slender.Tests.Hidpp;

public class LogiKeyboardTests
{
    private const byte FnIndex = 12;

    private static FakeDevice K860(Action<FakeDevice>? setup = null)
    {
        var dev = new FakeDevice();
        dev.Features.Clear();
        dev.Features[FeatureId.ReprogControlsV4] = FakeDevice.Reprog;
        dev.Features[FeatureId.Battery] = FakeDevice.Battery;
        dev.Features[KeyboardFeatureId.FnInversionMultiHost] = FnIndex;
        dev.AcceptedIndices.Clear();
        dev.AcceptedIndices.Add(0x01); // the K860 answers on 0x01
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
        dev.Handlers[(FnIndex, 1)] = _ => [];
        dev.Handlers[(FakeDevice.Battery, 0)] = _ => [87, 80, 0];
        setup?.Invoke(dev);
        return dev;
    }

    private static LogiKeyboardOptions Fast() => new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(150),
        MaxRetries = 1,
        RetryDelayBase = TimeSpan.FromMilliseconds(1),
        ReadPollMs = 20,
        ProbeTimeout = TimeSpan.FromMilliseconds(60),
        BackoffInitial = TimeSpan.FromMilliseconds(20),
        BackoffMax = TimeSpan.FromMilliseconds(80),
        HeartbeatInterval = TimeSpan.FromMilliseconds(30),
        AsleepReconnectAfter = TimeSpan.FromSeconds(60),
        DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
    };

    private static (LogiKeyboard Kb, FakeTransport T, FakeFactory F) Rig(FakeDevice dev, params FakeTransport[] more)
    {
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        foreach (var m in more) f.Enqueue(m);
        return (new LogiKeyboard(f, Fast()), t, f);
    }

    private static bool Divert(FakeTransport t, ushort cid, byte flags) => t.Writes.Any(w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3
        && w[4] == (byte)(cid >> 8) && w[5] == (byte)cid && w[6] == flags);

    private static bool FnWrite(FakeTransport t, byte state) => t.Writes.Any(w => w[2] == FnIndex && w[3] >> 4 == 1
        && w[4] == LogiKeyboard.CurrentHost && w[5] == state);

    [Fact]
    public async Task Connects_on_index_01_resets_leftover_diverts_to_native()
    {
        var (kb, t, _) = Rig(K860());
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);

            Assert.Equal("ERGO K860", kb.Name);
            foreach (var cid in LogiKeyboard.DivertableKeys) Assert.True(Divert(t, cid, 0x02), $"cid {cid}");
            Assert.DoesNotContain(t.Writes, w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3 && w[6] != 0x02);
        }
    }

    [Fact]
    public async Task Divert_uses_temporary_flag_only_and_reapplies_after_reconnect_but_fn_lock_is_written_once()
    {
        var dev = K860();
        var t2 = dev.NewTransport();
        var (kb, t1, f) = Rig(dev, t2);
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            await kb.SetDivertedKeysAsync([KeyboardId.TaskView, KeyboardId.Mute], CancellationToken.None);
            await kb.SetFnLockAsync(true, CancellationToken.None);

            Assert.True(Divert(t1, KeyboardId.TaskView, 0x03));
            Assert.True(Divert(t1, KeyboardId.Mute, 0x03));
            Assert.True(FnWrite(t1, 1));
            Assert.DoesNotContain(t1.Writes, w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3 && (w[6] & 0x04) != 0);

            t1.Fault();
            await Wait.UntilAsync(() => f.OpenCalls >= 2 && Divert(t2, KeyboardId.Mute, 0x03) && kb.IsConnected);
            await Task.Delay(50);
            Assert.True(Divert(t2, KeyboardId.TaskView, 0x03));
            Assert.True(Divert(t2, KeyboardId.Lock, 0x02));   // not wanted: back to native
            Assert.False(Divert(t2, KeyboardId.Mute, 0x02));  // wanted: not reset
            Assert.False(FnWrite(t2, 1));                      // already written: a reconnect leaves the keyboard's Fn lock alone
        }
    }

    [Fact]
    public async Task Fn_lock_set_before_connect_is_written_on_connect()
    {
        var (kb, t, _) = Rig(K860());
        using (kb)
        {
            await kb.SetFnLockAsync(false, CancellationToken.None); // offline: remembered, no write
            Assert.Empty(t.Writes);
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => FnWrite(t, 0));
        }
    }

    [Fact]
    public async Task Missing_fn_feature_is_tolerated()
    {
        var (kb, t, _) = Rig(K860(d => d.Features.Remove(KeyboardFeatureId.FnInversionMultiHost)));
        using (kb)
        {
            await kb.SetFnLockAsync(true, CancellationToken.None);
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            Assert.DoesNotContain(t.Writes, w => w[2] == FnIndex);
        }
    }

    [Fact]
    public async Task Key_events_are_raised_and_battery_read_via_0x1000()
    {
        var (kb, t, _) = Rig(K860());
        using (kb)
        {
            var events = new List<ButtonEvent>();
            kb.KeyChanged += (_, e) => { lock (events) events.Add(e); };
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);

            t.Inject(FakeDevice.Frame(0x01, FakeDevice.Reprog, 0, 0, 0x00, 0xE9, 0, 0));
            t.Inject(FakeDevice.Frame(0x01, FakeDevice.Reprog, 0, 0, 0, 0, 0, 0));
            await Wait.UntilAsync(() => { lock (events) return events.Count == 2; });
            lock (events)
            {
                Assert.Equal(new ButtonEvent(KeyboardId.VolumeUp, true), events[0]);
                Assert.Equal(new ButtonEvent(KeyboardId.VolumeUp, false), events[1]);
            }

            Assert.Equal(new BatteryStatus(87, false), await kb.GetBatteryAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Battery_is_null_when_no_battery_feature()
    {
        var (kb, _, _) = Rig(K860(d => d.Features.Remove(FeatureId.Battery)));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            Assert.Null(await kb.GetBatteryAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Dispose_returns_applied_diverts_to_native()
    {
        var (kb, t, _) = Rig(K860());
        await kb.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => kb.IsConnected);
        await kb.SetDivertedKeysAsync([KeyboardId.Calculator], CancellationToken.None);
        int Natives() => t.Writes.Count(w => w[2] == FakeDevice.Reprog && w[5] == (byte)KeyboardId.Calculator && w[6] == 0x02);
        int before = Natives(); // the connect-time reset
        kb.Dispose();
        Assert.Equal(before + 1, Natives());
        Assert.True(t.Disposed);
    }

    // ---- allowlist + transport selection ----

    [Fact]
    public void Allowlist_permits_only_fn_inversion_get_and_set()
    {
        Assert.True(HidppAllowlist.IsAllowed(0x40A3, 0));
        Assert.True(HidppAllowlist.IsAllowed(0x40A3, 1));
        Assert.False(HidppAllowlist.IsAllowed(0x40A3, 2));
        Assert.False(HidppAllowlist.IsAllowed(0x40A0, 0));
        Assert.False(HidppAllowlist.IsAllowed(0x40A2, 0));
    }

    private static HidCollectionInfo C(ushort pid, ushort up, ushort u) => new("path", 0x046D, pid, up, u, 20, 20, 0);

    [Fact]
    public void Transport_selection_is_by_pid()
    {
        var all = new[]
        {
            C(0xB359, 0x0001, 0x0006), C(0xB359, 0xFF43, 0x0202), C(0xB042, 0xFF43, 0x0202), C(0xB359, 0x000C, 0x0001),
        };
        var kb = WindowsHidTransportFactory.SelectCandidates(all, WindowsHidTransportFactory.ErgoK860ProductIds).ToList();
        Assert.Single(kb);
        Assert.Equal(0xB359, kb[0].ProductId);
        Assert.Equal(0xFF43, kb[0].UsagePage);

        var mx = WindowsHidTransportFactory.SelectCandidates(all).ToList(); // default unchanged
        Assert.Single(mx);
        Assert.Equal(0xB042, mx[0].ProductId);
    }
}
