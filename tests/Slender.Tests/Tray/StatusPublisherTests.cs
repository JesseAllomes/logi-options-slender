using Slender.Engine;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class StatusPublisherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class Harness
    {
        public DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        public StatusSnapshot Snapshot = new(true, 80, false, "excel");
        public List<SlenderStatus> Written = [];
        public List<(TimeSpan Delay, Action Run)> Scheduled = [];
        public int OptionsChecks;
        public bool OptionsRunning;

        public StatusPublisher Create() => new(
            () => Snapshot,
            write: Written.Add,
            optionsPlusRunning: () =>
            {
                OptionsChecks++;
                return OptionsRunning;
            },
            now: () => Now,
            schedule: (delay, run) =>
            {
                Scheduled.Add((delay, run));
                return new NoopDisposable();
            });
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public void Mouse_feel_values_and_force_band_are_published_while_connected()
    {
        var h = new Harness();
        h.Snapshot = h.Snapshot with { MouseValues = new MouseValues(75, 3887, 3887, 45) };
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        DeviceState mouse = Assert.Single(h.Written).Devices[0];
        Assert.Equal(75, mouse.WheelResistance);
        Assert.Equal(3887, mouse.SensePanelForce);
        Assert.Equal(1943, mouse.SensePanelForceMin);
        Assert.Equal(7774, mouse.SensePanelForceMax);
        Assert.Equal(45, mouse.HapticStrength);
    }

    [Fact]
    public void Device_presence_comes_from_the_registry_list()
    {
        var h = new Harness();
        h.Snapshot = h.Snapshot with
        {
            Devices =
            [
                new Slender.Engine.Devices.DeviceEntry("mx-master-4", "MX Master 4", Slender.Engine.Devices.DevicePresence.Sleeping, false, null),
                new Slender.Engine.Devices.DeviceEntry("ergo-k860", "ERGO K860", Slender.Engine.Devices.DevicePresence.Hidden, false, null),
            ],
        };
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        SlenderStatus status = Assert.Single(h.Written);
        Assert.Equal("sleeping", Assert.Single(status.Devices).Presence);
        Assert.Equal("mx-master-4", status.Devices[0].Id);
    }

    private static IReadOnlyList<DeviceState> HeadsetStatus(Slender.Engine.Devices.DevicePresence? headset)
    {
        var h = new Harness();
        if (headset is { } p)
        {
            h.Snapshot = h.Snapshot with
            {
                Devices =
                [
                    new Slender.Engine.Devices.DeviceEntry("mx-master-4", "MX Master 4", Slender.Engine.Devices.DevicePresence.Active, false, null),
                    new Slender.Engine.Devices.DeviceEntry("ergo-k860", "ERGO K860", Slender.Engine.Devices.DevicePresence.Hidden, false, null),
                    new Slender.Engine.Devices.DeviceEntry("yealink-wh64", "Yealink WH64", p, false, null),
                ],
            };
        }

        using StatusPublisher publisher = h.Create();
        publisher.Changed();
        return Assert.Single(h.Written).Devices;
    }

    [Fact]
    public void Headset_is_listed_once_seen_and_never_without_a_registry_list()
    {
        Assert.DoesNotContain(HeadsetStatus(null), d => d.Id == "yealink-wh64");
        Assert.DoesNotContain(HeadsetStatus(Slender.Engine.Devices.DevicePresence.Hidden), d => d.Id == "yealink-wh64");

        DeviceState gone = Assert.Single(HeadsetStatus(Slender.Engine.Devices.DevicePresence.Disconnected), d => d.Id == "yealink-wh64");
        Assert.Equal("disconnected", gone.Presence);
        Assert.False(gone.Connected);

        DeviceState here = Assert.Single(HeadsetStatus(Slender.Engine.Devices.DevicePresence.Active), d => d.Id == "yealink-wh64");
        Assert.True(here.Connected);
    }

    [Fact]
    public void The_headset_report_is_published_and_survives_the_json_round_trip()
    {
        var h = new Harness();
        var report = new HeadsetState(true, 72, false, false, 5, true, [new HeadsetSetting("General", "Wireless range", "Long (150 m)")]);
        h.Snapshot = h.Snapshot with
        {
            Devices = [new Slender.Engine.Devices.DeviceEntry("yealink-wh64", "Yealink WH64", Slender.Engine.Devices.DevicePresence.Active, false, null)],
            Headset = report,
        };
        using StatusPublisher publisher = h.Create();
        publisher.Changed();

        DeviceState written = Assert.Single(h.Written[^1].Devices, d => d.Id == "yealink-wh64");
        Assert.Equal(72, written.BatteryPercent);
        SlenderStatus back = Slender.Engine.Config.StatusJson.Parse(Slender.Engine.Config.StatusJson.Serialize(h.Written[^1]))!;
        HeadsetState read = Assert.Single(back.Devices, d => d.Id == "yealink-wh64").Headset!;
        Assert.Equal(72, read.BatteryPercent);
        Assert.Equal("Long (150 m)", Assert.Single(read.Settings!).Value);
    }

    [Fact]
    public void Without_a_registry_list_both_devices_are_listed()
    {
        var h = new Harness();
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        Assert.Equal(["mx-master-4", "ergo-k860"], Assert.Single(h.Written).Devices.Select(d => d.Id));
    }

    [Fact]
    public void Device_presence_is_null_without_a_registry()
    {
        var h = new Harness();
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        Assert.All(Assert.Single(h.Written).Devices, d => Assert.Null(d.Presence));
    }

    [Fact]
    public void Mouse_feel_values_are_hidden_when_the_mouse_is_disconnected()
    {
        var h = new Harness();
        h.Snapshot = new StatusSnapshot(false, null, false, null, MouseValues: new MouseValues(75, 3887, 3887, 45));
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        DeviceState mouse = Assert.Single(h.Written).Devices[0];
        Assert.Null(mouse.WheelResistance);
        Assert.Null(mouse.SensePanelForceMin);
        Assert.Null(mouse.HapticStrength);
    }

    [Fact]
    public void Idle_publisher_schedules_nothing()
    {
        var h = new Harness();
        using StatusPublisher publisher = h.Create();

        Assert.Empty(h.Scheduled);
        Assert.Empty(h.Written);
    }

    [Fact]
    public void First_change_writes_at_once_with_mouse_and_keyboard_entries()
    {
        var h = new Harness { OptionsRunning = true };
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        SlenderStatus status = Assert.Single(h.Written);
        Assert.Empty(h.Scheduled);
        DeviceState mouse = status.Devices.Single(d => d.Id == "mx-master-4");
        Assert.True(mouse.Connected);
        Assert.Equal(80, mouse.BatteryPercent);
        DeviceState keyboard = status.Devices.Single(d => d.Id == "ergo-k860");
        Assert.False(keyboard.Connected);
        Assert.True(status.OptionsPlusRunning);
        Assert.Equal("excel", status.ForegroundApp);
        Assert.Equal(h.Now, status.UpdatedAt);
    }

    [Fact]
    public void Disconnected_mouse_reports_no_battery_even_if_one_was_known()
    {
        var h = new Harness { Snapshot = new StatusSnapshot(false, 55, true, null) };
        using StatusPublisher publisher = h.Create();

        publisher.Changed();

        DeviceState mouse = h.Written.Single().Devices.Single(d => d.Id == "mx-master-4");
        Assert.False(mouse.Connected);
        Assert.Null(mouse.BatteryPercent);
        Assert.False(mouse.Charging);
    }

    [Fact]
    public void A_burst_inside_the_interval_becomes_one_delayed_write_of_the_newest_state()
    {
        var h = new Harness();
        using StatusPublisher publisher = h.Create();
        publisher.Changed(); // writes now

        h.Now += TimeSpan.FromMilliseconds(100);
        h.Snapshot = h.Snapshot with { ForegroundApp = "outlook" };
        publisher.Changed();
        h.Snapshot = h.Snapshot with { ForegroundApp = "chrome" };
        publisher.Changed();
        publisher.Changed();

        Assert.Single(h.Written);
        (TimeSpan delay, Action run) = Assert.Single(h.Scheduled);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delay);

        h.Now += delay;
        run();

        Assert.Equal(2, h.Written.Count);
        Assert.Equal("chrome", h.Written[1].ForegroundApp);
    }

    [Fact]
    public void Writes_are_never_closer_than_the_minimum_interval()
    {
        var h = new Harness();
        using StatusPublisher publisher = h.Create();
        DateTimeOffset? due = null;

        for (int i = 0; i < 40; i++)
        {
            publisher.Changed();
            if (due is null && h.Scheduled.Count > 0)
            {
                due = h.Now + h.Scheduled[^1].Delay;
            }

            h.Now += TimeSpan.FromMilliseconds(70);
            if (due is { } d && h.Now >= d)
            {
                Action run = h.Scheduled[^1].Run;
                h.Scheduled.Clear();
                due = null;
                run();
            }
        }

        DateTimeOffset[] stamps = h.Written.Select(w => w.UpdatedAt).ToArray();
        Assert.True(stamps.Length > 2);
        for (int i = 1; i < stamps.Length; i++)
        {
            Assert.True(stamps[i] - stamps[i - 1] >= StatusPublisher.MinInterval, $"write {i} came too soon");
        }
    }

    [Fact]
    public void Options_plus_check_is_cached_for_30_seconds()
    {
        var h = new Harness();
        using StatusPublisher publisher = h.Create();

        publisher.Changed();
        h.Now += TimeSpan.FromSeconds(1);
        publisher.Changed();
        h.Now += TimeSpan.FromSeconds(1);
        publisher.Changed();
        Assert.Equal(1, h.OptionsChecks);

        h.Now += TimeSpan.FromSeconds(31);
        h.OptionsRunning = true;
        publisher.PublishNow();

        Assert.Equal(2, h.OptionsChecks);
        Assert.True(h.Written[^1].OptionsPlusRunning);
    }

    [Fact]
    public void Disposed_publisher_ignores_changes()
    {
        var h = new Harness();
        StatusPublisher publisher = h.Create();
        publisher.Dispose();

        publisher.Changed();
        publisher.PublishNow();

        Assert.Empty(h.Written);
    }

    [Fact]
    public void Default_writer_creates_status_json_atomically()
    {
        string path = Path.Combine(_dir, "status.json");
        using var publisher = new StatusPublisher(() => new StatusSnapshot(true, 70, false, "excel"), path, optionsPlusRunning: () => false);

        publisher.Changed();

        SlenderStatus? read = Slender.Engine.Config.StatusJson.TryRead(path);
        Assert.NotNull(read);
        Assert.Equal(70, read.Devices[0].BatteryPercent);
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void The_sensor_dpi_list_is_published_compactly_and_survives_the_json_round_trip()
    {
        var h = new Harness();
        h.Snapshot = h.Snapshot with { MouseDpiCaps = Slender.Hidpp.DpiCapabilities.Range(200, 8000, 50) };
        using StatusPublisher pub = h.Create();
        pub.PublishNow();
        SlenderStatus back = Slender.Engine.Config.StatusJson.Parse(Slender.Engine.Config.StatusJson.Serialize(h.Written[^1]))!;
        DeviceState mouse = back.Devices.Single(d => d.Id == "mx-master-4");
        Assert.Equal((200, 8000, 50), (mouse.DpiMin, mouse.DpiMax, mouse.DpiStep));
        Assert.Null(mouse.DpiValues); // a plain range needs no list

        h.Snapshot = h.Snapshot with { MouseDpiCaps = Slender.Hidpp.DpiCapabilities.List([800, 1600, 3200]) };
        pub.PublishNow();
        back = Slender.Engine.Config.StatusJson.Parse(Slender.Engine.Config.StatusJson.Serialize(h.Written[^1]))!;
        mouse = back.Devices.Single(d => d.Id == "mx-master-4");
        Assert.Equal((800, 3200, 800), (mouse.DpiMin, mouse.DpiMax, mouse.DpiStep));
        Assert.Equal([800, 1600, 3200], mouse.DpiValues);
    }

    [Fact]
    public void No_dpi_list_is_published_while_unknown_or_the_mouse_is_away()
    {
        var h = new Harness();
        using StatusPublisher pub = h.Create();
        pub.PublishNow();
        Assert.Null(h.Written[^1].Devices.Single(d => d.Id == "mx-master-4").DpiMin);
        h.Snapshot = h.Snapshot with { MouseConnected = false, MouseDpiCaps = Slender.Hidpp.DpiCapabilities.Range(200, 8000, 50) };
        pub.PublishNow();
        Assert.Null(h.Written[^1].Devices.Single(d => d.Id == "mx-master-4").DpiMin);
    }
}

public sealed class SettingsLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Exe_next_to_the_tray_is_preferred()
    {
        Directory.CreateDirectory(_dir);
        string exe = Path.Combine(_dir, SettingsLauncher.ExeName);
        File.WriteAllText(exe, string.Empty);

        Assert.Equal(exe, SettingsLauncher.ResolveExe(_dir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Missing_exe_resolves_to_null()
    {
        Directory.CreateDirectory(_dir);

        Assert.Null(SettingsLauncher.ResolveExe(_dir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Dev_tree_falls_back_to_the_settings_project_output()
    {
        string trayBin = Path.Combine(_dir, "src", "Slender.Tray", "bin", "Debug", "net10.0-windows");
        string settingsOut = Path.Combine(_dir, "src", "Slender.Settings", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64");
        Directory.CreateDirectory(trayBin);
        Directory.CreateDirectory(settingsOut);
        string exe = Path.Combine(settingsOut, SettingsLauncher.ExeName);
        File.WriteAllText(exe, string.Empty);

        Assert.Equal(exe, SettingsLauncher.ResolveExe(trayBin + Path.DirectorySeparatorChar));
    }
}
