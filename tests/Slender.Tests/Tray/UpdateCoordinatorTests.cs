using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class UpdateCoordinatorTests
{
    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsAvailable { get; set; } = true;
        public string? CurrentVersion { get; set; } = "1.1.0";
        public string? Found;
        public Exception? CheckFails;
        public TaskCompletionSource<string?>? CheckGate;
        public TaskCompletionSource? DownloadGate;
        public int Checks, Downloads, ApplyOnExits, ApplyRestarts;
        public Exception? ApplyFails;

        public async Task<string?> CheckAsync(CancellationToken cancel)
        {
            Checks++;
            if (CheckGate is not null)
            {
                return await CheckGate.Task;
            }

            return CheckFails is null ? Found : throw CheckFails;
        }

        public async Task DownloadAsync(CancellationToken cancel)
        {
            Downloads++;
            if (DownloadGate is not null)
            {
                await DownloadGate.Task;
            }
        }

        public void ApplyOnExit() => ApplyOnExits++;

        public void ApplyAndRestart()
        {
            ApplyRestarts++;
            if (ApplyFails is not null)
            {
                throw ApplyFails;
            }
        }
    }

    private sealed class FakeSettings : ISettingsCloser
    {
        public bool Running;
        public bool Closes = true;
        public int CloseCalls;
        public TimeSpan? Timeout;

        public bool IsRunning() => Running;

        public Task<bool> CloseAsync(TimeSpan timeout)
        {
            CloseCalls++;
            Timeout = timeout;
            if (Closes)
            {
                Running = false;
            }

            return Task.FromResult(Closes);
        }
    }

    private sealed class CountingLog : ILog
    {
        public int Errors;
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) => Errors++;
    }

    private sealed class Harness
    {
        public FakeBackend Backend = new();
        public FakeSettings Settings = new();
        public CountingLog Log = new();
        public List<(UpdateNoticeKind Kind, string Text)> Notices = [];
        public int Changes, Applying, ApplyFailed;
        public List<string> Order = [];
        public List<TimeSpan> Delays = [];
        public List<Action> Timers = [];

        public UpdateCoordinator Create() => new(Backend, Settings, Log, (k, t) => { Order.Add("notice:" + t); Notices.Add((k, t)); }, () => Changes++, () => Applying++,
            () => { Order.Add("applyFailed"); ApplyFailed++; },
            (delay, run) =>
            {
                Delays.Add(delay);
                Timers.Add(run);
                return new Noop();
            });

        /// <summary>Fires the most recent timer, as the clock would.</summary>
        public void Fire() => Timers[^1]();
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    [Fact]
    public void Start_schedules_first_check_after_30_seconds()
    {
        var h = new Harness();
        using UpdateCoordinator c = h.Create();
        c.Start();
        Assert.Equal([TimeSpan.FromSeconds(30)], h.Delays);
        Assert.Equal(0, h.Backend.Checks);
    }

    [Fact]
    public void Start_schedules_nothing_without_an_update_source()
    {
        var h = new Harness();
        h.Backend.IsAvailable = false;
        using UpdateCoordinator c = h.Create();
        c.Start();
        Assert.Empty(h.Delays);
    }

    [Fact]
    public async Task Scheduled_check_rearms_every_6_hours_and_stays_quiet_when_up_to_date()
    {
        var h = new Harness();
        using UpdateCoordinator c = h.Create();
        c.Start();
        h.Fire();
        await WaitUntil(() => h.Delays.Count == 2);
        Assert.Equal(TimeSpan.FromHours(6), h.Delays[1]);
        Assert.Equal(1, h.Backend.Checks);
        Assert.Empty(h.Notices);
        Assert.False(c.UpdateReady);
    }

    [Fact]
    public async Task Found_update_downloads_then_is_ready_with_a_balloon()
    {
        var h = new Harness { Backend = { Found = "1.1.1" } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        Assert.Equal(1, h.Backend.Downloads);
        Assert.True(c.UpdateReady);
        Assert.Equal("1.1.1", c.AvailableVersion);
        Assert.Equal("1.1.0", c.CurrentVersion);
        Assert.Equal([(UpdateNoticeKind.Ready, "Slender 1.1.1 is ready. Click to restart and update.")], h.Notices);
        Assert.True(h.Changes >= 2); // found, then ready
        Assert.Equal(0, h.Backend.ApplyOnExits); // nothing is scheduled until the tray exits
    }

    [Fact]
    public async Task Scheduled_check_that_finds_an_update_also_announces_it()
    {
        var h = new Harness { Backend = { Found = "1.2.0" } };
        using UpdateCoordinator c = h.Create();
        c.Start();
        h.Fire();
        await WaitUntil(() => c.UpdateReady);
        Assert.Single(h.Notices);
    }

    [Fact]
    public async Task On_demand_check_says_up_to_date_only_when_asked()
    {
        var h = new Harness();
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        Assert.Equal([(UpdateNoticeKind.Info, "Slender is up to date")], h.Notices);
    }

    [Fact]
    public async Task Unreachable_source_is_logged_once_and_only_the_on_demand_check_tells_the_user()
    {
        var h = new Harness { Backend = { CheckFails = new IOException("gone") } };
        using UpdateCoordinator c = h.Create();
        c.Start();
        h.Fire();
        await WaitUntil(() => h.Delays.Count == 2);
        h.Fire();
        await WaitUntil(() => h.Delays.Count == 3);
        Assert.Equal(2, h.Backend.Checks);
        Assert.Equal(1, h.Log.Errors);
        Assert.Empty(h.Notices);

        await c.CheckNowAsync();
        Assert.Equal(1, h.Log.Errors);
        Assert.Equal([(UpdateNoticeKind.Info, "Couldn't reach the update folder")], h.Notices);
    }

    [Fact]
    public async Task A_check_after_a_failure_logs_again_once_the_source_has_worked()
    {
        var h = new Harness { Backend = { CheckFails = new IOException("gone") } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        h.Backend.CheckFails = null;
        await c.CheckNowAsync();
        h.Backend.CheckFails = new IOException("gone again");
        await c.CheckNowAsync();
        Assert.Equal(2, h.Log.Errors);
    }

    [Fact]
    public async Task Failed_download_clears_the_available_version()
    {
        var h = new Harness { Backend = { Found = "1.1.1", DownloadGate = new TaskCompletionSource() } };
        using UpdateCoordinator c = h.Create();
        Task check = c.CheckNowAsync();
        await WaitUntil(() => c.AvailableVersion == "1.1.1");
        Assert.False(c.UpdateReady);
        h.Backend.DownloadGate.SetException(new IOException("disk"));
        await check;
        Assert.Null(c.AvailableVersion);
        Assert.False(c.UpdateReady);
    }

    [Fact]
    public async Task Without_an_installed_source_on_demand_says_so_and_nothing_runs()
    {
        var h = new Harness { Backend = { IsAvailable = false } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        Assert.Equal(0, h.Backend.Checks);
        Assert.Equal(UpdateCoordinator.NotSetUpText, Assert.Single(h.Notices).Text);
    }

    [Fact]
    public async Task Only_one_check_or_download_runs_at_a_time()
    {
        var h = new Harness { Backend = { Found = "1.1.1", CheckGate = new TaskCompletionSource<string?>() } };
        using UpdateCoordinator c = h.Create();
        Task first = c.CheckNowAsync();
        await WaitUntil(() => h.Backend.Checks == 1);
        await c.CheckNowAsync(); // on demand while one is running
        c.Start();
        h.Fire(); // the schedule fires while one is running
        await WaitUntil(() => h.Delays.Count == 2);
        Assert.Equal(1, h.Backend.Checks);
        h.Backend.CheckGate.SetResult("1.1.1");
        await first;
        Assert.Equal(1, h.Backend.Downloads);
        // The on-demand check while busy says so; the scheduled one stays silent.
        Assert.Equal([(UpdateNoticeKind.Info, UpdateCoordinator.BusyText), (UpdateNoticeKind.Ready, "Slender 1.1.1 is ready. Click to restart and update.")], h.Notices);
    }

    [Fact]
    public async Task Once_ready_further_checks_do_not_hit_the_source_again()
    {
        var h = new Harness { Backend = { Found = "1.1.1" } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        await c.CheckNowAsync();
        Assert.Equal(1, h.Backend.Checks);
        Assert.Equal(2, h.Notices.Count);
        Assert.Equal(UpdateNoticeKind.Ready, h.Notices[1].Kind); // asking again re-shows the ready balloon
    }

    [Fact]
    public async Task Apply_does_nothing_until_an_update_is_ready()
    {
        var h = new Harness();
        using UpdateCoordinator c = h.Create();
        await c.ApplyAsync();
        Assert.Equal(0, h.Backend.ApplyRestarts);
    }

    [Fact]
    public async Task Apply_with_settings_closed_restarts_straight_away()
    {
        var h = new Harness { Backend = { Found = "1.1.1" } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        await c.ApplyAsync();
        Assert.Equal(0, h.Settings.CloseCalls);
        Assert.Equal(1, h.Applying);
        Assert.Equal(1, h.Backend.ApplyRestarts);
    }

    [Fact]
    public async Task Apply_with_settings_open_closes_it_first_within_5_seconds()
    {
        var h = new Harness { Backend = { Found = "1.1.1" }, Settings = { Running = true } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        await c.ApplyAsync();
        Assert.Equal(1, h.Settings.CloseCalls);
        Assert.Equal(TimeSpan.FromSeconds(5), h.Settings.Timeout);
        Assert.Equal(1, h.Backend.ApplyRestarts);
    }

    [Fact]
    public async Task Apply_tells_the_user_to_close_settings_when_it_will_not_close()
    {
        var h = new Harness { Backend = { Found = "1.1.1" }, Settings = { Running = true, Closes = false } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        h.Notices.Clear();
        await c.ApplyAsync();
        Assert.Equal(0, h.Backend.ApplyRestarts);
        Assert.Equal(0, h.Applying);
        Assert.Equal([(UpdateNoticeKind.Info, UpdateCoordinator.CloseSettingsText)], h.Notices);
        Assert.True(c.UpdateReady); // still ready: the user can try again

        h.Settings.Closes = true;
        await c.ApplyAsync();
        Assert.Equal(1, h.Backend.ApplyRestarts);
    }

    [Fact]
    public async Task Apply_failure_is_reported_and_leaves_the_update_ready()
    {
        var h = new Harness { Backend = { Found = "1.1.1", ApplyFails = new InvalidOperationException("no updater") } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        h.Notices.Clear();
        await c.ApplyAsync();
        Assert.Equal(UpdateCoordinator.ApplyFailedText, Assert.Single(h.Notices).Text);
        Assert.True(c.UpdateReady);
    }

    [Fact]
    public async Task Failed_restart_restores_the_icon_before_saying_so()
    {
        var h = new Harness { Backend = { Found = "1.1.1", ApplyFails = new InvalidOperationException("no updater") } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        h.Notices.Clear();
        h.Order.Clear();
        await c.ApplyAsync();
        Assert.Equal(1, h.Applying);
        Assert.Equal(1, h.ApplyFailed);
        Assert.Equal(["applyFailed", "notice:" + UpdateCoordinator.ApplyFailedText], h.Order);
    }

    [Fact]
    public async Task Exit_applies_a_ready_update_silently_and_nothing_otherwise()
    {
        var h = new Harness { Backend = { Found = "1.1.1" } };
        using UpdateCoordinator c = h.Create();
        c.ApplyOnExitIfReady();
        Assert.Equal(0, h.Backend.ApplyOnExits);
        await c.CheckNowAsync();
        c.ApplyOnExitIfReady();
        Assert.Equal(1, h.Backend.ApplyOnExits);
    }

    [Fact]
    public async Task Exit_apply_asks_settings_to_close_first()
    {
        var h = new Harness { Backend = { Found = "1.1.1" }, Settings = { Running = true } };
        using UpdateCoordinator c = h.Create();
        await c.CheckNowAsync();
        await c.ApplyOnExitIfReadyAsync();
        Assert.Equal(1, h.Settings.CloseCalls);
        Assert.Equal(1, h.Backend.ApplyOnExits);

        var h2 = new Harness { Backend = { Found = "1.1.1" } };
        using UpdateCoordinator c2 = h2.Create();
        await c2.CheckNowAsync();
        await c2.ApplyOnExitIfReadyAsync();
        Assert.Equal(0, h2.Settings.CloseCalls);
        Assert.Equal(1, h2.Backend.ApplyOnExits);
    }

    [Fact]
    public void Disposed_coordinator_does_not_rearm()
    {
        var h = new Harness();
        UpdateCoordinator c = h.Create();
        c.Start();
        c.Dispose();
        h.Fire();
        Assert.Single(h.Delays);
    }

    [Fact]
    public void Status_json_carries_the_update_fields_and_old_files_still_parse()
    {
        var status = new SlenderStatus([], false, null, DateTimeOffset.UnixEpoch, "1.1.0", "1.1.1", true);
        SlenderStatus? back = StatusJson.Parse(StatusJson.Serialize(status));
        Assert.Equal("1.1.0", back!.CurrentVersion);
        Assert.Equal("1.1.1", back.AvailableVersion);
        Assert.True(back.UpdateReady);

        SlenderStatus? old = StatusJson.Parse("""{"devices":[],"optionsPlusRunning":false,"foregroundApp":null,"updatedAt":"2026-10-15T00:00:00+00:00"}""");
        Assert.Null(old!.CurrentVersion);
        Assert.Null(old.AvailableVersion);
        Assert.False(old.UpdateReady);
    }

    [Fact]
    public void Publisher_puts_the_update_state_in_status()
    {
        SlenderStatus? written = null;
        using var publisher = new StatusPublisher(
            () => new StatusSnapshot(false, null, false, null, CurrentVersion: "1.1.0", AvailableVersion: "1.1.1", UpdateReady: true),
            write: s => written = s, optionsPlusRunning: () => false);
        publisher.PublishNow();
        Assert.Equal(("1.1.0", "1.1.1", true), (written!.CurrentVersion, written.AvailableVersion, written.UpdateReady));
    }

    [Theory]
    [InlineData("1.1.0", null, false, "Version 1.1.0 · up to date")]
    [InlineData("1.1.0", "1.1.1", true, "Version 1.1.0 · 1.1.1 ready")]
    [InlineData("1.1.0", "1.1.1", false, "Version 1.1.0 · 1.1.1 downloading")]
    [InlineData(null, null, false, "Version 9.9 · up to date")]
    public void Settings_row_text(string? current, string? available, bool ready, string expected)
    {
        var status = new SlenderStatus([], false, null, DateTimeOffset.UnixEpoch, current, available, ready);
        Assert.Equal(expected, UpdateText.Version(status, "9.9"));
        Assert.Equal("Version 9.9 · up to date", UpdateText.Version(null, "9.9"));
    }

    [Fact]
    public void Baked_source_is_read_from_assembly_metadata_and_config_overrides_it()
    {
        // The test assembly has no SlenderUpdateSource: a dev build has no default.
        Assert.Null(VelopackUpdateBackend.BakedSource(typeof(UpdateCoordinatorTests).Assembly));
        Assert.Equal(@"D:\mine", VelopackUpdateBackend.ResolveSource(@"D:\mine"));
    }
}
