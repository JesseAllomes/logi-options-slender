using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class DpiTests
{
    private const byte Dpi = 14;

    private static (LogiMouse mouse, FakeDevice dev) Create(int? dpi, int current)
    {
        var dev = new FakeDevice();
        dev.Features[FeatureId.AdjustableDpi] = Dpi;
        dev.Handlers[(Dpi, 2)] = _ => [0, (byte)(current >> 8), (byte)current, 0x03, 0xE8];
        dev.Handlers[(Dpi, 3)] = _ => [];
        var factory = new FakeFactory();
        factory.Enqueue(dev.NewTransport());
        var o = Wait.FastOptions();
        return (new LogiMouse(factory, new LogiMouseOptions
        {
            RequestTimeout = o.RequestTimeout, MaxRetries = o.MaxRetries, RetryDelayBase = o.RetryDelayBase,
            ReadPollMs = o.ReadPollMs, ProbeTimeout = o.ProbeTimeout, BackoffInitial = o.BackoffInitial,
            BackoffMax = o.BackoffMax, HeartbeatInterval = o.HeartbeatInterval,
            AsleepReconnectAfter = o.AsleepReconnectAfter, DisposeUndivertBudget = o.DisposeUndivertBudget,
            Dpi = dpi,
        }), dev);
    }

    [Fact]
    public async Task Connect_writes_configured_dpi()
    {
        var (mouse, dev) = Create(1600, 1000);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => dev.CountCalls(Dpi, 3) == 1);
            var set = Assert.Single(dev.Calls, c => c.Feature == Dpi && c.Function == 3);
            Assert.Equal(new byte[] { 0, 0x06, 0x40 }, set.Params[..3]);
        }
    }

    [Fact]
    public async Task Same_dpi_or_null_writes_nothing()
    {
        var (same, devSame) = Create(1000, 1000);
        var (none, devNone) = Create(null, 1000);
        using (same)
        using (none)
        {
            await same.StartAsync(CancellationToken.None);
            await none.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => devSame.CountCalls(Dpi, 2) == 1 && none.IsConnected);
            Assert.Equal(0, devSame.CountCalls(Dpi, 3));
            Assert.Equal(0, devNone.CountCalls(Dpi, 2) + devNone.CountCalls(Dpi, 3));
        }
    }

    [Fact]
    public async Task GetDpi_returns_the_device_value_when_connected()
    {
        var (mouse, _) = Create(null, 1600);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.Equal(1600, await mouse.GetDpiAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task GetDpi_is_null_when_not_connected()
    {
        var (mouse, dev) = Create(null, 1600);
        using (mouse)
        {
            Assert.False(mouse.IsConnected);
            Assert.Null(await mouse.GetDpiAsync(CancellationToken.None));
            Assert.Equal(0, dev.CountCalls(Dpi, 2));
        }
    }

    [Fact]
    public async Task GetDpi_is_null_when_the_device_reports_zero()
    {
        var (mouse, _) = Create(null, 0);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.Null(await mouse.GetDpiAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task GetDpi_is_null_when_the_read_fails_with_a_HID_error()
    {
        var dev = new FakeDevice();
        dev.Features[FeatureId.AdjustableDpi] = Dpi;
        var transport = dev.NewTransport();
        transport.OnWrite = w =>
        {
            if (w[2] == Dpi && (w[3] >> 4) == 2)
            {
                var error = new byte[HidppMessage.LongLength];
                error[0] = HidppMessage.LongReportId;
                error[1] = w[1];
                error[2] = HidppMessage.Hidpp2ErrorFeature;
                error[3] = Dpi;
                error[4] = w[3];
                error[5] = 2; // invalid argument
                return [error];
            }
            return dev.Handle(w);
        };
        var factory = new FakeFactory();
        factory.Enqueue(transport);
        var o = Wait.FastOptions();
        using var mouse = new LogiMouse(factory, o);
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        Assert.Null(await mouse.GetDpiAsync(CancellationToken.None));
    }
}
