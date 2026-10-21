using Slender.Engine;
using Slender.Hidpp;
using Slender.Engine.Config;

namespace Slender.Tests.Config;

public sealed class JsonConfigStoreTests : IDisposable
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
    public void Round_trip_preserves_every_action_variant()
    {
        var bindings = new Dictionary<ushort, ActionSpec>
        {
            [ControlId.Gesture] = new ActionSpec.Keys("Ctrl+Shift+P"),
            [ControlId.Middle] = new ActionSpec.MouseButton("Middle"),
            [ControlId.Back] = new ActionSpec.None(),
            [ControlId.ModeShift] = new ActionSpec.ActionsRing(),
        };
        ushort key = 0x0100;
        foreach (ExcelCommand command in Enum.GetValues<ExcelCommand>())
        {
            bindings[key++] = new ActionSpec.Excel(command);
        }

        SlenderConfig expected = new(
            SlenderConfig.CurrentVersion,
            [new Profile(null, bindings), new Profile("excel", new Dictionary<ushort, ActionSpec>())],
            new ExcelSettings(ThumbWheelScrollsColumns: false, ColumnsPerNotch: 4),
            new HapticSettings(Enabled: false, Level: 0, OnButtonPress: true, OnActionsRing: false),
            LaunchAtLogin: true);

        Store().Save(expected);
        SlenderConfig actual = Store().Load();

        AssertSameConfig(expected, actual);
    }

    [Fact]
    public void Default_config_round_trips()
    {
        Store().Save(SlenderConfig.Default);

        AssertSameConfig(SlenderConfig.Default, Store().Load());
    }

    [Fact]
    public void Hex_control_ids_are_written_and_read_back()
    {
        var bindings = new Dictionary<ushort, ActionSpec> { [ControlId.Gesture] = new ActionSpec.None() };
        var config = SlenderConfig.Default with { Profiles = [new Profile(null, bindings)] };

        Store().Save(config);

        string text = File.ReadAllText(FilePath);
        Assert.Contains("\"0x00C3\"", text);
        SlenderConfig loaded = Store().Load();
        Assert.Equal(new ActionSpec.None(), Assert.Single(loaded.Profiles).Bindings[ControlId.Gesture]);
    }

    [Fact]
    public void Missing_file_returns_default()
    {
        Assert.False(File.Exists(FilePath));

        Assert.Same(SlenderConfig.Default, Store().Load());
    }

    [Fact]
    public void Corrupt_file_is_renamed_to_backup_and_default_returned()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");

        SlenderConfig loaded = Store().Load();

        Assert.Same(SlenderConfig.Default, loaded);
        Assert.False(File.Exists(FilePath));
        Assert.Single(Directory.GetFiles(_dir, "config.json.bad-*"));
    }

    [Theory]
    [InlineData("""{"version":99,"profiles":[],"excel":{},"haptics":{}}""")]
    [InlineData("""{"profiles":[],"excel":{},"haptics":{}}""")]
    [InlineData("""{"version":1,"profiles":[{"appProcess":null,"bindings":{"0xZZZZ":{"type":"none"}}}],"excel":{},"haptics":{}}""")]
    [InlineData("""{"version":1,"profiles":[{"appProcess":null,"bindings":{"0x0052":{"type":"warpDrive"}}}],"excel":{},"haptics":{}}""")]
    [InlineData("""{"version":1,"profiles":[{"appProcess":null,"bindings":{"0x0052":{"type":"excel","command":"Nope"}}}],"excel":{},"haptics":{}}""")]
    public void Unusable_file_is_quarantined_and_default_returned(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);

        SlenderConfig loaded = Store().Load();

        Assert.Same(SlenderConfig.Default, loaded);
        Assert.False(File.Exists(FilePath));
        Assert.Single(Directory.GetFiles(_dir, "config.json.bad-*"));
    }

    [Fact]
    public void Atomic_save_leaves_no_tmp_file_and_overwrites()
    {
        var store = Store();
        store.Save(SlenderConfig.Default);
        SlenderConfig second = SlenderConfig.Default with { LaunchAtLogin = true };

        store.Save(second);

        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.True(Store().Load().LaunchAtLogin);
    }

    [Fact]
    public void Haptic_cues_and_named_alerts_round_trip()
    {
        var haptics = new HapticSettings(Strength: 70)
        {
            Cues = new Dictionary<HapticEvent, HapticCue>
            {
                [HapticEvent.TouchTap] = new HapticCue(true, 8, 3, 90, 40),
                [HapticEvent.ButtonPress] = new HapticCue(false, 2),
            },
            Alerts = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase)
            {
                ["claude"] = new HapticCue(true, 7, 2, 200),
                ["ringer"] = new HapticCue(true, 5, 6, 80, Repeats: 4, RepeatGapMs: 1000),
                ["bad name!"] = new HapticCue(),
            },
        };
        Store().Save(SlenderConfig.Default with { Haptics = haptics });
        HapticSettings loaded = Store().Load().Haptics;

        Assert.Equal(new HapticCue(true, 8, 3, 90, 40), loaded.Cues[HapticEvent.TouchTap]);
        Assert.False(loaded.Cues[HapticEvent.ButtonPress].Enabled);
        Assert.Equal(new HapticCue(true, 7, 2, 200), loaded.Alerts["CLAUDE"]);
        Assert.Equal(new HapticCue(true, 5, 6, 80, Repeats: 4, RepeatGapMs: 1000), loaded.Alerts["ringer"]);
        Assert.Equal(2, loaded.Alerts.Count); // a name the pipe could never carry is dropped on load
    }

    [Fact]
    public void Patterns_and_cue_references_round_trip()
    {
        var haptics = new HapticSettings
        {
            Patterns = new Dictionary<string, HapticPattern>(StringComparer.OrdinalIgnoreCase)
            {
                ["Phone ringer"] = new HapticPattern(12, 6, 80, 3, 1500),
            },
            Cues = new Dictionary<HapticEvent, HapticCue> { [HapticEvent.TouchTap] = new HapticCue(true, 8, 1, 150, 100, Custom: "Phone ringer") },
            Alerts = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase) { ["call"] = new HapticCue(true, 5, Custom: "phone ringer") },
        };
        Store().Save(SlenderConfig.Default with { Haptics = haptics });
        HapticSettings loaded = Store().Load().Haptics;

        Assert.Equal(new HapticPattern(12, 6, 80, 3, 1500), loaded.Patterns["PHONE RINGER"]);
        Assert.Equal("Phone ringer", loaded.Cues[HapticEvent.TouchTap].Custom);
        Assert.Equal(100, loaded.Cues[HapticEvent.TouchTap].Strength);
        Assert.Equal("phone ringer", loaded.Alerts["call"].Custom);
        Assert.Equal(haptics, loaded);
    }

    [Fact]
    public void Bad_pattern_data_is_dropped_or_clamped_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {"version":1,"profiles":[],"excel":{},"haptics":{"patterns":{
              "Good":{"pattern":99,"pulses":50,"spacingMs":1,"repeats":0,"repeatGapMs":999999},
              "good":{"pattern":2},
              "Knock":{"pattern":2},
              "bad name!":{"pattern":2},
              "":{"pattern":2}},
             "cues":{"touchTap":{"pattern":3,"custom":"no good!"}}}}
            """);

        HapticSettings loaded = Store().Load().Haptics;

        Assert.Equal(new HapticPattern(14, 6, 40, 1, 5000), Assert.Single(loaded.Patterns).Value); // built-in names, bad characters and case duplicates are dropped
        Assert.Null(loaded.Cues[HapticEvent.TouchTap].Custom);
    }

    [Fact]
    public void An_old_config_without_patterns_loads_as_before()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"version":1,"profiles":[],"excel":{},"haptics":{"cues":{"touchTap":{"pattern":8,"pulses":2,"spacingMs":90,"strength":40}}}}""");

        HapticSettings loaded = Store().Load().Haptics;

        Assert.Empty(loaded.Patterns);
        Assert.Equal(new HapticCue(true, 8, 2, 90, 40), loaded.Cues[HapticEvent.TouchTap]);
        Store().Save(SlenderConfig.Default with { Haptics = loaded });
        Assert.DoesNotContain("patterns\": {", File.ReadAllText(FilePath));
    }

    private static void AssertSameConfig(SlenderConfig expected, SlenderConfig actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Excel, actual.Excel);
        Assert.Equal(expected.Haptics, actual.Haptics);
        Assert.Equal(expected.LaunchAtLogin, actual.LaunchAtLogin);
        Assert.Equal(expected.Profiles.Count, actual.Profiles.Count);

        for (int i = 0; i < expected.Profiles.Count; i++)
        {
            Profile e = expected.Profiles[i];
            Profile a = actual.Profiles[i];
            Assert.Equal(e.AppProcess, a.AppProcess);
            Assert.Equal(e.Bindings.Count, a.Bindings.Count);
            foreach (KeyValuePair<ushort, ActionSpec> pair in e.Bindings)
            {
                Assert.True(a.Bindings.TryGetValue(pair.Key, out ActionSpec? value), $"missing key {pair.Key:X4}");
                Assert.Equal(pair.Value, value);
            }
        }
    }

    [Fact]
    public void Battery_alerts_round_trip()
    {
        JsonConfigStore store = Store();
        SlenderConfig config = store.Load() with { BatteryAlerts = new BatteryAlertSettings(false, 20, 5) };
        store.Save(config);
        Assert.Equal(new BatteryAlertSettings(false, 20, 5), Store().Load().BatteryAlerts);
    }

    [Fact]
    public void Config_without_battery_alerts_loads_defaults()
    {
        Store().Save(SlenderConfig.Default with { LaunchAtLogin = true });
        var obj = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.True(obj.Remove("batteryAlerts"));
        File.WriteAllText(FilePath, obj.ToJsonString());

        SlenderConfig loaded = Store().Load();

        Assert.Equal(new BatteryAlertSettings(), loaded.BatteryAlerts);
        Assert.True(loaded.LaunchAtLogin);
        Assert.Empty(Directory.GetFiles(_dir, "config.json.bad-*"));
    }
}
