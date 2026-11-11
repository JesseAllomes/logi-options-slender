using Slender.Hidpp;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class BadgeModelTests
{
    private static BadgeInput Dev(int percent, bool charging = false, ChargeState? state = null, bool connected = true, int threshold = 15) =>
        new(connected, new BatteryStatus(percent, charging, state ?? (charging ? ChargeState.Charging : ChargeState.Discharging)), threshold);

    private static readonly BadgeInput Absent = new(false, null, 15);

    [Fact]
    public void Healthy_discharging_devices_show_nothing() =>
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(Dev(80), Dev(60, threshold: 10), true));

    [Fact]
    public void Charging_device_shows_charging() =>
        Assert.Equal(BadgeKind.Charging, BadgeModel.Decide(Dev(40, charging: true), Absent, true));

    [Fact]
    public void Full_on_charger_shows_static_full() =>
        Assert.Equal(BadgeKind.Full, BadgeModel.Decide(Dev(100, charging: true, state: ChargeState.Full), Absent, true));

    [Fact]
    public void Low_beats_charging_on_the_other_device() =>
        Assert.Equal(BadgeKind.Low, BadgeModel.Decide(Dev(40, charging: true), Dev(8, threshold: 10), true));

    [Fact]
    public void Charging_beats_full() =>
        Assert.Equal(BadgeKind.Charging, BadgeModel.Decide(Dev(100, true, ChargeState.Full), Dev(50, charging: true, threshold: 10), true));

    [Theory]
    [InlineData(16, "None")]
    [InlineData(15, "Low")]
    [InlineData(3, "Low")]
    public void Threshold_is_at_or_under(int percent, string expected) =>
        Assert.Equal(Enum.Parse<BadgeKind>(expected), BadgeModel.Decide(Dev(percent), Absent, true));

    [Fact]
    public void Each_device_uses_its_own_threshold()
    {
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(Absent, Dev(12, threshold: 10), true));
        Assert.Equal(BadgeKind.Low, BadgeModel.Decide(Absent, Dev(10, threshold: 10), true));
    }

    [Fact]
    public void Low_device_on_charger_is_charging_not_low() =>
        Assert.Equal(BadgeKind.Charging, BadgeModel.Decide(Dev(5, charging: true), Absent, true));

    [Fact]
    public void Disconnected_or_unread_devices_show_nothing()
    {
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(Dev(3, connected: false), Dev(40, charging: true, connected: false), true));
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(new BadgeInput(true, null, 15), Absent, true));
    }

    [Fact]
    public void Alerts_off_hides_low_but_not_charging()
    {
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(Dev(3), Absent, false));
        Assert.Equal(BadgeKind.Charging, BadgeModel.Decide(Dev(3, charging: true), Absent, false));
    }

    [Fact]
    public void Error_state_shows_no_badge()
    {
        var error = new BadgeInput(true, new BatteryStatus(3, false, ChargeState.Error), 15);
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(error, Absent, true));
        Assert.Equal(BadgeKind.None, BadgeModel.Decide(new BadgeInput(true, new BatteryStatus(50, true, ChargeState.Error), 15), Absent, true));
    }

    [Fact]
    public void Error_on_one_device_does_not_hide_the_other()
    {
        var error = new BadgeInput(true, new BatteryStatus(3, false, ChargeState.Error), 15);
        Assert.Equal(BadgeKind.Charging, BadgeModel.Decide(error, Dev(50, charging: true), true));
    }

    [Fact]
    public void Only_charging_and_low_animate_and_only_with_windows_animations()
    {
        Assert.True(BadgeTiming.NeedsTimer(BadgeKind.Charging, true));
        Assert.True(BadgeTiming.NeedsTimer(BadgeKind.Low, true));
        Assert.False(BadgeTiming.NeedsTimer(BadgeKind.Full, true));
        Assert.False(BadgeTiming.NeedsTimer(BadgeKind.None, true));
        Assert.False(BadgeTiming.NeedsTimer(BadgeKind.Charging, false));
        Assert.False(BadgeTiming.NeedsTimer(BadgeKind.Low, false));
    }

    [Fact]
    public void Reduced_motion_and_static_kinds_use_one_frame()
    {
        Assert.Equal(1, BadgeTiming.FrameCount(BadgeKind.Charging, false));
        Assert.Equal(1, BadgeTiming.FrameCount(BadgeKind.Low, false));
        Assert.Equal(1, BadgeTiming.FrameCount(BadgeKind.Full, true));
        Assert.Equal(0, BadgeTiming.FrameCount(BadgeKind.None, true));
        Assert.True(BadgeTiming.FrameCount(BadgeKind.Charging, true) > 10);
        Assert.True(BadgeTiming.FrameCount(BadgeKind.Low, true) > 10);
    }

    [Theory]
    [InlineData("Charging")]
    [InlineData("Low")]
    public void Frame_index_walks_the_loop_and_wraps(string name)
    {
        var kind = Enum.Parse<BadgeKind>(name);
        int count = BadgeTiming.FrameCount(kind, true), period = BadgeTiming.PeriodMs(kind);
        Assert.Equal(0, BadgeTiming.FrameIndex(kind, true, 0));
        Assert.Equal(0, BadgeTiming.FrameIndex(kind, true, period));
        Assert.Equal(count - 1, BadgeTiming.FrameIndex(kind, true, period - 1));
        Assert.Equal(BadgeTiming.FrameIndex(kind, true, 700), BadgeTiming.FrameIndex(kind, true, 700 + 3 * period));
        int prev = 0;
        for (int t = 0; t < period; t += 10)
        {
            int i = BadgeTiming.FrameIndex(kind, true, t);
            Assert.InRange(i, prev, prev + 1);
            prev = i;
        }
    }

    [Fact]
    public void Static_frame_index_is_always_zero()
    {
        Assert.Equal(0, BadgeTiming.FrameIndex(BadgeKind.Charging, false, 12345));
        Assert.Equal(0, BadgeTiming.FrameIndex(BadgeKind.Full, true, 12345));
        Assert.Equal(0, BadgeTiming.FrameIndex(BadgeKind.Low, true, -5));
    }

    [Fact]
    public void Frame_rate_is_about_twelve_per_second()
    {
        Assert.InRange(1000.0 / BadgeTiming.TimerIntervalMs, 11, 13);
        Assert.InRange(BadgeTiming.PeriodMs(BadgeKind.Low), 1550, 1650);
    }
}
