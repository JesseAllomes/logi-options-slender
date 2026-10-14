using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class SmartShiftTests
{
    private const byte SmartShift = 13;

    private static (LogiMouse mouse, FakeDevice dev) Create(SmartShiftSetting? setting, byte[] current)
    {
        var dev = new FakeDevice();
        dev.Features[FeatureId.SmartShiftEnhanced] = SmartShift;
        dev.Handlers[(SmartShift, 1)] = _ => current;
        dev.Handlers[(SmartShift, 2)] = _ => [];
        var factory = new FakeFactory();
        factory.Enqueue(dev.NewTransport());
        var options = Wait.FastOptions();
        return (new LogiMouse(factory, new LogiMouseOptions
        {
            RequestTimeout = options.RequestTimeout,
            MaxRetries = options.MaxRetries,
            RetryDelayBase = options.RetryDelayBase,
            ReadPollMs = options.ReadPollMs,
            ProbeTimeout = options.ProbeTimeout,
            BackoffInitial = options.BackoffInitial,
            BackoffMax = options.BackoffMax,
            HeartbeatInterval = options.HeartbeatInterval,
            AsleepReconnectAfter = options.AsleepReconnectAfter,
            DisposeUndivertBudget = options.DisposeUndivertBudget,
            SmartShift = setting,
        }), dev);
    }

    [Fact]
    public async Task Connect_writes_threshold_keeping_mode_and_force()
    {
        var (mouse, dev) = Create(new SmartShiftSetting(true, 12), [0x01, 22, 100]);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(SmartShift, 2) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == SmartShift && c.Function == 2);
            Assert.Equal(new byte[] { 0x01, 12, 100 }, set.Params[..3]);
        }
    }

    [Fact]
    public async Task Disabled_writes_0xFF_threshold()
    {
        var (mouse, dev) = Create(new SmartShiftSetting(false, 22), [0x02, 22, 100]);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(SmartShift, 2) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == SmartShift && c.Function == 2);
            Assert.Equal(new byte[] { 0x02, 0xFF, 100 }, set.Params[..3]);
        }
    }

    [Fact]
    public async Task Unchanged_threshold_and_null_setting_write_nothing()
    {
        var (same, devSame) = Create(new SmartShiftSetting(true, 22), [0x01, 22, 100]);
        var (none, devNone) = Create(null, [0x01, 22, 100]);
        using (same)
        using (none)
        {
            await same.StartAsync(CancellationToken.None);
            await none.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => same.IsConnected && none.IsConnected);
            await Wait.UntilAsync(() => devSame.CountCalls(SmartShift, 1) == 1);
            Assert.Equal(0, devSame.CountCalls(SmartShift, 2));
            Assert.Equal(0, devNone.CountCalls(SmartShift, 1) + devNone.CountCalls(SmartShift, 2));
        }
    }
}
