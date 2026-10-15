using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class FeelTests
{
    private const byte SmartShift = 13, Force = 12, Haptic = FakeDevice.Haptics;

    private static (LogiMouse mouse, FakeDevice dev) Create(int? torque = null, int? force = null, int? baseline = null, int? haptic = null,
        int currentForce = 3887, byte currentHaptic = 45)
    {
        var dev = new FakeDevice();
        dev.Features[FeatureId.SmartShiftEnhanced] = SmartShift;
        dev.Features[FeatureId.ForceSensing] = Force;
        dev.Features[FeatureId.Haptics] = Haptic;
        dev.Handlers[(SmartShift, 1)] = _ => [0x02, 23, 100];
        dev.Handlers[(SmartShift, 2)] = _ => [];
        dev.Handlers[(Force, 2)] = _ => [(byte)(currentForce >> 8), (byte)currentForce, 0, 0];
        dev.Handlers[(Force, 3)] = _ => [];
        dev.Handlers[(Haptic, 1)] = _ => [1, currentHaptic];
        dev.Handlers[(Haptic, 2)] = _ => [];
        var factory = new FakeFactory();
        factory.Enqueue(dev.NewTransport());
        var o = Wait.FastOptions();
        return (new LogiMouse(factory, new LogiMouseOptions
        {
            RequestTimeout = o.RequestTimeout, MaxRetries = o.MaxRetries, RetryDelayBase = o.RetryDelayBase,
            ReadPollMs = o.ReadPollMs, ProbeTimeout = o.ProbeTimeout, BackoffInitial = o.BackoffInitial,
            BackoffMax = o.BackoffMax, HeartbeatInterval = o.HeartbeatInterval,
            AsleepReconnectAfter = o.AsleepReconnectAfter, DisposeUndivertBudget = o.DisposeUndivertBudget,
            WheelResistance = torque, SensePanelForce = force, SensePanelBaseline = baseline, HapticStrength = haptic,
        }), dev);
    }

    [Fact]
    public async Task Connect_writes_torque_keeping_mode_and_auto_disengage()
    {
        var (mouse, dev) = Create(torque: 40);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(SmartShift, 2) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == SmartShift && c.Function == 2);
            Assert.Equal(new byte[] { 0x02, 23, 40 }, set.Params[..3]);
        }
    }

    [Fact]
    public async Task Torque_is_clamped_and_equal_value_writes_nothing()
    {
        var (high, devHigh) = Create(torque: 500);
        var (same, devSame) = Create(torque: 100);
        using (high)
        using (same)
        {
            await high.StartAsync(CancellationToken.None);
            await same.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => devHigh.CountCalls(SmartShift, 1) == 1 && devSame.CountCalls(SmartShift, 1) == 1);
            Assert.Equal(0, devHigh.CountCalls(SmartShift, 2)); // 500 clamps to 100 = what the device already has
            Assert.Equal(0, devSame.CountCalls(SmartShift, 2));
        }
    }

    [Fact]
    public async Task Wheel_resistance_reads_back_and_sets_while_connected()
    {
        var (mouse, dev) = Create();
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.Equal(100, await mouse.GetWheelResistanceAsync(CancellationToken.None));
            await mouse.SetWheelResistanceAsync(10, CancellationToken.None);
            var set = Assert.Single(dev.Calls, c => c.Feature == SmartShift && c.Function == 2);
            Assert.Equal(new byte[] { 0x02, 23, 10 }, set.Params[..3]);
        }
    }

    [Fact]
    public async Task Null_values_touch_nothing()
    {
        var (mouse, dev) = Create();
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.SetWheelResistanceAsync(null, CancellationToken.None);
            await mouse.SetSensePanelForceAsync(null, CancellationToken.None);
            await mouse.SetHapticStrengthAsync(null, CancellationToken.None);
            Assert.DoesNotContain(dev.Calls, c => c.Function == 2 && (c.Feature == SmartShift || c.Feature == Haptic));
            Assert.DoesNotContain(dev.Calls, c => c.Feature == Force && c.Function == 3);
        }
    }

    [Theory]
    [InlineData(5000, 5000)]
    [InlineData(10, 1943)]      // below the band: clamped to 50% of 3887
    [InlineData(60000, 7774)]   // above the band: clamped to 200%
    public async Task Sense_panel_force_is_clamped_to_the_band(int wanted, int written)
    {
        var (mouse, dev) = Create(force: wanted);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(Force, 3) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == Force && c.Function == 3);
            Assert.Equal(new byte[] { 0, (byte)(written >> 8), (byte)written }, set.Params[..3]);
            Assert.Equal(3887, mouse.SensePanelBaseline); // the first reading is remembered
        }
    }

    [Fact]
    public async Task Stored_baseline_anchors_the_band_even_after_the_device_value_changed()
    {
        var (mouse, dev) = Create(force: 60000, baseline: 2000, currentForce: 3000);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(Force, 3) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == Force && c.Function == 3);
            Assert.Equal(new byte[] { 0, 0x0F, 0xA0 }, set.Params[..3]); // 4000 = 200% of 2000
            Assert.Equal(2000, mouse.SensePanelBaseline);
        }
    }

    [Fact]
    public async Task Sense_panel_force_reads_back()
    {
        var (mouse, _) = Create();
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.Equal(3887, await mouse.GetSensePanelForceAsync(CancellationToken.None));
        }
    }

    [Fact]
    public void Band_is_half_to_double()
    {
        Assert.Equal((1943, 7774), LogiMouse.SensePanelBand(3887));
        Assert.Equal((20000, 0xFFFF), LogiMouse.SensePanelBand(40000));
        Assert.Equal((1, 2), LogiMouse.SensePanelBand(1));
    }

    [Fact]
    public async Task Haptic_strength_writes_enabled_and_level_and_reads_back()
    {
        var (mouse, dev) = Create(haptic: 80);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(Haptic, 2) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == Haptic && c.Function == 2);
            Assert.Equal(new byte[] { 1, 80 }, set.Params[..2]);
            Assert.Equal(45, await mouse.GetHapticStrengthAsync(CancellationToken.None)); // the fake returns a fixed state
        }
    }

    [Fact]
    public async Task Same_haptic_strength_writes_nothing_and_over_range_clamps()
    {
        var (same, devSame) = Create(haptic: 45);
        var (over, devOver) = Create(haptic: 400, currentHaptic: 100);
        using (same)
        using (over)
        {
            await same.StartAsync(CancellationToken.None);
            await over.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => devSame.CountCalls(Haptic, 1) == 1 && devOver.CountCalls(Haptic, 1) == 1);
            Assert.Equal(0, devSame.CountCalls(Haptic, 2));
            Assert.Equal(0, devOver.CountCalls(Haptic, 2)); // 400 clamps to 100 = current
        }
    }

    [Fact]
    public void Only_fn3_of_force_sensing_became_writable()
    {
        // 0x19C0 fn3 is the single newly allowed request; 0x2111 fn2 and 0x19B0 fn2 were already inside their ranges.
        Assert.True(HidppAllowlist.IsAllowed(0x19C0, 3));
        Assert.False(HidppAllowlist.IsAllowed(0x19C0, 4));
        Assert.True(HidppAllowlist.IsAllowed(0x2111, 2));
        Assert.False(HidppAllowlist.IsAllowed(0x2111, 3));
        Assert.True(HidppAllowlist.IsAllowed(0x19B0, 2));
        Assert.False(HidppAllowlist.IsAllowed(0x19B0, 5));
    }
}
