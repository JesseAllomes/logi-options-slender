using System.Text;
using Slender.Engine;
using Slender.Hidpp;
using Slender.Settings.Logic;

namespace Slender.Tests.Hidpp;

/// <summary>m42: wake/reconnect events, unhandled-notification log, wheel mode and Easy-Switch host reads.</summary>
public class DeviceEventTests
{
    private const byte Wds = 21, Cfg = 22, SmartShift = 23, FeatSet = 2, HostChange = 24, HostsInfoIdx = 25, LockKey = 26;

    private static FakeDevice MouseDevice(Func<byte> mode, Action<FakeDevice>? setup = null)
    {
        var dev = new FakeDevice();
        dev.Features[FeatureId.WirelessDeviceStatus] = Wds;
        dev.Features[FeatureId.ConfigChange] = Cfg;
        dev.Features[FeatureId.SmartShiftEnhanced] = SmartShift;
        dev.Features[FeatureId.FeatureSet] = FeatSet;
        dev.Features[0x4220] = LockKey;
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
        dev.Handlers[(FakeDevice.Thumb, 1)] = _ => [0, 0];
        dev.Handlers[(FakeDevice.Thumb, 2)] = _ => [];
        dev.Handlers[(FakeDevice.Thumb, 0)] = _ => [0, 0, 0, 0, 0, 0, 0, 0];
        dev.Handlers[(FakeDevice.HiRes, 1)] = _ => [0x02];
        dev.Handlers[(SmartShift, 1)] = _ => [mode(), 10, 75];
        dev.Handlers[(SmartShift, 2)] = _ => [];
        dev.Handlers[(FeatSet, 1)] = p => p[0] == LockKey ? [0x42, 0x20, 0] : [0x12, 0x34, 0];
        setup?.Invoke(dev);
        return dev;
    }

    private static byte[] Event(byte feature, byte fn, params byte[] p) => FakeDevice.Frame(0xFF, feature, fn, 0, p);

    private static int DivertWrites(FakeTransport t, ushort cid) => t.Writes.Count(w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3
        && w[4] == (byte)(cid >> 8) && w[5] == (byte)cid && w[6] == 0x03);

    // ---- 0x1D4B reconnection ----

    [Fact]
    public async Task Reconnection_event_reapplies_volatile_state_and_raises_Reconfigured()
    {
        var dev = MouseDevice(() => 2);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        using var mouse = new LogiMouse(f, Wait.FastOptions());
        int reconfigured = 0;
        mouse.Reconfigured += (_, _) => Interlocked.Increment(ref reconfigured);
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        await mouse.SetDivertedControlsAsync([ControlId.Gesture], CancellationToken.None);
        int before = DivertWrites(t, ControlId.Gesture);
        Assert.Equal(1, before);

        t.Inject(Event(Wds, 0, 1, 1, 1)); // status 1 = reconnection, request 1, reason 1 = power switch

        await Wait.UntilAsync(() => Volatile.Read(ref reconfigured) == 1);
        Assert.True(DivertWrites(t, ControlId.Gesture) > before, "the divert is written again");
        Assert.True(mouse.IsConnected);
        Assert.Equal(1, f.OpenCalls); // same session: no reconnect needed
    }

    [Fact]
    public async Task Wireless_status_without_reconnection_or_request_does_nothing()
    {
        var dev = MouseDevice(() => 2);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        using var mouse = new LogiMouse(f, Wait.FastOptions());
        int reconfigured = 0;
        mouse.Reconfigured += (_, _) => Interlocked.Increment(ref reconfigured);
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        await mouse.SetDivertedControlsAsync([ControlId.Gesture], CancellationToken.None);
        int before = DivertWrites(t, ControlId.Gesture);

        t.Inject(Event(Wds, 0, 0, 0, 0));
        await Task.Delay(200);

        Assert.Equal(0, Volatile.Read(ref reconfigured));
        Assert.Equal(before, DivertWrites(t, ControlId.Gesture));
    }

    [Fact]
    public void Wireless_status_layout_is_status_request_reason()
    {
        Assert.True(HidppMessage.TryParse(Event(Wds, 0, 1, 0, 1), out var m));
        Assert.Equal((true, false, (byte)1), LogiDeviceBase.ParseWirelessStatus(m!));
        Assert.True(HidppMessage.TryParse(Event(Wds, 0, 0, 1, 0), out m));
        Assert.Equal((false, true, (byte)0), LogiDeviceBase.ParseWirelessStatus(m!));
    }

    [Fact]
    public async Task Config_change_is_logged_once_and_reapplies_but_our_own_echo_is_ignored()
    {
        var dev = MouseDevice(() => 2);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        long now = 1_000_000;
        var lines = new List<string>();
        var options = new LogiMouseOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(150), MaxRetries = 1, RetryDelayBase = TimeSpan.FromMilliseconds(1), ReadPollMs = 20,
            ProbeTimeout = TimeSpan.FromMilliseconds(60), BackoffInitial = TimeSpan.FromMilliseconds(20), BackoffMax = TimeSpan.FromMilliseconds(80),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30), DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
            Clock = () => Interlocked.Read(ref now),
        };
        using var mouse = new LogiMouse(f, options) { LogSink = l => { lock (lines) lines.Add(l); } };
        int reconfigured = 0;
        mouse.Reconfigured += (_, _) => Interlocked.Increment(ref reconfigured);
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);

        // Right after the connect apply: the event is the echo of our own writes.
        Interlocked.Add(ref now, 500);
        t.Inject(Event(Cfg, 0, 0xAB, 0xCD));
        await Wait.UntilAsync(() => { lock (lines) return lines.Any(l => l.Contains("config change event")); });
        await Task.Delay(100);
        Assert.Equal(0, Volatile.Read(ref reconfigured));

        // Later (other software changed the device): re-apply.
        Interlocked.Add(ref now, 60_000);
        t.Inject(Event(Cfg, 0, 0xAB, 0xCD));
        await Wait.UntilAsync(() => Volatile.Read(ref reconfigured) == 1);
        string[] cfgLines;
        lock (lines) cfgLines = [.. lines.Where(l => l.Contains("config change event"))];
        Assert.Equal(2, cfgLines.Length);
        Assert.Contains("ABCD", cfgLines[0]);
    }

    // ---- unhandled notifications ----

    [Fact]
    public void Log_limiter_lets_one_line_per_key_through_per_interval()
    {
        var limiter = new NotificationLogLimiter(TimeSpan.FromSeconds(10));
        Assert.True(limiter.Allow(0x4220, 0));
        Assert.False(limiter.Allow(0x4220, 5_000));
        Assert.True(limiter.Allow(0x1004, 5_000)); // another feature has its own budget
        Assert.False(limiter.Allow(0x4220, 9_999));
        Assert.True(limiter.Allow(0x4220, 10_000));
        Assert.False(limiter.Allow(0x4220, 10_001));
    }

    [Fact]
    public async Task Unhandled_notifications_are_logged_with_the_feature_id_and_rate_limited()
    {
        var dev = MouseDevice(() => 2);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        long now = 5_000_000;
        var lines = new List<string>();
        var options = new LogiMouseOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(150), MaxRetries = 1, RetryDelayBase = TimeSpan.FromMilliseconds(1), ReadPollMs = 20,
            ProbeTimeout = TimeSpan.FromMilliseconds(60), BackoffInitial = TimeSpan.FromMilliseconds(20), BackoffMax = TimeSpan.FromMilliseconds(80),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30), DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
            Clock = () => Interlocked.Read(ref now),
        };
        using var mouse = new LogiMouse(f, options) { LogSink = l => { lock (lines) lines.Add(l); } };
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        int Count(string text) { lock (lines) return lines.Count(l => l.Contains(text)); }

        for (int i = 0; i < 5; i++) t.Inject(Event(LockKey, 0, 0x01, (byte)i, 0x07)); // the K860-style lock key feature, layout unknown
        t.Inject(Event(30, 0, 0x99)); // a sentinel on another index: once it is logged the five above were all seen
        await Wait.UntilAsync(() => Count("0x1234") == 1 && Count("0x4220") >= 1);
        Assert.Equal(1, Count("unhandled notification feature 0x4220: "));

        Interlocked.Add(ref now, 11_000);
        t.Inject(Event(LockKey, 0, 0x02, 0x00, 0x00));
        await Wait.UntilAsync(() => Count("unhandled notification feature 0x4220") == 2);
    }

    [Fact]
    public async Task Handled_features_are_not_logged_as_unhandled()
    {
        var dev = MouseDevice(() => 2);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        var lines = new List<string>();
        using var mouse = new LogiMouse(f, Wait.FastOptions()) { LogSink = l => { lock (lines) lines.Add(l); } };
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);

        t.Inject(Event(FakeDevice.Thumb, 0, 0, 1, 0, 0, 1, 0x02)); // thumb wheel: handled by the mouse
        t.Inject(Event(SmartShift, 0, 2)); // wheel mode: handled
        t.Inject(Event(30, 0, 0x99)); // sentinel
        await Wait.UntilAsync(() => { lock (lines) return lines.Any(l => l.Contains("0x1234")); });
        lock (lines) Assert.Single(lines, l => l.Contains("unhandled"));
    }

    // ---- wheel mode ----

    [Fact]
    public void Wheel_mode_parse_follows_the_documented_bytes()
    {
        Assert.Equal(WheelMode.FreeSpin, LogiMouse.ParseWheelMode([1, 10, 75]));
        Assert.Equal(WheelMode.Ratchet, LogiMouse.ParseWheelMode([2, 10, 75]));
        Assert.Equal(WheelMode.Unknown, LogiMouse.ParseWheelMode([0, 10, 75]));
        Assert.Equal(WheelMode.Unknown, LogiMouse.ParseWheelMode([9, 10, 75]));
        Assert.Equal(WheelMode.Ratchet, LogiMouse.ParseRatchetSwitch([0x01]));
        Assert.Equal(WheelMode.Ratchet, LogiMouse.ParseRatchetSwitch([0x03]));
        Assert.Equal(WheelMode.FreeSpin, LogiMouse.ParseRatchetSwitch([0x00]));
    }

    [Fact]
    public async Task Wheel_mode_is_read_on_connect_after_our_toggle_and_from_events()
    {
        byte mode = 2; // ratchet
        var dev = MouseDevice(() => Volatile.Read(ref mode), d => d.Handlers[(SmartShift, 2)] = p => { Volatile.Write(ref mode, p[0]); return []; });
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        using var mouse = new LogiMouse(f, Wait.FastOptions());
        var seen = new List<WheelMode>();
        mouse.WheelModeChanged += (_, m) => { lock (seen) seen.Add(m); };
        Assert.Equal(WheelMode.Unknown, mouse.WheelMode);

        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        Assert.Equal(WheelMode.Ratchet, mouse.WheelMode); // read on connect, before IsConnected
        lock (seen) Assert.Equal([WheelMode.Ratchet], seen);

        Assert.True(await mouse.ToggleWheelModeAsync(CancellationToken.None)); // Slender's own toggle: ratchet -> free-spin
        Assert.Equal(WheelMode.FreeSpin, mouse.WheelMode);

        // The wheel's own button flips it back; the device sends a 0x2111 event (p[0] = new mode) and the read confirms.
        Volatile.Write(ref mode, 2);
        t.Inject(Event(SmartShift, 0, 2, 10, 75));
        await Wait.UntilAsync(() => mouse.WheelMode == WheelMode.Ratchet);

        // 0x2121 event 1 "ratchet switch", p[0] bit0 = ratchet, confirmed by the 0x2111 read that follows.
        Volatile.Write(ref mode, 1);
        t.Inject(Event(FakeDevice.HiRes, 1, 0x00));
        await Wait.UntilAsync(() => mouse.WheelMode == WheelMode.FreeSpin);

        lock (seen) Assert.Equal([WheelMode.Ratchet, WheelMode.FreeSpin, WheelMode.Ratchet, WheelMode.FreeSpin], seen);
    }

    [Fact]
    public async Task Wheel_mode_event_with_a_wrong_guess_is_corrected_by_the_read()
    {
        byte mode = 2;
        var dev = MouseDevice(() => Volatile.Read(ref mode));
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        using var mouse = new LogiMouse(f, Wait.FastOptions());
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);

        t.Inject(Event(FakeDevice.HiRes, 1, 0x00)); // decoded as free-spin first, but the device still says ratchet
        await Task.Delay(300);
        Assert.Equal(WheelMode.Ratchet, mouse.WheelMode);
    }

    // ---- Easy-Switch hosts ----

    [Fact]
    public async Task Host_name_is_assembled_across_chunks()
    {
        const string name = "Jesse's Work Laptop 01"; // 22 bytes: a 14-byte chunk and an 8-byte chunk
        var bytes = Encoding.UTF8.GetBytes(name);
        var requests = new List<byte[]>();
        var result = await LogiDeviceBase.ReadHostNameChunksAsync(req =>
        {
            requests.Add(req);
            var chunk = new byte[16];
            chunk[0] = req[0]; chunk[1] = req[1];
            int n = Math.Min(14, bytes.Length - req[1]);
            Array.Copy(bytes, req[1], chunk, 2, n);
            return Task.FromResult(chunk);
        }, host: 2, nameLength: bytes.Length);

        Assert.Equal(name, result);
        Assert.Equal(2, requests.Count);
        Assert.Equal(new byte[] { 2, 0 }, requests[0]);
        Assert.Equal(new byte[] { 2, 14 }, requests[1]); // second chunk asks for the offset after the first
    }

    [Fact]
    public async Task Host_name_stops_at_NUL_and_gives_null_for_an_empty_name()
    {
        Task<byte[]> Reply(byte[] req)
        {
            var chunk = new byte[16];
            Encoding.ASCII.GetBytes("Desk").CopyTo(chunk, 2); // then NULs although the length said 10
            return Task.FromResult(chunk);
        }
        Assert.Equal("Desk", await LogiDeviceBase.ReadHostNameChunksAsync(Reply, 0, 10));
        Assert.Null(await LogiDeviceBase.ReadHostNameChunksAsync(_ => Task.FromResult(new byte[16]), 0, 5));
    }

    [Fact]
    public void Host_info_layouts()
    {
        Assert.Equal((3, 1), LogiDeviceBase.ParseChangeHost([3, 1]));
        Assert.Equal((3, 2), LogiDeviceBase.ParseHostsCapabilities([0x0F, 0, 3, 2]));
        Assert.Equal((true, 6), LogiDeviceBase.ParseHostInfo([0, 1, 1, 0, 6, 0]));
        Assert.Equal((false, 0), LogiDeviceBase.ParseHostInfo([2, 0, 0, 0, 0, 0]));
    }

    private static FakeDevice Keyboard(Action<FakeDevice>? setup = null)
    {
        var dev = new FakeDevice();
        dev.Features.Clear();
        dev.Features[FeatureId.ReprogControlsV4] = FakeDevice.Reprog;
        dev.Features[FeatureId.Battery] = FakeDevice.Battery;
        dev.Features[KeyboardFeatureId.FnInversionMultiHost] = 12;
        dev.Features[FeatureId.ChangeHost] = HostChange;
        dev.Features[FeatureId.HostsInfo] = HostsInfoIdx;
        dev.AcceptedIndices.Clear();
        dev.AcceptedIndices.Add(0x01);
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
        dev.Handlers[(FakeDevice.Battery, 0)] = _ => [87, 80, 0];
        dev.Handlers[(12, 0)] = p => [p[0], 1]; // every channel reads the same: the inference alone is ambiguous
        dev.Handlers[(12, 1)] = _ => [];
        dev.Handlers[(HostChange, 0)] = _ => [3, 2];
        dev.Handlers[(HostsInfoIdx, 0)] = _ => [0, 0, 3, 2];
        dev.Handlers[(HostsInfoIdx, 1)] = p => p[0] switch
        {
            0 => [0, 1, 1, 0, 6, 0],
            1 => [1, 1, 1, 0, 4, 0],
            _ => [2, 0, 0, 0, 0, 0],
        };
        dev.Handlers[(HostsInfoIdx, 3)] = p => p[0] == 0
            ? [0, 0, .. "Laptop"u8.ToArray()]
            : [1, 0, .. "Desk"u8.ToArray()];
        setup?.Invoke(dev);
        return dev;
    }

    [Fact]
    public async Task Keyboard_reads_hosts_and_the_real_channel_replaces_the_inference()
    {
        var dev = Keyboard();
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        using var kb = new LogiKeyboard(f, new LogiKeyboardOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(150), MaxRetries = 1, RetryDelayBase = TimeSpan.FromMilliseconds(1), ReadPollMs = 20,
            ProbeTimeout = TimeSpan.FromMilliseconds(60), BackoffInitial = TimeSpan.FromMilliseconds(20), BackoffMax = TimeSpan.FromMilliseconds(80),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30), DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
        });
        int changed = 0;
        kb.HostsChanged += (_, _) => Interlocked.Increment(ref changed);
        Assert.Null(kb.Hosts);

        await kb.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => kb.IsConnected);

        var hosts = kb.Hosts!;
        Assert.Equal(2, hosts.Current);
        Assert.Equal(3, hosts.Hosts.Count);
        Assert.Equal(new HostEntry(0, "Laptop", true), hosts.Hosts[0]);
        Assert.Equal(new HostEntry(1, "Desk", true), hosts.Hosts[1]);
        Assert.Equal(new HostEntry(2, null, false), hosts.Hosts[2]);
        Assert.Equal(2, kb.CurrentChannel); // every 0x40A3 read matched, so the old inference would have said "unknown"
        Assert.Equal(1, Volatile.Read(ref changed));

        // Only read functions went out: 0x1814 fn0, 0x1815 fn0/fn1/fn3; never fn1 of 0x1814 or fn2 of 0x1815.
        Assert.Equal(0, dev.CountCalls(HostChange, 1));
        Assert.Equal(0, dev.CountCalls(HostsInfoIdx, 2));
        Assert.DoesNotContain(dev.Calls, c => c.Feature == HostChange && c.Function != 0);
        Assert.DoesNotContain(dev.Calls, c => c.Feature == HostsInfoIdx && c.Function is not (0 or 1 or 3));

        // A refresh that reads the same thing does not announce a change.
        Assert.NotNull(await kb.RefreshHostsAsync(CancellationToken.None));
        Assert.Equal(1, Volatile.Read(ref changed));
    }

    [Fact]
    public async Task Keyboard_without_host_features_keeps_the_inference_fallback()
    {
        var dev = Keyboard(d =>
        {
            d.Features.Remove(FeatureId.ChangeHost);
            d.Features.Remove(FeatureId.HostsInfo);
            d.Handlers[(12, 0)] = p => [p[0], p[0] is 0xFF or 1 ? (byte)1 : (byte)0]; // only channel 1 matches the current host (0xFF)
        });
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        using var kb = new LogiKeyboard(f, new LogiKeyboardOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(150), MaxRetries = 1, RetryDelayBase = TimeSpan.FromMilliseconds(1), ReadPollMs = 20,
            ProbeTimeout = TimeSpan.FromMilliseconds(60), BackoffInitial = TimeSpan.FromMilliseconds(20), BackoffMax = TimeSpan.FromMilliseconds(80),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30), DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
        });
        await kb.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => kb.IsConnected);

        Assert.Null(kb.Hosts);
        Assert.Equal(1, kb.CurrentChannel); // inferred from the 0x40A3 reads, as before
    }

    [Fact]
    public void Host_rows_read_channel_name_and_current()
    {
        Assert.Equal("Channel 1 � Laptop � current", KeyboardOps.HostRowTitle(new HostState(0, "Laptop", true, true)));
        Assert.Equal("Channel 2 � Desk", KeyboardOps.HostRowTitle(new HostState(1, "Desk", true, false)));
        Assert.Equal("Channel 3 � not paired", KeyboardOps.HostRowTitle(new HostState(2, null, false, false)));
        Assert.Equal("Channel 2", KeyboardOps.HostRowTitle(new HostState(1, null, true, false)));
    }
}
