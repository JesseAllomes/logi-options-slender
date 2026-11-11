using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class BatteryEventTests
{
    private const byte SwIdOurs = 0x0B;

    [Theory]
    [InlineData((byte)0, 70, false, ChargeState.Discharging)]
    [InlineData((byte)1, 70, true, ChargeState.Charging)]
    [InlineData((byte)2, 70, true, ChargeState.Charging)]
    [InlineData((byte)3, 100, true, ChargeState.Full)]
    [InlineData((byte)4, 70, false, ChargeState.Error)]
    public void Unified_status_codes(byte code, int pct, bool charging, ChargeState state)
        => Assert.Equal(new BatteryStatus(pct, charging, state), LogiDeviceBase.ParseUnifiedBattery([(byte)pct, 0x08, code]));

    [Theory]
    [InlineData((byte)0, false, ChargeState.Discharging)]
    [InlineData((byte)1, true, ChargeState.Charging)]
    [InlineData((byte)2, true, ChargeState.Charging)]
    [InlineData((byte)3, true, ChargeState.Full)]
    [InlineData((byte)4, true, ChargeState.Charging)]
    [InlineData((byte)5, false, ChargeState.Error)]
    [InlineData((byte)6, false, ChargeState.Error)]
    [InlineData((byte)7, false, ChargeState.Error)]
    public void Legacy_status_codes(byte code, bool charging, ChargeState state)
        => Assert.Equal(new BatteryStatus(42, charging, state), LogiDeviceBase.ParseBatteryStatus([42, 30, code]));

    [Fact]
    public void Percent_is_clamped()
        => Assert.Equal(100, LogiDeviceBase.ParseUnifiedBattery([200, 0, 0]).Percent);

    private static (LogiMouse mouse, FakeTransport t) MouseRig()
    {
        var dev = new FakeDevice();
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        return (new LogiMouse(f, Wait.FastOptions()), t);
    }

    private static (LogiKeyboard kb, FakeTransport t) KeyboardRig()
    {
        var dev = new FakeDevice();
        dev.Features.Clear();
        dev.Features[FeatureId.ReprogControlsV4] = FakeDevice.Reprog;
        dev.Features[FeatureId.Battery] = FakeDevice.Battery;
        dev.AcceptedIndices.Clear();
        dev.AcceptedIndices.Add(0x01);
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        var o = new LogiKeyboardOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(150), MaxRetries = 1, RetryDelayBase = TimeSpan.FromMilliseconds(1), ReadPollMs = 20,
            ProbeTimeout = TimeSpan.FromMilliseconds(60), BackoffInitial = TimeSpan.FromMilliseconds(20), BackoffMax = TimeSpan.FromMilliseconds(80),
            HeartbeatInterval = TimeSpan.FromMilliseconds(30), AsleepReconnectAfter = TimeSpan.FromSeconds(60),
            DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
        };
        return (new LogiKeyboard(f, o), t);
    }

    [Fact]
    public async Task Mouse_raises_event_for_0x1004_and_logs_only_state_changes()
    {
        var (mouse, t) = MouseRig();
        var lines = new List<string>();
        mouse.LogSink = l => { lock (lines) lines.Add(l); };
        using (mouse)
        {
            var got = new List<BatteryStatus>();
            mouse.BatteryChanged += (_, b) => { lock (got) got.Add(b); };
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);

            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, 0, 15, 0, 1));
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, 0, 16, 0, 1));
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, 0, 100, 0, 3));
            await Wait.UntilAsync(() => { lock (got) return got.Count == 3; });
            lock (got)
            {
                Assert.Equal(new BatteryStatus(15, true, ChargeState.Charging), got[0]);
                Assert.Equal(new BatteryStatus(100, true, ChargeState.Full), got[2]);
            }
            await Wait.UntilAsync(() => { lock (lines) return lines.Count(l => l.Contains("battery event")) == 2; });
            lock (lines) Assert.Contains(lines, l => l.Contains("battery event: 15% Charging"));
        }
    }

    [Fact]
    public async Task Keyboard_raises_event_for_0x1000()
    {
        var (kb, t) = KeyboardRig();
        using (kb)
        {
            var got = new List<BatteryStatus>();
            kb.BatteryChanged += (_, b) => { lock (got) got.Add(b); };
            await kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => kb.IsConnected);

            t.Inject(FakeDevice.Frame(0x01, FakeDevice.Battery, 0, 0, 60, 50, 1));
            t.Inject(FakeDevice.Frame(0x01, FakeDevice.Battery, 0, 0, 5, 0, 6));
            await Wait.UntilAsync(() => { lock (got) return got.Count == 2; });
            lock (got)
            {
                Assert.Equal(new BatteryStatus(60, true, ChargeState.Charging), got[0]);
                Assert.Equal(new BatteryStatus(5, false, ChargeState.Error), got[1]);
            }
        }
    }

    [Fact]
    public async Task Event_from_our_own_swid_is_ignored()
    {
        var (mouse, t) = MouseRig();
        using (mouse)
        {
            int n = 0;
            mouse.BatteryChanged += (_, _) => Interlocked.Increment(ref n);
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);

            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, SwIdOurs, 15, 0, 1));
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, 0, 20, 0, 0)); // marker, processed after the ignored one
            await Wait.UntilAsync(() => Volatile.Read(ref n) >= 1);
            await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref n));
        }
    }

    [Fact]
    public async Task Other_feature_index_and_other_function_are_not_battery()
    {
        var (mouse, t) = MouseRig();
        using (mouse)
        {
            int n = 0;
            mouse.BatteryChanged += (_, _) => Interlocked.Increment(ref n);
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);

            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.HiRes, 0, 0, 15, 0, 1));    // unrelated feature
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 1, 0, 15, 0, 1));  // battery, but not the fn0 event
            await Task.Delay(150);
            Assert.Equal(0, Volatile.Read(ref n));
        }
    }

    [Fact]
    public async Task Handler_exception_does_not_break_notifications()
    {
        var (mouse, t) = MouseRig();
        using (mouse)
        {
            int n = 0;
            mouse.BatteryChanged += (_, _) => { Interlocked.Increment(ref n); throw new InvalidOperationException("boom"); };
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, 0, 15, 0, 1));
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Battery, 0, 0, 16, 0, 1));
            await Wait.UntilAsync(() => Volatile.Read(ref n) == 2);
        }
    }
}
