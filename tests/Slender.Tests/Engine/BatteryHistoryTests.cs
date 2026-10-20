using Slender.Engine.Battery;

namespace Slender.Tests.Engine;

public sealed class BatteryHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private string Path_ => Path.Combine(_dir, "battery-history.json");

    private BatteryHistory Create() => new(Path_, () => _now);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Readings_survive_a_restart()
    {
        var h = Create();
        Assert.True(h.Record("mouse", 80, false));
        _now = _now.AddMinutes(10);
        Assert.True(h.Record("mouse", 79, false));
        var again = Create().Samples("mouse");
        Assert.Equal([80, 79], again.Select(s => s.Percent));
        Assert.Equal(_now, again[^1].At);
    }

    [Fact]
    public void Throttle_keeps_changes_and_one_reading_an_hour_only()
    {
        var h = Create();
        Assert.True(h.Record("mouse", 80, false));
        _now = _now.AddSeconds(30);
        Assert.False(h.Record("mouse", 79, false)); // under a minute
        _now = _now.AddMinutes(5);
        Assert.False(h.Record("mouse", 80, false)); // same level, within the hour
        Assert.True(h.Record("mouse", 79, false)); // changed
        _now = _now.AddMinutes(10);
        Assert.True(h.Record("mouse", 79, true)); // charging changed
        _now = _now.AddMinutes(61);
        Assert.True(h.Record("mouse", 79, true)); // hourly heartbeat
        Assert.Equal(4, h.Samples("mouse").Count);
    }

    [Fact]
    public void Devices_are_kept_apart()
    {
        var h = Create();
        h.Record("mouse", 80, false);
        h.Record("keyboard", 50, false);
        Assert.Single(h.Samples("mouse"));
        Assert.Equal(50, h.Samples("keyboard")[0].Percent);
        Assert.Empty(h.Samples("other"));
    }

    [Fact]
    public void Readings_older_than_sixty_days_are_dropped_on_write_and_on_load()
    {
        var h = Create();
        h.Record("mouse", 90, false);
        _now = _now.AddDays(30);
        h.Record("mouse", 70, false);
        _now = _now.AddDays(31); // the first is now 61 days old
        h.Record("mouse", 50, false);
        Assert.Equal([70, 50], h.Samples("mouse").Select(s => s.Percent));
        _now = _now.AddDays(40); // the second is 71 days old when read back
        Assert.Equal([50], Create().Samples("mouse").Select(s => s.Percent));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"devices\": 5}")]
    [InlineData("{\"devices\":{\"mouse\":[{\"t\":99999999999999999,\"p\":50,\"c\":false}]}}")]
    [InlineData("")]
    [InlineData("{\"devices\":{\"mouse\":null}}")]
    public void A_corrupt_file_starts_empty_and_is_replaced_by_the_next_reading(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, text);
        var h = Create();
        Assert.Empty(h.Samples("mouse"));
        Assert.True(h.Record("mouse", 60, false));
        Assert.Equal([60], Create().Samples("mouse").Select(s => s.Percent));
    }

    [Fact]
    public void Bad_rows_in_an_otherwise_good_file_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        long t = _now.AddHours(-1).ToUnixTimeSeconds();
        File.WriteAllText(Path_, $"{{\"devices\":{{\"mouse\":[{{\"t\":{t},\"p\":250,\"c\":false}},{{\"t\":{t},\"p\":42,\"c\":false}},null]}}}}");
        Assert.Equal([42], Create().Samples("mouse").Select(s => s.Percent));
    }

    [Fact]
    public void An_unwritable_location_never_throws()
    {
        Directory.CreateDirectory(_dir);
        string blocked = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocked, "x"); // a file where the folder should be
        var h = new BatteryHistory(Path.Combine(blocked, "battery-history.json"), () => _now);
        Assert.True(h.Record("mouse", 80, false)); // kept in memory
        Assert.Single(h.Samples("mouse"));
    }

    [Fact]
    public void The_estimate_comes_from_the_recorded_readings()
    {
        var h = Create();
        for (int hour = 0; hour <= 120; hour++) // an hourly reading, losing a point every 12 hours
        {
            h.Record("mouse", 80 - hour / 12, false);
            _now = _now.AddHours(1);
        }

        Assert.Equal(BatteryEstimateKind.Estimate, h.Estimate("mouse").Kind);
        Assert.Equal(BatteryEstimateKind.NotEnoughData, h.Estimate("keyboard").Kind);
    }
}
