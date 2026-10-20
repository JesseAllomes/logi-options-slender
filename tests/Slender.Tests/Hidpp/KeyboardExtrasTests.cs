using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class KeyboardExtrasTests
{
    private const byte FnIndex = 12, DisableIndex = 15;

    // Fake 0x40A3 state per host: replies [host, inverted, 1, 1] like the 2026-10-15 probe (host 0xFF = the current one).
    private static FakeDevice K860(bool[] fnState, int current, byte caps = 0x1F, Action<FakeDevice>? setup = null)
    {
        var dev = new FakeDevice();
        dev.Features.Clear();
        dev.Features[FeatureId.ReprogControlsV4] = FakeDevice.Reprog;
        dev.Features[FeatureId.Battery] = FakeDevice.Battery;
        dev.Features[KeyboardFeatureId.FnInversionMultiHost] = FnIndex;
        dev.Features[KeyboardFeatureId.DisableKeys] = DisableIndex;
        dev.AcceptedIndices.Clear();
        dev.AcceptedIndices.Add(0x01);
        var state = (bool[])fnState.Clone();
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
        dev.Handlers[(FakeDevice.Battery, 0)] = _ => [87, 80, 0];
        dev.Handlers[(FnIndex, 0)] = p => [p[0], (byte)(state[p[0] == 0xFF ? current : p[0]] ? 1 : 0), 1, 1];
        dev.Handlers[(FnIndex, 1)] = p => { state[p[0] == 0xFF ? current : p[0]] = p[1] != 0; return []; };
        byte mask = 0;
        dev.Handlers[(DisableIndex, 0)] = _ => [caps];
        dev.Handlers[(DisableIndex, 1)] = _ => [mask];
        dev.Handlers[(DisableIndex, 2)] = p => { mask = p[0]; return []; };
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

    private static (LogiKeyboard Kb, FakeTransport T) Rig(FakeDevice dev)
    {
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        return (new LogiKeyboard(f, Fast()), t);
    }

    private static bool Wrote(FakeTransport t, byte index, byte fn, params byte[] p) =>
        t.Writes.Any(w => w[2] == index && w[3] >> 4 == fn && p.Select((b, i) => w[4 + i] == b).All(x => x));

    private static readonly bool[] Off3 = [false, false, false];

    // ---- 0x4521 ----

    [Fact]
    public async Task Disabled_keys_are_written_with_fn2_and_read_back_with_fn1()
    {
        var (kb, t) = Rig(K860(Off3, 0));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            var back = await kb.SetDisabledKeysAsync(DisabledKeys.CapsLock | DisabledKeys.Windows, CancellationToken.None);
            Assert.Equal(DisabledKeys.CapsLock | DisabledKeys.Windows, back);
            Assert.True(Wrote(t, DisableIndex, 2, 0x11));
            Assert.True(Wrote(t, DisableIndex, 1)); // the read back
            Assert.Equal(DisabledKeys.None, await kb.SetDisabledKeysAsync(DisabledKeys.None, CancellationToken.None));
            Assert.True(Wrote(t, DisableIndex, 2, 0x00));
        }
    }

    [Fact]
    public async Task Disabled_keys_are_limited_to_the_capability_mask()
    {
        var (kb, t) = Rig(K860(Off3, 0, caps: 0x03));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            await kb.SetDisabledKeysAsync(DisabledKeys.All, CancellationToken.None);
            Assert.True(Wrote(t, DisableIndex, 2, 0x03));
        }
    }

    [Fact]
    public async Task Disabled_keys_set_before_connect_are_applied_on_connect_and_a_missing_feature_is_tolerated()
    {
        var (kb, t) = Rig(K860(Off3, 0));
        using (kb)
        {
            Assert.Null(await kb.SetDisabledKeysAsync(DisabledKeys.Insert, CancellationToken.None)); // offline: remembered
            Assert.Empty(t.Writes);
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => Wrote(t, DisableIndex, 2, 0x08));
        }

        var (kb2, t2) = Rig(K860(Off3, 0, setup: d => d.Features.Remove(KeyboardFeatureId.DisableKeys)));
        using (kb2)
        {
            await kb2.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb2.IsConnected);
            Assert.Null(await kb2.SetDisabledKeysAsync(DisabledKeys.CapsLock, CancellationToken.None));
            Assert.DoesNotContain(t2.Writes, w => w[2] == DisableIndex);
        }
    }

    // ---- 0x40A3 per channel ----

    [Fact]
    public async Task Channel_fn_lock_writes_each_chosen_host_index_and_skips_null()
    {
        var (kb, t) = Rig(K860(Off3, 0));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            await kb.SetChannelFnLockAsync([true, null, false], CancellationToken.None);
            Assert.True(Wrote(t, FnIndex, 1, 0, 1));
            Assert.True(Wrote(t, FnIndex, 1, 2, 0));
            Assert.DoesNotContain(t.Writes, w => w[2] == FnIndex && w[3] >> 4 == 1 && w[4] == 1);
        }
    }

    [Fact]
    public async Task Channel_fn_lock_is_applied_on_connect_and_the_current_channel_is_inferred()
    {
        // Host 0 is current and reads off; hosts 1 and 2 read on, so the current host is uniquely index 0.
        var (kb, t) = Rig(K860([false, true, true], current: 0));
        using (kb)
        {
            await kb.SetChannelFnLockAsync([null, true, null], CancellationToken.None); // offline: remembered
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => Wrote(t, FnIndex, 1, 1, 1) && kb.CurrentChannel is not null);
            Assert.Equal(0, kb.CurrentChannel);
        }
    }

    [Fact]
    public async Task Current_channel_is_unknown_when_the_reads_cannot_tell_channels_apart()
    {
        var (kb, _) = Rig(K860([true, true, true], current: 1));
        using (kb)
        {
            await kb.SetChannelFnLockAsync([null, null, true], CancellationToken.None);
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            Assert.Null(kb.CurrentChannel);
        }
    }

    // ---- Fn lock reads (the keyboard owns Fn lock; Fn+Esc changes it) ----

    [Fact]
    public async Task Connect_reads_the_keyboards_fn_lock_and_raises_it()
    {
        var (kb, t) = Rig(K860([true, false, false], current: 0));
        using (kb)
        {
            var reads = new List<bool>();
            kb.FnLockRead += (_, on) => { lock (reads) reads.Add(on); };
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.FnLockNow is not null);

            Assert.True(kb.FnLockNow);
            lock (reads) Assert.Contains(true, reads);
            Assert.DoesNotContain(t.Writes, w => w[2] == FnIndex && w[3] >> 4 == 1); // read only
        }
    }

    [Fact]
    public async Task ReadFnLockAsync_sees_a_change_made_on_the_keyboard()
    {
        bool fnOn = false;
        var (kb, t) = Rig(K860(Off3, 0, setup: d => d.Handlers[(FnIndex, 0)] = p => [p[0], (byte)(fnOn ? 1 : 0), 1, 1]));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.FnLockNow is not null);
            Assert.False(kb.FnLockNow);

            fnOn = true; // Fn+Esc
            Assert.True(await kb.ReadFnLockAsync(CancellationToken.None));
            Assert.True(kb.FnLockNow);
            Assert.DoesNotContain(t.Writes, w => w[2] == FnIndex && w[3] >> 4 == 1);
        }
    }

    [Fact]
    public async Task ReadFnLockAsync_is_null_offline_and_without_the_feature()
    {
        var (offline, _) = Rig(K860(Off3, 0));
        using (offline) Assert.Null(await offline.ReadFnLockAsync(CancellationToken.None));

        var (kb, _) = Rig(K860(Off3, 0, setup: d => d.Features.Remove(KeyboardFeatureId.FnInversionMultiHost)));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            Assert.Null(await kb.ReadFnLockAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Fn_lock_written_online_updates_FnLockNow()
    {
        var (kb, _) = Rig(K860(Off3, 0));
        using (kb)
        {
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.FnLockNow is not null);
            await kb.SetFnLockAsync(true, CancellationToken.None);
            Assert.True(kb.FnLockNow);
        }
    }

    // ---- spare keys ----

    [Fact]
    public async Task Spare_keys_are_reset_to_native_on_connect_diverted_temporarily_when_bound_and_raise_events()
    {
        var (kb, t) = Rig(K860(Off3, 0));
        using (kb)
        {
            var events = new List<ButtonEvent>();
            kb.KeyChanged += (_, e) => { lock (events) events.Add(e); };
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);
            bool Divert(ushort cid, byte flags) => t.Writes.Any(w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3
                && w[4] == (byte)(cid >> 8) && w[5] == (byte)cid && w[6] == flags);
            foreach (var cid in new[] { KeyboardId.Spare00EB, KeyboardId.Spare00EC, KeyboardId.Spare0101 }) Assert.True(Divert(cid, 0x02));

            await kb.SetDivertedKeysAsync([KeyboardId.Spare0101], CancellationToken.None);
            Assert.True(Divert(KeyboardId.Spare0101, 0x03));
            Assert.DoesNotContain(t.Writes, w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3 && (w[6] & 0x04) != 0);

            t.Inject(FakeDevice.Frame(0x01, FakeDevice.Reprog, 0, 0, 0x01, 0x01, 0, 0));
            t.Inject(FakeDevice.Frame(0x01, FakeDevice.Reprog, 0, 0, 0, 0, 0, 0));
            await Wait.UntilAsync(() => { lock (events) return events.Count == 2; });
            lock (events) Assert.Equal(new ButtonEvent(KeyboardId.Spare0101, true), events[0]);
        }
    }

    [Fact]
    public void Spare_keys_are_named_from_solaar_or_by_id()
    {
        Assert.Equal("Key 0x00EB", SpareKeys.Label(KeyboardId.Spare00EB));
        Assert.Equal("Second left click", SpareKeys.Label(KeyboardId.Spare00EC));
        Assert.Contains("Launchpad", SpareKeys.Label(KeyboardId.Spare0101));
        Assert.True(SpareKeys.IsSpare(0x0101));
        Assert.False(SpareKeys.IsSpare(KeyboardId.Mute));
    }

    // ---- allowlist ----

    [Fact]
    public void Allowlist_permits_only_the_disable_keys_functions_used()
    {
        Assert.True(HidppAllowlist.IsAllowed(0x4521, 0)); // getCapabilities
        Assert.True(HidppAllowlist.IsAllowed(0x4521, 1)); // getDisabledKeys
        Assert.True(HidppAllowlist.IsAllowed(0x4521, 2)); // setDisabledKeys
        Assert.False(HidppAllowlist.IsAllowed(0x4521, 3));
        Assert.False(HidppAllowlist.IsAllowed(0x40A3, 2));
    }

    [Theory]
    [InlineData((ushort)0x00C2)] // signed DFU control
    [InlineData((ushort)0x00D0)] // DFU
    [InlineData((ushort)0x00D1)] // DFU
    [InlineData((ushort)0x1802)] // device reset
    [InlineData((ushort)0x1E00)] // enable hidden features
    [InlineData((ushort)0x4220)] // lock key state: no public function layout, never called
    [InlineData((ushort)0x4531)] // multi-platform: only the current host is documented, not used
    [InlineData((ushort)0x1803)] // GPIO
    [InlineData((ushort)0x8100)] // onboard profiles
    public void Everything_else_stays_blocked(ushort feature)
    {
        for (byte fn = 0; fn < 16; fn++) Assert.False(HidppAllowlist.IsAllowed(feature, fn), $"{feature:X4} fn{fn}");
    }
}
