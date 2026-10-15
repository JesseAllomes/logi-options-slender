using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;

namespace Slender.Tests.Config;

public sealed class ConfigV2Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private JsonConfigStore Store() => new(_dir);

    private string FilePath => Path.Combine(_dir, "config.json");

    [Fact]
    public void V2_round_trip_keeps_pointer_keyboard_keybindings_name_and_path()
    {
        SlenderConfig expected = new(
            SlenderConfig.CurrentVersion,
            [
                new Profile(null, new Dictionary<ushort, ActionSpec>()) { DisplayName = "All apps" },
                new Profile("excel", new Dictionary<ushort, ActionSpec> { [ControlId.Back] = new ActionSpec.Excel(ExcelCommand.PreviousSheet) })
                {
                    KeyBindings = new Dictionary<ushort, ActionSpec> { [191] = new ActionSpec.Keys("Win+Shift+S"), [10] = new ActionSpec.None() },
                    DisplayName = "Excel",
                    ExePath = @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE",
                },
            ],
            new ExcelSettings(true, 3),
            new HapticSettings(true, 1, true, false),
            LaunchAtLogin: true)
        {
            Pointer = new PointerSettings(Dpi: 2400, SmartShiftEnabled: false, SmartShiftThreshold: 30, InvertWheel: true, InvertThumbWheel: true),
            Keyboard = new KeyboardSettings(FnLock: true),
        };

        Store().Save(expected);
        SlenderConfig actual = Store().Load();

        Assert.Equal(2, actual.Version);
        Assert.Equal(expected.Pointer, actual.Pointer);
        Assert.Equal(expected.Keyboard, actual.Keyboard);
        Assert.Equal(expected.Profiles.Count, actual.Profiles.Count);
        Profile excel = actual.Profiles[1];
        Assert.Equal("Excel", excel.DisplayName);
        Assert.Equal(@"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", excel.ExePath);
        Assert.Equal(new ActionSpec.Keys("Win+Shift+S"), excel.KeyBindings[191]);
        Assert.IsType<ActionSpec.None>(excel.KeyBindings[10]);
        Assert.Equal(new ActionSpec.Excel(ExcelCommand.PreviousSheet), excel.Bindings[ControlId.Back]);
        Assert.Equal("All apps", actual.Profiles[0].DisplayName);
        Assert.Null(actual.Profiles[0].ExePath);
    }

    [Fact]
    public void Null_dpi_round_trips_as_null()
    {
        Store().Save(SlenderConfig.Default);

        Assert.Null(Store().Load().Pointer.Dpi);
    }

    [Fact]
    public void Version_1_file_loads_with_defaults_for_new_fields_and_saves_as_version_2()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "launchAtLogin": true,
              "profiles": [
                { "appProcess": null, "bindings": { "0x00C3": { "type": "keys", "chord": "Win+Tab" } } },
                { "appProcess": "excel", "bindings": { "0x0056": { "type": "excel", "command": "NextSheet" } } }
              ],
              "excel": { "thumbWheelScrollsColumns": false, "columnsPerNotch": 2 },
              "haptics": { "enabled": false, "level": 1, "onButtonPress": true, "onActionsRing": false }
            }
            """);

        SlenderConfig loaded = Store().Load();

        Assert.Equal(SlenderConfig.CurrentVersion, loaded.Version);
        Assert.True(loaded.LaunchAtLogin);
        Assert.Equal(new PointerSettings(), loaded.Pointer);
        Assert.Equal(new KeyboardSettings(), loaded.Keyboard);
        Assert.Empty(loaded.Profiles[0].KeyBindings);
        Assert.Null(loaded.Profiles[1].DisplayName);
        Assert.Null(loaded.Profiles[1].ExePath);
        Assert.Equal(new ActionSpec.Keys("Win+Tab"), loaded.Profiles[0].Bindings[ControlId.Gesture]);
        Assert.False(loaded.Excel.ThumbWheelScrollsColumns);
        Assert.Equal(2, loaded.Excel.ColumnsPerNotch);

        Store().Save(loaded);

        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"version\": 2", json);
        Assert.Equal(2, Store().Load().Version);
        Assert.Equal(new ActionSpec.Keys("Win+Tab"), Store().Load().Profiles[0].Bindings[ControlId.Gesture]);
    }

    [Fact]
    public void Future_version_is_quarantined_and_default_returned()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "version": 3, "profiles": [] }""");

        SlenderConfig loaded = Store().Load();

        Assert.Equal(SlenderConfig.Default.Profiles.Count, loaded.Profiles.Count);
        Assert.False(File.Exists(FilePath));
        Assert.Single(Directory.GetFiles(_dir, "config.json.bad-*"));
    }

    [Fact]
    public void Out_of_range_pointer_values_are_clamped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "version": 2, "profiles": [ { "appProcess": null, "bindings": {} } ],
              "pointer": { "dpi": 5, "smartShiftThreshold": 9999 } }
            """);

        PointerSettings pointer = Store().Load().Pointer;

        Assert.Equal(100, pointer.Dpi);
        Assert.Equal(255, pointer.SmartShiftThreshold);
    }
}

public sealed class StatusJsonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static SlenderStatus Sample() => new(
        [
            new DeviceState("mx-master-4", "MX Master 4", true, 65, false),
            new DeviceState("ergo-k860", "ERGO K860", false, null, false, "Unknown"),
        ],
        OptionsPlusRunning: true,
        ForegroundApp: "excel",
        UpdatedAt: new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.FromHours(11)));

    [Fact]
    public void Status_round_trips_through_json()
    {
        SlenderStatus original = Sample();

        SlenderStatus? parsed = StatusJson.Parse(StatusJson.Serialize(original));

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Devices.Count);
        Assert.Equal(original.Devices[0], parsed.Devices[0]);
        Assert.Equal(original.Devices[1], parsed.Devices[1]);
        Assert.True(parsed.OptionsPlusRunning);
        Assert.Equal("excel", parsed.ForegroundApp);
        Assert.Equal(original.UpdatedAt, parsed.UpdatedAt);
    }

    [Fact]
    public void Json_uses_camel_case_names()
    {
        string json = StatusJson.Serialize(Sample());

        Assert.Contains("\"devices\"", json);
        Assert.Contains("\"optionsPlusRunning\": true", json);
        Assert.Contains("\"batteryPercent\": 65", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Malformed_status_parses_to_null(string text)
    {
        Assert.Null(StatusJson.Parse(text));
    }

    [Fact]
    public void WriteAtomic_creates_the_file_and_leaves_no_temp_file()
    {
        string path = Path.Combine(_dir, "status.json");

        StatusJson.WriteAtomic(path, Sample());
        StatusJson.WriteAtomic(path, Sample() with { ForegroundApp = "outlook" });

        Assert.Equal("outlook", StatusJson.TryRead(path)?.ForegroundApp);
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void TryRead_returns_null_when_the_file_is_missing()
    {
        Assert.Null(StatusJson.TryRead(Path.Combine(_dir, "nope.json")));
    }
}

public sealed class ConfigFileWatcherTests : IDisposable
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
    public void External_change_raises_Changed_with_the_new_config()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default);
        using var watcher = new ConfigFileWatcher(store);
        var raised = new List<SlenderConfig>();
        watcher.Changed += (_, c) => raised.Add(c);

        // Another process saves a different config.
        new JsonConfigStore(_dir).Save(SlenderConfig.Default with { LaunchAtLogin = true });
        watcher.CheckNow();

        Assert.Single(raised);
        Assert.True(raised[0].LaunchAtLogin);
    }

    [Fact]
    public void Own_writes_and_unchanged_content_do_not_raise()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default);
        using var watcher = new ConfigFileWatcher(store);
        int raised = 0;
        watcher.Changed += (_, _) => raised++;

        store.Save(SlenderConfig.Default with { LaunchAtLogin = true });
        watcher.NoteOwnWrite();
        watcher.CheckNow();
        watcher.CheckNow();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Real_file_event_is_debounced_into_one_reload()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default);
        using var watcher = new ConfigFileWatcher(store);
        var signal = new TaskCompletionSource<SlenderConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        int raised = 0;
        watcher.Changed += (_, c) =>
        {
            raised++;
            signal.TrySetResult(c);
        };

        new JsonConfigStore(_dir).Save(SlenderConfig.Default with { LaunchAtLogin = true });

        SlenderConfig result = await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(500);

        Assert.True(result.LaunchAtLogin);
        Assert.Equal(1, raised);
    }
}
