using Slender.Engine;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class TrayStateTests
{
    [Fact]
    public void Status_includes_keyboard_connection_and_battery()
    {
        var written = new List<SlenderStatus>();
        using var publisher = new StatusPublisher(
            () => new StatusSnapshot(true, 80, false, null, KeyboardConnected: true, KeyboardBattery: 100),
            write: written.Add, optionsPlusRunning: () => false);

        publisher.PublishNow();

        DeviceState kb = written.Single().Devices.Single(d => d.Id == "ergo-k860");
        Assert.True(kb.Connected);
        Assert.Equal(100, kb.BatteryPercent);
    }

    [Fact]
    public void Disconnected_keyboard_hides_stale_battery()
    {
        var written = new List<SlenderStatus>();
        using var publisher = new StatusPublisher(
            () => new StatusSnapshot(true, 80, false, null, KeyboardConnected: false, KeyboardBattery: 50),
            write: written.Add, optionsPlusRunning: () => false);

        publisher.PublishNow();

        DeviceState kb = written.Single().Devices.Single(d => d.Id == "ergo-k860");
        Assert.False(kb.Connected);
        Assert.Null(kb.BatteryPercent);
    }

    [Fact]
    public void Paused_snapshot_reports_options_plus_running_even_if_cache_says_no()
    {
        var written = new List<SlenderStatus>();
        using var publisher = new StatusPublisher(
            () => new StatusSnapshot(false, null, false, null, PausedForOptionsPlus: true),
            write: written.Add, optionsPlusRunning: () => false);

        publisher.PublishNow();

        Assert.True(written.Single().OptionsPlusRunning);
    }

    [Fact]
    public void Guard_pauses_while_options_plus_runs_then_starts_engines_once()
    {
        bool running = true;
        int starts = 0;
        var guard = new OptionsPlusGuard(() => running, () => starts++);

        Assert.True(guard.Evaluate());
        Assert.True(guard.Evaluate());
        Assert.True(guard.Paused);
        Assert.Equal(0, starts);

        running = false;
        Assert.False(guard.Evaluate());
        Assert.False(guard.Paused);
        Assert.True(guard.Started);
        Assert.Equal(1, starts);

        // Options+ coming back later does not stop or restart the engines.
        running = true;
        Assert.False(guard.Evaluate());
        Assert.Equal(1, starts);
    }

    [Fact]
    public void Guard_starts_immediately_when_options_plus_absent()
    {
        int starts = 0;
        var guard = new OptionsPlusGuard(() => false, () => starts++);

        Assert.False(guard.Evaluate());
        Assert.Equal(1, starts);
    }

    [Theory]
    [InlineData(false, null, false, "ERGO K860 — not connected")]
    [InlineData(true, null, false, "ERGO K860 — connected")]
    [InlineData(true, 100, false, "ERGO K860 — connected, 100%")]
    [InlineData(true, 42, true, "ERGO K860 — connected, 42% charging")]
    [InlineData(false, 90, false, "ERGO K860 — not connected")]
    public void Device_line_formats(bool connected, int? percent, bool charging, string expected) =>
        Assert.Equal(expected, TrayText.Device("ERGO K860", connected, percent, charging));

    [Fact]
    public void Paused_line_text() =>
        Assert.Equal("Paused — Logi Options+ is running", TrayText.Paused);
}
