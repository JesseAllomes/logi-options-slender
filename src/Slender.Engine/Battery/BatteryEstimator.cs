namespace Slender.Engine.Battery;

/// <summary>One battery reading with its time.</summary>
public readonly record struct BatterySample(DateTimeOffset At, int Percent, bool Charging);

public enum BatteryEstimateKind { NotEnoughData, Charging, Estimate }

/// <summary>What the battery history says. Text is the sentence for Settings; Short fits the tray tooltip.</summary>
public sealed record BatteryEstimate(BatteryEstimateKind Kind, double? DaysLeft, string Text, string Short)
{
    public static BatteryEstimate NotEnough { get; } = new(BatteryEstimateKind.NotEnoughData, null, "Not enough data yet", "");
}

/// <summary>
/// Days left from the discharge slope. Only the newest discharge counts: a charging reading, or a rise of two points or more, starts
/// a new one. Readings can be as coarse as four levels (the ERGO K860), so the fit uses the moments the level was seen to drop
/// (the exact time a level starts is known to within one poll, a level held since before the first reading is not), and a drop
/// whose previous reading is hours old is skipped because its time is unknown. It needs two such drops, at least 12 hours
/// apart and 2 points down, before it says anything.
/// </summary>
public static class BatteryEstimator
{
    public static readonly TimeSpan MinSpan = TimeSpan.FromHours(12);
    public const int MinDrop = 2;

    /// <summary>A drop seen more than this long after the previous reading has an unknown time and is skipped.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromHours(3);
    public const int MaxDays = 365;

    public static BatteryEstimate Estimate(IReadOnlyList<BatterySample> samples)
    {
        if (samples.Count == 0) return BatteryEstimate.NotEnough;
        var ordered = samples.OrderBy(s => s.At).ToList();
        BatterySample last = ordered[^1];
        if (last.Charging) return new BatteryEstimate(BatteryEstimateKind.Charging, null, "Charging", "charging");

        // Start of the newest discharge: after the last charging reading or the last real rise.
        int start = 0;
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].Charging || ordered[i].Percent >= ordered[i - 1].Percent + 2) start = i;
        }

        var points = new List<(double Days, int Percent)>();
        int level = ordered[start].Percent;
        DateTimeOffset origin = ordered[start].At;
        for (int i = start + 1; i < ordered.Count; i++)
        {
            BatterySample s = ordered[i];
            if (s.Percent >= level) continue; // flat, or a one point wobble upward
            if (s.At - ordered[i - 1].At <= MaxGap) points.Add(((s.At - origin).TotalDays, s.Percent));
            level = s.Percent;
        }

        if (points.Count < 2) return BatteryEstimate.NotEnough;
        if (TimeSpan.FromDays(points[^1].Days - points[0].Days) < MinSpan || points[0].Percent - points[^1].Percent < MinDrop) return BatteryEstimate.NotEnough;

        double meanT = points.Average(p => p.Days), meanP = points.Average(p => (double)p.Percent);
        double sxx = points.Sum(p => (p.Days - meanT) * (p.Days - meanT));
        if (sxx <= 0) return BatteryEstimate.NotEnough;
        double slope = points.Sum(p => (p.Days - meanT) * (p.Percent - meanP)) / sxx; // percent per day, negative
        if (slope >= 0) return BatteryEstimate.NotEnough;
        return Describe(last.Percent / -slope);
    }

    public static BatteryEstimate Describe(double days)
    {
        if (days > MaxDays) return new BatteryEstimate(BatteryEstimateKind.Estimate, days, "Over a year left", ">1y");
        if (days < 1)
        {
            int hours = Math.Max(1, (int)Math.Round(days * 24));
            return new BatteryEstimate(BatteryEstimateKind.Estimate, days, $"About {hours} {(hours == 1 ? "hour" : "hours")} left", $"~{hours}h");
        }

        int d = (int)Math.Round(days);
        return new BatteryEstimate(BatteryEstimateKind.Estimate, days, $"About {d} {(d == 1 ? "day" : "days")} left", $"~{d}d");
    }
}
