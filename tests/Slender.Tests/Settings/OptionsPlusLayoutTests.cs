using Slender.Hidpp;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

/// <summary>Small hand-written fixtures in the shape of the Options+ depot files. No Logitech files are used.</summary>
internal static class Fixtures
{
    public const string MouseManifest = """
        {
          "devices": [
            { "modelId": "2b042", "resources": [
              { "key": "device_image", "src": "front_core.webp" },
              { "key": "device_buttons_image", "src": "side_core.webp" } ] },
            { "modelId": "2b042_ext1", "resources": [
              { "key": "device_buttons_image", "src": "side_ext_1.webp" } ] }
          ],
          "resources": [ { "key": "image_metadata", "src": "core_metadata.json" } ]
        }
        """;

    public const string KeyboardManifest = """
        {
          "devices": [
            { "modelId": "6b359", "resources": [
              { "key": "device_image", "src": "front.webp" },
              { "key": "device_keys_image", "src": "front.webp" },
              { "key": "image_metadata", "src": "metadata.json" } ] }
          ],
          "resources": [ { "key": "manifest", "src": "manifest.json" } ]
        }
        """;

    public const string MouseMetadata = """
        {
          "images": [
            { "key": "device_gesture_buttons_image", "origin": { "width": 636, "height": 1024 },
              "assignments": [ { "slotId": "mx-master-4-2b042-left", "slotName": "SLOT_NAME_GESTURE_LEFT_BUTTON", "marker": { "x": -40, "y": 30 }, "label": { "x": -1, "y": 0 } } ] },
            { "key": "device_buttons_image", "origin": { "width": 636, "height": 1024 },
              "assignments": [
                { "slotId": "mx-master-4-2b042_c82", "slotName": "SLOT_NAME_MIDDLE_BUTTON", "marker": { "x": 75.5, "y": 19 }, "label": { "x": 1, "y": 0 } },
                { "slotId": "mx-master-4-2b042_c196", "slotName": "SLOT_NAME_MODESHIFT_BUTTON", "marker": { "x": 80.5, "y": 39.5 }, "label": { "x": 1, "y": 0 } },
                { "slotId": "mx-master-4-2b042_thumb_wheel_adapter", "slotName": "SLOT_NAME_THUMBWHEEL", "marker": { "x": 56.5, "y": 56.4 }, "label": { "x": 1, "y": 0 } },
                { "slotId": "mx-master-4-2b042_c86", "slotName": "SLOT_NAME_FORWARD_BUTTON", "marker": { "x": 42.5, "y": 47 }, "label": { "x": 1, "y": 0 } },
                { "slotId": "mx-master-4-2b042_c83", "slotName": "SLOT_NAME_BACK_BUTTON", "marker": { "x": 47.3, "y": 60 }, "label": { "x": 0, "y": 1 } },
                { "slotId": "mx-master-4-2b042_c195", "slotName": "SLOT_NAME_GESTURE_BUTTON", "marker": { "x": 38.6, "y": 36.1 }, "label": { "x": -1, "y": 0 } },
                { "slotId": "mx-master-4-2b042_c416", "slotName": "ASSIGNMENT_NAME_SHOW_RADIAL_MENU", "marker": { "x": 30.4, "y": 48.9 }, "label": { "x": -1, "y": 0 } },
                { "slotId": "mx-master-4-2b042_unknown", "slotName": "SLOT_NAME_SOMETHING_NEW", "marker": { "x": 1, "y": 1 }, "label": { "x": 1, "y": 0 } }
              ] }
          ]
        }
        """;

    public const string KeyboardMetadata = """
        {
          "images": [
            { "key": "device_keys_image", "origin": { "width": 1872, "height": 728 },
              "assignments": [
                { "slotId": "ergo-k860-6b359_c199", "slotName": "SLOT_NAME_BRIGHTNESS_DOWN", "marker": { "x": 7.5, "y": 4.6 }, "label": { "x": -1, "y": -1 } },
                { "slotId": "ergo-k860-6b359_c224", "slotName": "SLOT_NAME_MISSION_CONTROL", "marker": { "x": 15.3, "y": 6.1 }, "label": { "x": -1, "y": -1 } },
                { "slotId": "ergo-k860-6b359_c191", "slotName": "SLOT_NAME_SCREEN_CAPTURE", "marker": { "x": 63.7, "y": 5 }, "label": { "x": 1, "y": -1 } },
                { "slotId": "ergo-k860-6b359_c228", "slotName": "SLOT_NAME_PREVIOUS", "marker": { "x": 34.3, "y": 10.5 }, "label": { "x": 1, "y": -1 } }
              ] }
          ]
        }
        """;
}

public sealed class OptionsPlusLayoutTests
{
    [Fact]
    public void Manifest_resolves_the_buttons_image_for_the_exact_model()
    {
        Assert.Equal("side_core.webp", OptionsPlusLayout.FindResource(Fixtures.MouseManifest, "2b042", "device_buttons_image"));
        Assert.Equal("front_core.webp", OptionsPlusLayout.FindResource(Fixtures.MouseManifest, "2b042", "device_image"));
        Assert.Equal("side_ext_1.webp", OptionsPlusLayout.FindResource(Fixtures.MouseManifest, "2b042_ext1", "device_buttons_image"));
    }

    [Fact]
    public void Manifest_returns_null_for_unknown_model_or_key()
    {
        Assert.Null(OptionsPlusLayout.FindResource(Fixtures.MouseManifest, "ffffff", "device_buttons_image"));
        Assert.Null(OptionsPlusLayout.FindResource(Fixtures.MouseManifest, "2b042", "device_keys_image"));
    }

    [Fact]
    public void Metadata_file_comes_from_the_device_or_the_manifest_wide_resource()
    {
        Assert.Equal("metadata.json", OptionsPlusLayout.FindMetadataFile(Fixtures.KeyboardManifest, "6b359"));
        Assert.Equal("core_metadata.json", OptionsPlusLayout.FindMetadataFile(Fixtures.MouseManifest, "2b042"));
    }

    [Fact]
    public void HasModel_matches_exact_model_ids_only()
    {
        Assert.True(OptionsPlusLayout.HasModel(Fixtures.MouseManifest, "2b042"));
        Assert.False(OptionsPlusLayout.HasModel(Fixtures.MouseManifest, "2b04"));
        Assert.False(OptionsPlusLayout.HasModel(Fixtures.KeyboardManifest, "2b042"));
    }

    [Fact]
    public void Mouse_hotspots_map_to_slender_control_ids()
    {
        var parsed = OptionsPlusLayout.ParseMetadata(Fixtures.MouseMetadata, "device_buttons_image", DeviceKind.Mouse);

        Assert.NotNull(parsed);
        Assert.Equal(636, parsed.Value.Width);
        Assert.Equal(1024, parsed.Value.Height);
        Dictionary<string, ushort> ids = parsed.Value.Hotspots.ToDictionary(h => h.Slot, h => h.ControlId);
        Assert.Equal(ControlId.Middle, ids["SLOT_NAME_MIDDLE_BUTTON"]);
        Assert.Equal(ControlId.ModeShift, ids["SLOT_NAME_MODESHIFT_BUTTON"]);
        Assert.Equal(DeviceIds.ThumbWheelPseudoId, ids["SLOT_NAME_THUMBWHEEL"]);
        Assert.Equal(ControlId.Forward, ids["SLOT_NAME_FORWARD_BUTTON"]);
        Assert.Equal(ControlId.Back, ids["SLOT_NAME_BACK_BUTTON"]);
        Assert.Equal(ControlId.Gesture, ids["SLOT_NAME_GESTURE_BUTTON"]);
        Assert.Equal(ControlId.SensePanel, ids["ASSIGNMENT_NAME_SHOW_RADIAL_MENU"]);
    }

    [Fact]
    public void Unmappable_slots_are_skipped()
    {
        var parsed = OptionsPlusLayout.ParseMetadata(Fixtures.MouseMetadata, "device_buttons_image", DeviceKind.Mouse);

        Assert.NotNull(parsed);
        Assert.Equal(7, parsed.Value.Hotspots.Count);
        Assert.DoesNotContain(parsed.Value.Hotspots, h => h.Slot.Contains("SOMETHING_NEW"));
    }

    [Fact]
    public void Coordinates_and_label_sides_are_read_from_the_metadata()
    {
        var parsed = OptionsPlusLayout.ParseMetadata(Fixtures.MouseMetadata, "device_buttons_image", DeviceKind.Mouse)!.Value;

        Hotspot middle = parsed.Hotspots.Single(h => h.ControlId == ControlId.Middle);
        Assert.Equal(75.5, middle.X);
        Assert.Equal(19, middle.Y);
        Assert.Equal(1, middle.LabelSide);
        Assert.Equal(-1, parsed.Hotspots.Single(h => h.ControlId == ControlId.Gesture).LabelSide);
        Assert.Equal(-1, parsed.Hotspots.Single(h => h.ControlId == ControlId.SensePanel).LabelSide);
        // label.x == 0 (below) is drawn on the right.
        Assert.Equal(1, parsed.Hotspots.Single(h => h.ControlId == ControlId.Back).LabelSide);
    }

    [Fact]
    public void Missing_image_key_returns_null()
    {
        Assert.Null(OptionsPlusLayout.ParseMetadata(Fixtures.MouseMetadata, "device_nope_image", DeviceKind.Mouse));
        Assert.Null(OptionsPlusLayout.ParseMetadata("{}", "device_buttons_image", DeviceKind.Mouse));
    }

    [Fact]
    public void Keyboard_slots_use_the_decimal_cid_after_c()
    {
        var parsed = OptionsPlusLayout.ParseMetadata(Fixtures.KeyboardMetadata, "device_keys_image", DeviceKind.Keyboard)!.Value;

        Assert.Equal(1872, parsed.Width);
        Assert.Equal(728, parsed.Height);
        Assert.Equal<ushort>([199, 224, 191, 228], parsed.Hotspots.Select(h => h.ControlId).ToArray());
        Assert.Equal("Brightness down", parsed.Hotspots[0].Name);
        Assert.Equal("Task view", parsed.Hotspots[1].Name);
        Assert.Equal("Screen capture", parsed.Hotspots[2].Name);
        Assert.Equal("Previous track", parsed.Hotspots[3].Name);
        Assert.Equal(-1, parsed.Hotspots[0].LabelSide);
        Assert.Equal(1, parsed.Hotspots[2].LabelSide);
    }

    [Theory]
    [InlineData("SLOT_NAME_VOLUME_UP", "Volume up")]
    [InlineData("SLOT_NAME_PLAY_PAUSE", "Play / pause")]
    [InlineData("SLOT_NAME_MISSION_CONTROL", "Task view")]
    [InlineData("SLOT_NAME_SHOW_DESKTOP", "Show desktop")]
    [InlineData("ASSIGNMENT_NAME_SHOW_RADIAL_MENU", "Show radial menu")]
    [InlineData("SLOT_NAME_BACK_BUTTON", "Back")]
    public void Friendly_names(string slot, string expected)
    {
        Assert.Equal(expected, OptionsPlusLayout.FriendlyName(slot));
    }

    [Theory]
    [InlineData("mx-master-4-2b042_c82", "SLOT_NAME_WHATEVER", DeviceKind.Mouse, 82)]
    [InlineData("ergo-k860-6b359_c10", "SLOT_NAME_CALCULATOR", DeviceKind.Keyboard, 10)]
    [InlineData("anything", "SLOT_NAME_MIDDLE_BUTTON", DeviceKind.Mouse, 0x52)]
    public void Slot_mapping_prefers_names_then_decimal_cids(string slotId, string slotName, DeviceKind kind, int expected)
    {
        Assert.True(OptionsPlusLayout.TryMapSlot(kind, slotId, slotName, out ushort id, out _));
        Assert.Equal(expected, id);
    }

    [Fact]
    public void Slot_without_name_or_cid_does_not_map()
    {
        Assert.False(OptionsPlusLayout.TryMapSlot(DeviceKind.Mouse, "mx-master-4-2b042-left", "SLOT_NAME_GESTURE_LEFT_BUTTON", out _, out _));
    }
}
