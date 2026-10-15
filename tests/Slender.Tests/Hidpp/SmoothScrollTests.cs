using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class SmoothScrollTests
{
    [Theory]
    [InlineData(0x00, true, 0x02)]
    [InlineData(0x01, true, 0x02)]
    [InlineData(0x07, true, 0x06)]
    [InlineData(0x02, false, 0x00)]
    [InlineData(0x07, false, 0x04)]
    [InlineData(0x00, false, 0x00)]
    public void WheelMode_math_never_sets_bit0(int current, bool hiRes, int expected)
    {
        var r = LogiMouse.ComputeWheelMode((byte)current, hiRes);
        Assert.Equal(expected, r);
        Assert.Equal(0, r & 0x01);
    }

    private static (LogiMouse mouse, FakeDevice dev) Create(bool? smooth, byte initial)
    {
        var dev = new FakeDevice();
        var mode = initial;
        dev.Handlers[(FakeDevice.HiRes, 1)] = _ => [mode];
        dev.Handlers[(FakeDevice.HiRes, 2)] = p => { mode = p[0]; return [mode]; };
        var factory = new FakeFactory();
        factory.Enqueue(dev.NewTransport());
        var o = Wait.FastOptions();
        return (new LogiMouse(factory, new LogiMouseOptions
        {
            RequestTimeout = o.RequestTimeout, MaxRetries = o.MaxRetries, RetryDelayBase = o.RetryDelayBase,
            ReadPollMs = o.ReadPollMs, ProbeTimeout = o.ProbeTimeout, BackoffInitial = o.BackoffInitial,
            BackoffMax = o.BackoffMax, HeartbeatInterval = o.HeartbeatInterval,
            AsleepReconnectAfter = o.AsleepReconnectAfter, DisposeUndivertBudget = o.DisposeUndivertBudget,
            SmoothScroll = smooth,
        }), dev);
    }

    [Fact]
    public async Task Connect_sets_hires_and_keeps_bit0_clear()
    {
        var (mouse, dev) = Create(true, 0x01);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.Calls.Count(c => c.Feature == FakeDevice.HiRes && c.Function == 2) == 2);
            var writes = dev.Calls.Where(c => c.Feature == FakeDevice.HiRes && c.Function == 2).Select(c => c.Params[0]).ToArray();
            Assert.Equal(new byte[] { 0x00, 0x02 }, writes); // clear target, then set hi-res
            Assert.All(writes, w => Assert.Equal(0, w & 1));
        }
    }

    [Fact]
    public async Task Unchanged_writes_nothing_and_null_leaves_hires_alone()
    {
        var (on, devOn) = Create(true, 0x02);
        var (none, devNone) = Create(null, 0x02);
        using (on)
        using (none)
        {
            await on.StartAsync(CancellationToken.None);
            await none.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => on.IsConnected && none.IsConnected);
            await Task.Delay(100);
            Assert.Equal(0, devOn.CountCalls(FakeDevice.HiRes, 2));
            Assert.Equal(0, devNone.CountCalls(FakeDevice.HiRes, 2));
        }
    }

    [Fact]
    public async Task SetSmoothScrollAsync_toggles_off_keeping_other_bits()
    {
        var (mouse, dev) = Create(true, 0x06);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.SetSmoothScrollAsync(false, CancellationToken.None);
            var last = dev.Calls.Last(c => c.Feature == FakeDevice.HiRes && c.Function == 2);
            Assert.Equal(0x04, last.Params[0]);
        }
    }
}
