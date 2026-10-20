using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;

namespace Slender.Tests.Notify;

public sealed class NotifyHapticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ---- toast names ----

    [Theory]
    [InlineData("Outlook\r\nNew mail\r\nHello", "Outlook")]
    [InlineData("New notification\nSlack\nmessage", "Slack")]
    [InlineData("  Teams  ", "Teams")]
    [InlineData("", ToastNames.Unknown)]
    [InlineData(null, ToastNames.Unknown)]
    [InlineData("New notification", ToastNames.Unknown)]
    [InlineData("\r\n \r\n", ToastNames.Unknown)]
    public void App_name_is_first_real_line(string? raw, string expected) =>
        Assert.Equal(expected, ToastNames.AppFromAccessibleName(raw));

    [Fact]
    public void App_name_is_capped_and_control_chars_removed()
    {
        string name = ToastNames.AppFromAccessibleName("A\tB" + new string('x', 200));
        Assert.Equal(NotifyHapticSettings.MaxAppNameLength, name.Length);
        Assert.DoesNotContain('\t', name);
    }

    // ---- rate limit ----

    [Fact]
    public void Same_app_is_limited_per_interval_and_other_apps_are_not()
    {
        long now = 0;
        var limiter = new NotifyRateLimiter(5000, 10, 30000, () => now);
        Assert.True(limiter.TryAcquire("Slack"));
        Assert.False(limiter.TryAcquire("slack"));
        Assert.True(limiter.TryAcquire("Teams"));
        now = 5000;
        Assert.True(limiter.TryAcquire("Slack"));
    }

    [Fact]
    public void Global_cap_stops_a_burst_across_apps_then_recovers()
    {
        long now = 0;
        var limiter = new NotifyRateLimiter(1000, 3, 10000, () => now);
        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("b"));
        Assert.True(limiter.TryAcquire("c"));
        Assert.False(limiter.TryAcquire("d"));
        now = 10000;
        Assert.True(limiter.TryAcquire("d"));
    }

    // ---- seen apps ----

    [Fact]
    public void Seen_apps_record_count_and_times_and_reload()
    {
        var store = new SeenAppsStore(_dir);
        var t1 = new DateTimeOffset(2026, 10, 20, 1, 0, 0, TimeSpan.Zero);
        Assert.True(store.Record("Outlook", t1));
        Assert.False(store.Record("outlook", t1.AddMinutes(5)));
        var apps = SeenAppsStore.Read(_dir);
        var one = Assert.Single(apps);
        Assert.Equal(2, one.Count);
        Assert.Equal(t1.UtcTicks, one.FirstTicks);
        Assert.Equal(t1.AddMinutes(5).UtcTicks, one.LastTicks);
        Assert.False(new SeenAppsStore(_dir).Record("Outlook", t1.AddMinutes(9))); // a restarted tray carries on
        Assert.Equal(3, SeenAppsStore.Read(_dir)[0].Count);
    }

    [Fact]
    public void Seen_apps_are_capped_dropping_the_least_recently_seen()
    {
        var store = new SeenAppsStore(_dir);
        var t = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < SeenAppsStore.Cap + 5; i++) store.Record("app" + i, t.AddSeconds(i));
        var apps = SeenAppsStore.Read(_dir);
        Assert.Equal(SeenAppsStore.Cap, apps.Count);
        Assert.DoesNotContain(apps, a => a.Name == "app0");
        Assert.Contains(apps, a => a.Name == "app" + (SeenAppsStore.Cap + 4));
    }

    [Fact]
    public void Seen_apps_missing_or_corrupt_file_is_survivable()
    {
        Assert.Empty(SeenAppsStore.Read(_dir));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SeenAppsStore.FileName), "garbage\nGood\t5\t6\t2\nbad\tx\ty\tz\n\t1\t2\t3\n\u0000\u0000\u0000");
        var apps = SeenAppsStore.Read(_dir);
        Assert.Equal("Good", Assert.Single(apps).Name);
        Assert.True(new SeenAppsStore(_dir).Record("New", DateTimeOffset.UtcNow)); // and writing over it works
        Assert.Equal(2, SeenAppsStore.Read(_dir).Count);
    }

    // ---- config ----

    [Fact]
    public void Defaults_are_off_and_apps_fall_back_to_the_default_cue()
    {
        var n = new HapticSettings().Notify;
        Assert.False(n.Enabled);
        Assert.Null(n.Effective("Outlook"));
        var on = n with { Enabled = true };
        Assert.Equal(n.Default, on.Effective("Outlook"));
        var muted = on with { Apps = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["Spam"] = new(false, 3) } };
        Assert.Null(muted.Effective("spam"));
        Assert.NotNull(muted.Effective("Other"));
    }

    [Fact]
    public void Config_round_trips_and_default_config_writes_nothing_extra()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default);
        Assert.Contains("\"notify\": null", File.ReadAllText(Path.Combine(_dir, "config.json"))); // nothing but null for the default

        var notify = new NotifyHapticSettings
        {
            Enabled = true,
            Default = new HapticCue(true, 7, 2, 200),
            Apps = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["Slack"] = new(true, 9, 3, 90, 40), ["Muted"] = new(false, 1) },
        };
        store.Save(SlenderConfig.Default with { Haptics = new HapticSettings { Notify = notify } });
        var loaded = store.Load().Haptics.Notify;
        Assert.Equal(notify, loaded);
        Assert.Equal(new HapticCue(true, 9, 3, 90, 40), loaded.Apps["slack"]);
    }

    [Fact]
    public void Config_garbage_is_clamped_or_dropped()
    {
        Directory.CreateDirectory(_dir);
        string longName = new('n', 100);
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            "{\"version\":1,\"profiles\":[],\"haptics\":{\"notify\":{\"enabled\":true,\"default\":{\"pattern\":99,\"pulses\":-4},\"apps\":{\"\":{\"pattern\":1},\"  \":{\"pattern\":1},\"" + longName
            + "\":{\"pattern\":1},\"Ok\":{\"pattern\":500,\"spacingMs\":1},\"Null\":null}}}}");
        var n = new JsonConfigStore(_dir).Load().Haptics.Notify;
        Assert.True(n.Enabled);
        Assert.Equal(HapticCue.PatternMax, n.Default.Pattern);
        Assert.Equal(1, n.Default.Pulses);
        var ok = Assert.Single(n.Apps);
        Assert.Equal("Ok", ok.Key);
        Assert.Equal(HapticCue.PatternMax, ok.Value.Pattern);
        Assert.Equal(HapticCue.SpacingMin, ok.Value.SpacingMs);
    }

    // ---- settings ops ----

    [Fact]
    public void Rows_merge_seen_and_configured_apps()
    {
        var settings = new NotifyHapticSettings { Apps = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["Zed"] = new(false), ["Old"] = new(true, 3) } };
        var seen = new[] { new SeenApp("Old", 100, 200, 4), new SeenApp("New", 100, 300, 1) };
        var rows = NotifyOps.Rows(seen, settings);
        Assert.Equal(["New", "Old", "Zed"], rows.Select(r => r.Name));
        Assert.Null(rows[2].LastSeen);
        Assert.Equal(4, rows[1].Count);
    }

    [Fact]
    public void App_cue_equal_to_default_drops_the_override()
    {
        var s = new HapticSettings();
        s = NotifyOps.WithAppCue(s, "Slack", new HapticCue(true, 9));
        Assert.Equal(9, s.Notify.CueFor("Slack").Pattern);
        s = NotifyOps.WithAppCue(s, "Slack", s.Notify.Default);
        Assert.Empty(s.Notify.Apps);
        s = NotifyOps.WithEnabled(NotifyOps.WithDefault(s, new HapticCue(true, 2, 99)), true);
        Assert.True(s.Notify.Enabled);
        Assert.Equal(HapticCue.PulsesMax, s.Notify.Default.Pulses);
    }
}
