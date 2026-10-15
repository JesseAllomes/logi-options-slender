using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;

namespace Slender.Tests.Config;

public sealed class FeelSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private JsonConfigStore Store() => new(_dir);

    private void WriteJson(string pointerAndHaptics)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            "{ \"version\": 2, \"profiles\": [ { \"appProcess\": null, \"bindings\": {} } ], " + pointerAndHaptics + " }");
    }

    [Fact]
    public void New_fields_round_trip()
    {
        var config = SlenderConfig.Default with { Haptics = new HapticSettings(Strength: 62) };
        config = config with { Pointer = new PointerSettings(WheelResistance: 40, SensePanelForce: 5000, SensePanelBaseline: 3887, SmoothScroll: true) };

        Store().Save(config);
        var back = Store().Load();

        Assert.Equal(config.Pointer, back.Pointer);
        Assert.Equal(62, back.Haptics.Strength);
    }

    [Fact]
    public void Old_config_loads_with_new_fields_null_and_level_mapping()
    {
        WriteJson("\"pointer\": { \"dpi\": 1600 }, \"haptics\": { \"enabled\": true, \"level\": 1 }");

        var loaded = Store().Load();

        Assert.Equal(1600, loaded.Pointer.Dpi);
        Assert.Null(loaded.Pointer.WheelResistance);
        Assert.Null(loaded.Pointer.SensePanelForce);
        Assert.Null(loaded.Haptics.Strength);
        Assert.Equal(50, loaded.Haptics.EffectiveStrength);
    }

    [Fact]
    public void New_fields_are_clamped_on_load()
    {
        WriteJson("\"pointer\": { \"dpi\": 90000, \"wheelResistance\": 250, \"sensePanelForce\": -4 }, \"haptics\": { \"strength\": -3 }");

        var loaded = Store().Load();

        Assert.Equal(8000, loaded.Pointer.Dpi);
        Assert.Equal(100, loaded.Pointer.WheelResistance);
        Assert.Equal(1, loaded.Pointer.SensePanelForce);
        Assert.Equal(0, loaded.Haptics.Strength);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(3, 100)]
    [InlineData(9, 100)]
    public void Level_maps_to_device_strength_when_strength_is_null(int level, int expected) =>
        Assert.Equal(expected, new HapticSettings(Level: level).EffectiveStrength);

    [Fact]
    public void Strength_overrides_level() => Assert.Equal(33, new HapticSettings(Level: 3, Strength: 33).EffectiveStrength);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 0)]
    [InlineData(55, 1)]
    [InlineData(100, 3)]
    public void Notch_follows_strength(int strength, int notch) => Assert.Equal(notch, HapticSettings.NotchFor(strength));

    [Fact]
    public void Sense_panel_slider_maps_across_the_band()
    {
        Assert.Equal(0, PointerMapping.ForceToSlider(1943, 1943, 7774));
        Assert.Equal(100, PointerMapping.ForceToSlider(7774, 1943, 7774));
        Assert.Equal(1943, PointerMapping.SliderToForce(0, 1943, 7774));
        Assert.Equal(7774, PointerMapping.SliderToForce(100, 1943, 7774));
        Assert.Equal("Light", PointerMapping.ForceText(0));
        Assert.Equal("Firm", PointerMapping.ForceText(100));
    }

    [Fact]
    public void Status_json_carries_the_mouse_feel_values()
    {
        var device = new DeviceState("mx-master-4", "MX Master 4", true, 80, false)
        {
            WheelResistance = 75, SensePanelForce = 3887, SensePanelForceMin = 1943, SensePanelForceMax = 7774, HapticStrength = 45,
        };
        var back = StatusJson.Parse(StatusJson.Serialize(new SlenderStatus([device], false, null, DateTimeOffset.UnixEpoch)))!.Devices[0];

        Assert.Equal(75, back.WheelResistance);
        Assert.Equal(3887, back.SensePanelForce);
        Assert.Equal(1943, back.SensePanelForceMin);
        Assert.Equal(7774, back.SensePanelForceMax);
        Assert.Equal(45, back.HapticStrength);
    }
}
