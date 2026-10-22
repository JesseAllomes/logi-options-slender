using Slender.Engine.Devices;
using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class WakeTests
{
    private static LogiMouseOptions Slow() => new()
    {
        BackoffInitial = TimeSpan.FromSeconds(30),
        BackoffMax = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public async Task Wake_ends_backoff_wait_and_retries_now()
    {
        var f = new FakeFactory(); // always fails to open
        using var mouse = new LogiMouse(f, Slow());
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => f.OpenCalls == 1);
        await Task.Delay(200);
        Assert.Equal(1, f.OpenCalls); // waiting out the 30 s backoff

        // The loop may not have reached the delay yet; wake until it retries.
        await Wait.UntilAsync(() => { mouse.Wake(); return f.OpenCalls >= 2; }, 5000);
    }

    [Fact]
    public void Wake_is_noop_when_not_started()
    {
        using var mouse = new LogiMouse(new FakeFactory(), Slow());
        mouse.Wake();
    }

    [Fact]
    public async Task Module_forwards_link_state_and_wake()
    {
        var f = new FakeFactory();
        using var mouse = new LogiMouse(f, Slow());
        var reports = new List<(string, bool)>();
        using var module = new LogiDeviceModule("mx-master-4", mouse, (id, up) => { lock (reports) reports.Add((id, up)); });
        Assert.Equal(("mx-master-4", false), reports[0]);

        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => f.OpenCalls == 1);
        await Wait.UntilAsync(() => { module.Wake(); return f.OpenCalls >= 2; }, 5000);
        Assert.Equal("mx-master-4", module.DeviceId);
    }
}
