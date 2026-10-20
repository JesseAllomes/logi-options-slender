using Slender.Engine.Battery;

namespace Slender.Tests.Engine;

public class BatteryEstimatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static BatterySample S(double hours, int percent, bool charging = false) => new(T0.AddHours(hours), percent, charging);

    /// <summary>
    /// What the history really holds: a reading every hour (the heartbeat) with the level held between changes, plus a reading at
    /// the moment of each change. Each change is (hour, percent).
    /// </summary>
    private static List<BatterySample> Hourly(int startPercent, double endHour, params (double Hour, int Percent)[] changes)
    {
        var list = new List<BatterySample>();
        for (double h = 0; h <= endHour; h++)
        {
            int level = startPercent;
            foreach (var c in changes) if (c.Hour <= h) level = c.Percent;
            list.Add(S(h, level));
        }

        foreach (var c in changes) list.Add(S(c.Hour, c.Percent));
        return list.OrderBy(x => x.At).ToList();
    }

    /// <summary>A fine-grained discharge: 1 point every 12 hours.</summary>
    private static List<BatterySample> Steady(int startPercent, int points) =>
        Hourly(startPercent, points * 12, Enumerable.Range(1, points).Select(i => (i * 12.0 - 0.5, startPercent - i)).ToArray());

    [Fact]
    public void No_readings_is_not_enough_data()
    {
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate([]).Kind);
        Assert.Equal("Not enough data yet", BatteryEstimator.Estimate([S(0, 90)]).Text);
    }

    [Fact]
    public void A_single_drop_or_a_short_span_is_not_enough_data()
    {
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate([S(0, 90), S(5, 89)]).Kind);
        // two drops, but only 2 hours apart
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate([S(0, 90), S(1, 89), S(2, 88), S(3, 87)]).Kind);
    }

    [Fact]
    public void A_steady_discharge_gives_days_left_from_its_slope()
    {
        // 2 points a day from 80 to 70 after five days: 70 / 2 = 35 days
        var samples = Steady(80, 10);
        BatteryEstimate e = BatteryEstimator.Estimate(samples);
        Assert.Equal(BatteryEstimateKind.Estimate, e.Kind);
        Assert.Equal(35, e.DaysLeft!.Value, 1);
        Assert.Equal("About 35 days left", e.Text);
        Assert.Equal("~35d", e.Short);
    }

    [Fact]
    public void Charging_now_says_charging()
    {
        var samples = Steady(80, 10);
        samples.Add(S(200, 71, charging: true));
        Assert.Equal(BatteryEstimateKind.Charging, BatteryEstimator.Estimate(samples).Kind);
    }

    [Fact]
    public void Only_the_discharge_after_the_last_charge_counts()
    {
        // an earlier, much faster discharge, a charge, then a slow one
        var samples = new List<BatterySample> { S(0, 100), S(1, 90), S(2, 80), S(3, 70), S(4, 60), S(5, 45), S(6, 45, true), S(7, 70, true), S(8, 100, true) };
        samples.AddRange(Hourly(100, 8 * 24, Enumerable.Range(1, 8).Select(i => (i * 24.0 - 0.5, 100 - i)).ToArray()).Select(x => x with { At = x.At.AddHours(9) })); // 1 point a day after the charge
        BatteryEstimate e = BatteryEstimator.Estimate(samples);
        Assert.Equal(BatteryEstimateKind.Estimate, e.Kind);
        Assert.InRange(e.DaysLeft!.Value, 85, 95); // 92 left at 1 point a day
    }

    [Fact]
    public void A_rise_without_a_charging_flag_also_starts_a_new_discharge()
    {
        var samples = Hourly(50, 24, (12, 49), (24, 48));
        samples.Add(S(25, 90));
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate(samples).Kind); // new discharge has no drops yet
        samples.AddRange(Hourly(90, 72, (24, 89), (48, 88), (72, 87)).Skip(1).Select(x => x with { At = x.At.AddHours(25) })); // 1 point a day from 90
        Assert.InRange(BatteryEstimator.Estimate(samples).DaysLeft!.Value, 80, 95);
    }

    [Fact]
    public void A_one_point_wobble_up_is_not_a_charge()
    {
        var samples = Hourly(80, 48, (11.5, 79), (23.5, 78), (30, 79), (35.5, 77), (47.5, 76));
        BatteryEstimate e = BatteryEstimator.Estimate(samples);
        Assert.Equal(BatteryEstimateKind.Estimate, e.Kind);
    }

    [Fact]
    public void Four_coarse_levels_wait_for_two_drops_then_estimate()
    {
        // The K860 reports 100, 50, 30, 10. Polled every 10 minutes, the level is seen to drop at the moment it does.
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate(Hourly(100, 100)).Kind);
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate(Hourly(100, 110, (100.2, 50))).Kind); // one drop has no slope
        // 20 points in 10 days = 2 per day
        BatteryEstimate e = BatteryEstimator.Estimate(Hourly(100, 100.2 + 24 * 10 + 5, (100.2, 50), (100.2 + 24 * 10, 30)));
        Assert.Equal(BatteryEstimateKind.Estimate, e.Kind);
        Assert.Equal(15, e.DaysLeft!.Value, 1); // 30 left at 2 a day
    }

    [Fact]
    public void A_drop_seen_after_a_long_gap_has_an_unknown_time_and_is_skipped()
    {
        // laptop shut for two days: the drop to 50 shows up late and must not be used as a timing point
        var samples = new List<BatterySample> { S(0, 100), S(1, 100), S(49, 50) }; // the 48 hour gap hides when 100 became 50
        samples.AddRange(new[] { S(50, 50), S(299, 50), S(299.9, 30), S(300, 30), S(399, 30), S(399.9, 10), S(400, 10) });
        BatteryEstimate e = BatteryEstimator.Estimate(samples);
        Assert.Equal(BatteryEstimateKind.Estimate, e.Kind);
        Assert.InRange(e.DaysLeft!.Value, 1.5, 2.5); // 10 left at 20 points per 100 hours = 4.8 per day
    }

    [Fact]
    public void An_empty_slope_or_a_flat_battery_is_not_enough_data()
    {
        var flat = Enumerable.Range(0, 30).Select(i => S(i * 24, 80)).ToList();
        Assert.Equal(BatteryEstimateKind.NotEnoughData, BatteryEstimator.Estimate(flat).Kind);
    }

    [Theory]
    [InlineData(0.4, "About 10 hours left", "~10h")]
    [InlineData(1.0, "About 1 day left", "~1d")]
    [InlineData(12.4, "About 12 days left", "~12d")]
    [InlineData(400, "Over a year left", ">1y")]
    public void Wording(double days, string text, string tip)
    {
        BatteryEstimate e = BatteryEstimator.Describe(days);
        Assert.Equal(text, e.Text);
        Assert.Equal(tip, e.Short);
    }
}
