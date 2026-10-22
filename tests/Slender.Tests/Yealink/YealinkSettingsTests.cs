using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Devices;
using Slender.Tests.Engine;
using Slender.Yealink;

namespace Slender.Tests.Yealink;

public sealed class YealinkSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTransport _transport = new();
    private readonly FakeMic _mic = new();
    private readonly FakeExecutor _exec = new();
    private readonly List<string> _log = [];
    private YealinkSettings _settings = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private YealinkModule Module(bool sync = false, bool leds = false) =>
        new(_transport, _log.Add, () => false, () => _mic) { MuteSync = sync, LedWritesEnabled = leds };

    private YealinkRouter Router(YealinkModule m) => new(m, _exec, () => _settings, a => a(), _log.Add);

    [Fact]
    public void Defaults_leave_buttons_to_the_phone_app_and_sync_mute_without_led_writes()
    {
        var y = SlenderConfig.Default.Yealink;
        Assert.IsType<ActionSpec.None>(y.Hook);
        Assert.IsType<ActionSpec.None>(y.Reject);
        Assert.IsType<ActionSpec.None>(y.TeamsButton);
        Assert.True(y.MuteSync);
        Assert.False(y.LedWrites);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var y = new YealinkSettings(new ActionSpec.Keys("Ctrl+Alt+H"), new ActionSpec.None(), new ActionSpec.Keys("Win+D"), MuteSync: false, LedWrites: true);
        new JsonConfigStore(_dir).Save(SlenderConfig.Default with { Yealink = y });
        Assert.Equal(y, new JsonConfigStore(_dir).Load().Yealink);
    }

    [Fact]
    public void An_old_config_without_the_section_loads_the_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{"version":2,"profiles":[{"bindings":{}}]}""");
        Assert.Equal(new YealinkSettings(), new JsonConfigStore(_dir).Load().Yealink);
    }

    [Fact]
    public void A_partial_section_fills_the_rest_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{"version":2,"profiles":[{"bindings":{}}],"yealink":{"ledWrites":true}}""");
        var y = new JsonConfigStore(_dir).Load().Yealink;
        Assert.True(y.LedWrites);
        Assert.True(y.MuteSync);
        Assert.IsType<ActionSpec.None>(y.Hook);
    }

    [Fact]
    public void Remove_device_resets_only_the_headset_settings()
    {
        var busy = SlenderConfig.Default with
        {
            Yealink = new YealinkSettings(new ActionSpec.Keys("A"), new ActionSpec.Keys("B"), new ActionSpec.Keys("C"), false, true),
            Keyboard = new KeyboardSettings(FnLock: true),
            LaunchAtLogin = true,
        };

        var after = DeviceSettingsReset.Forget(busy, DeviceCatalog.YealinkWh64);

        Assert.Equal(new YealinkSettings(), after.Yealink);
        Assert.Equal(busy.Keyboard, after.Keyboard);
        Assert.True(after.LaunchAtLogin);
        Assert.Equal(busy.Yealink, DeviceSettingsReset.Forget(busy, DeviceCatalog.ErgoK860).Yealink);
    }

    [Fact]
    public void Each_button_runs_its_own_bound_action()
    {
        var m = Module();
        using var r = Router(m);
        _settings = new YealinkSettings(new ActionSpec.Keys("H"), new ActionSpec.Keys("R"), new ActionSpec.Keys("T"));
        m.Start();
        var l = _transport.Link!;

        l.Feed(YealinkCollection.Telephony, 4, 0x01, 0);
        Assert.Equal([(_settings.Hook, true), (_settings.Hook, false)], _exec.Calls);
        _exec.Calls.Clear();
        l.Feed(YealinkCollection.Telephony, 4, 0x41, 0);
        Assert.Equal([(_settings.Reject, true), (_settings.Reject, false)], _exec.Calls);
        _exec.Calls.Clear();
        l.Feed(YealinkCollection.Teams, 0x9B, 0x01, 0);
        Assert.Equal([(_settings.TeamsButton, true), (_settings.TeamsButton, false)], _exec.Calls);
    }

    [Fact]
    public void An_unbound_button_does_nothing_but_is_logged()
    {
        var m = Module();
        using var r = Router(m);
        m.Start();
        var l = _transport.Link!;

        l.Feed(YealinkCollection.Telephony, 4, 0x01, 0);
        l.Feed(YealinkCollection.Telephony, 4, 0x41, 0);
        l.Feed(YealinkCollection.Teams, 0x9B, 0x01, 0);

        Assert.Empty(_exec.Calls);
        Assert.Equal(3, _log.Count(x => x.Contains("left to the phone app")));
    }

    [Fact]
    public void The_action_is_posted_not_run_on_the_read_thread_and_a_failure_is_contained()
    {
        var m = Module();
        var queued = new List<Action>();
        using var r = new YealinkRouter(m, _exec, () => _settings, queued.Add, _log.Add);
        _settings = new YealinkSettings(new ActionSpec.None(), new ActionSpec.Keys("R"), new ActionSpec.None());
        _exec.Throw = true;
        m.Start();

        _transport.Link!.Feed(YealinkCollection.Telephony, 4, 0x40, 0);
        Assert.Empty(_exec.Calls);
        Assert.Single(queued);
        queued[0]();
        Assert.Contains(_log, x => x.Contains("action failed"));
    }

    [Fact]
    public void Disposed_router_stops_listening()
    {
        var m = Module();
        var r = Router(m);
        _settings = new YealinkSettings(new ActionSpec.Keys("H"), new ActionSpec.None(), new ActionSpec.None());
        m.Start();
        r.Dispose();
        _transport.Link!.Feed(YealinkCollection.Telephony, 4, 0x01, 0);
        Assert.Empty(_exec.Calls);
    }

    [Fact]
    public void Changing_a_flag_while_started_reopens_the_link_and_an_unchanged_one_does_not()
    {
        var m = Module(sync: true, leds: false);
        m.Start();
        Assert.Equal(1, _transport.Opens);
        Assert.False(_transport.WriteRequested);

        m.ApplySettings(ledWrites: false, muteSync: true);
        Assert.Equal(1, _transport.Opens);

        var first = _transport.Link!;
        m.ApplySettings(ledWrites: true, muteSync: true);
        Assert.Equal(2, _transport.Opens);
        Assert.True(first.Disposed);
        Assert.True(_transport.WriteRequested);

        m.ApplySettings(ledWrites: true, muteSync: false);
        Assert.Equal(3, _transport.Opens);
        Assert.Equal(2, _mic.Disposals); // one per reopen that had mute sync on
    }

    [Fact]
    public void Changing_a_flag_while_stopped_opens_nothing_and_applies_at_the_next_start()
    {
        var m = Module();
        m.ApplySettings(ledWrites: true, muteSync: true);
        Assert.Equal(0, _transport.Opens);
        m.Start();
        Assert.True(_transport.WriteRequested);
    }

    [Fact]
    public void Tray_toggle_flips_the_windows_mute_even_with_mute_sync_off()
    {
        var m = Module(sync: false);
        Assert.False(m.GetMicMuted());
        Assert.True(m.ToggleMicMute());
        Assert.True(_mic.Muted);
        Assert.True(m.GetMicMuted());
        Assert.False(m.ToggleMicMute());
        Assert.False(_mic.Muted);
    }

    [Fact]
    public void Tray_toggle_reports_null_when_the_microphone_is_unavailable()
    {
        var m = Module();
        _mic.Throw = true;
        Assert.Null(m.GetMicMuted());
        Assert.Null(m.ToggleMicMute());
    }
}
