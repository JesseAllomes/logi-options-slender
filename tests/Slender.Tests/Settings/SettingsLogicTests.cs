using Slender.Engine;
using Slender.Hidpp;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class ProfileOpsTests
{
    private static SlenderConfig Empty() => SlenderConfig.Default with
    {
        Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { DisplayName = "All apps" }],
    };

    [Fact]
    public void Setting_an_app_binding_changes_only_that_app()
    {
        SlenderConfig c = ProfileOps.AddApp(Empty(), "Excel", "Excel", @"C:\x\excel.exe");

        c = ProfileOps.SetBinding(c, "excel", DeviceKind.Mouse, ControlId.Forward, new ActionSpec.Excel(ExcelCommand.NextSheet));

        Assert.Equal(new ActionSpec.Excel(ExcelCommand.NextSheet), ProfileOps.Own(c, "excel", DeviceKind.Mouse, ControlId.Forward));
        Assert.Null(ProfileOps.Own(c, null, DeviceKind.Mouse, ControlId.Forward));
    }

    [Fact]
    public void Effective_inherits_from_all_apps_when_the_app_has_no_binding()
    {
        SlenderConfig c = ProfileOps.AddApp(Empty(), "excel", "Excel", null);
        c = ProfileOps.SetBinding(c, null, DeviceKind.Mouse, ControlId.Gesture, new ActionSpec.Keys("Win+Tab"));

        (ActionSpec? action, bool inherited) = ProfileOps.Effective(c, "excel", DeviceKind.Mouse, ControlId.Gesture);

        Assert.Equal(new ActionSpec.Keys("Win+Tab"), action);
        Assert.True(inherited);
    }

    [Fact]
    public void Own_binding_overrides_the_inherited_one()
    {
        SlenderConfig c = ProfileOps.AddApp(Empty(), "excel", "Excel", null);
        c = ProfileOps.SetBinding(c, null, DeviceKind.Mouse, ControlId.Gesture, new ActionSpec.Keys("Win+Tab"));
        c = ProfileOps.SetBinding(c, "excel", DeviceKind.Mouse, ControlId.Gesture, new ActionSpec.Keys("Ctrl+Z"));

        (ActionSpec? action, bool inherited) = ProfileOps.Effective(c, "excel", DeviceKind.Mouse, ControlId.Gesture);

        Assert.Equal(new ActionSpec.Keys("Ctrl+Z"), action);
        Assert.False(inherited);
    }

    [Fact]
    public void Null_action_clears_the_binding_so_the_app_follows_all_apps_again()
    {
        SlenderConfig c = ProfileOps.AddApp(Empty(), "excel", "Excel", null);
        c = ProfileOps.SetBinding(c, "excel", DeviceKind.Mouse, ControlId.Back, new ActionSpec.Excel(ExcelCommand.PreviousSheet));

        c = ProfileOps.SetBinding(c, "excel", DeviceKind.Mouse, ControlId.Back, null);

        Assert.Null(ProfileOps.Own(c, "excel", DeviceKind.Mouse, ControlId.Back));
        Assert.Equal(0, ProfileOps.ChangedCount(c, "excel", DeviceKind.Mouse));
    }

    [Fact]
    public void Mouse_and_keyboard_bindings_are_stored_separately()
    {
        SlenderConfig c = ProfileOps.SetBinding(Empty(), null, DeviceKind.Keyboard, 191, new ActionSpec.Keys("Ctrl+Shift+S"));

        Profile all = c.Profiles[0];
        Assert.Empty(all.Bindings);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Shift+S"), all.KeyBindings[191]);
        Assert.Equal(1, ProfileOps.ChangedCount(c, null, DeviceKind.Keyboard));
        Assert.Equal(0, ProfileOps.ChangedCount(c, null, DeviceKind.Mouse));
    }

    [Fact]
    public void Editing_an_app_creates_its_profile_and_a_missing_default_profile_is_inserted_first()
    {
        SlenderConfig c = SlenderConfig.Default with { Profiles = [] };

        c = ProfileOps.SetBinding(c, "chrome", DeviceKind.Mouse, ControlId.Back, new ActionSpec.MouseButton("Back"));
        c = ProfileOps.SetBinding(c, null, DeviceKind.Mouse, ControlId.Middle, new ActionSpec.MouseButton("Middle"));

        Assert.Null(c.Profiles[0].AppProcess);
        Assert.Equal("chrome", c.Profiles[1].AppProcess);
    }

    [Fact]
    public void AddApp_is_case_insensitive_and_ignores_duplicates_and_blanks()
    {
        SlenderConfig c = ProfileOps.AddApp(Empty(), "Excel", "Excel", @"C:\excel.exe");

        SlenderConfig again = ProfileOps.AddApp(c, "EXCEL", "Excel 2", null);
        SlenderConfig blank = ProfileOps.AddApp(c, "  ", "x", null);

        Assert.Same(c, again);
        Assert.Same(c, blank);
        Assert.Equal("excel", c.Profiles[1].AppProcess);
        Assert.Equal(@"C:\excel.exe", c.Profiles[1].ExePath);
    }

    [Fact]
    public void RemoveApp_never_removes_all_apps()
    {
        SlenderConfig c = ProfileOps.AddApp(Empty(), "excel", "Excel", null);

        SlenderConfig removed = ProfileOps.RemoveApp(c, "Excel");

        Assert.Single(removed.Profiles);
        Assert.Null(removed.Profiles[0].AppProcess);
    }

    [Fact]
    public void None_actions_do_not_count_as_changes_or_as_own_bindings()
    {
        SlenderConfig c = ProfileOps.SetBinding(Empty(), null, DeviceKind.Mouse, ControlId.Back, new ActionSpec.None());

        Assert.Null(ProfileOps.Own(c, null, DeviceKind.Mouse, ControlId.Back));
        Assert.Equal(0, ProfileOps.ChangedCount(c, null, DeviceKind.Mouse));
    }

    [Fact]
    public void ChangedCount_ignores_controls_the_device_does_not_have()
    {
        SlenderConfig c = ProfileOps.SetBinding(Empty(), null, DeviceKind.Mouse, 0x7777, new ActionSpec.Keys("A"));

        Assert.Equal(0, ProfileOps.ChangedCount(c, null, DeviceKind.Mouse));
    }

    [Fact]
    public void Thumb_wheel_pseudo_id_is_a_normal_binding_key()
    {
        SlenderConfig c = ProfileOps.SetBinding(Empty(), null, DeviceKind.Mouse, DeviceIds.ThumbWheelPseudoId, new ActionSpec.Keys("Ctrl+Z"));

        Assert.Equal(1, ProfileOps.ChangedCount(c, null, DeviceKind.Mouse));
    }

    [Theory]
    [InlineData(1, "1 button changed \u00B7 others follow All apps")]
    [InlineData(4, "4 buttons changed \u00B7 others follow All apps")]
    [InlineData(0, "0 buttons changed \u00B7 others follow All apps")]
    public void Subtitle_for_mouse_apps(int changed, string expected)
    {
        Assert.Equal(expected, ProfileOps.Subtitle(changed, DeviceKind.Mouse, isAllApps: false));
    }

    [Fact]
    public void Subtitle_for_keyboard_uses_keys()
    {
        Assert.Equal("1 key changed \u00B7 others follow All apps", ProfileOps.Subtitle(1, DeviceKind.Keyboard, false));
        Assert.Equal("3 keys changed \u00B7 others follow All apps", ProfileOps.Subtitle(3, DeviceKind.Keyboard, false));
    }

    [Fact]
    public void App_name_falls_back_to_a_capitalised_process_name()
    {
        Assert.Equal("Excel", ProfileOps.AppName(new Profile("excel", new Dictionary<ushort, ActionSpec>())));
        Assert.Equal("Microsoft Excel", ProfileOps.AppName(new Profile("excel", new Dictionary<ushort, ActionSpec>()) { DisplayName = "Microsoft Excel" }));
    }
}

public sealed class ActionCatalogTests
{
    [Fact]
    public void Chord_parts_use_short_key_cap_names()
    {
        Assert.Equal(["Ctrl", "PgDn"], ActionCatalog.ChordParts("Ctrl+PageDown"));
        Assert.Equal(["Win", "Shift", "S"], ActionCatalog.ChordParts("Win+Shift+S"));
    }

    [Fact]
    public void Keys_label_has_chips_and_a_name_for_well_known_chords()
    {
        ActionLabel known = ActionCatalog.Describe(new ActionSpec.Keys("Win+Shift+S"));
        ActionLabel custom = ActionCatalog.Describe(new ActionSpec.Keys("Ctrl+Shift+P"));

        Assert.Equal("Snip & copy", known.Text);
        Assert.Equal(["Win", "Shift", "S"], known.Chips);
        Assert.Equal(string.Empty, custom.Text);
        Assert.Equal(["Ctrl", "Shift", "P"], custom.Chips);
    }

    [Fact]
    public void Other_actions_read_as_plain_text()
    {
        Assert.Equal("Next sheet", ActionCatalog.Describe(new ActionSpec.Excel(ExcelCommand.NextSheet)).Text);
        Assert.Equal("Zoom to 100%", ActionCatalog.Describe(new ActionSpec.Excel(ExcelCommand.ZoomReset)).Text);
        Assert.Equal("Right click", ActionCatalog.Describe(new ActionSpec.MouseButton("Right")).Text);
        Assert.Equal("Actions Ring", ActionCatalog.Describe(new ActionSpec.ActionsRing()).Text);
        Assert.Equal("Not assigned", ActionCatalog.Describe(new ActionSpec.None()).Text);
    }

    [Fact]
    public void Every_excel_command_has_a_friendly_name_and_an_option()
    {
        foreach (ExcelCommand command in Enum.GetValues<ExcelCommand>())
        {
            Assert.DoesNotContain(command.ToString(), new[] { ActionCatalog.ExcelName(command) });
        }

        Assert.Equal(Enum.GetValues<ExcelCommand>().Length, ActionCatalog.ExcelOptions.Count);
    }

    [Fact]
    public void Mouse_picker_options_are_all_valid_engine_values()
    {
        string[] valid = ["Left", "Right", "Middle", "Back", "Forward"];
        Assert.All(ActionCatalog.MouseOptions, o => Assert.Contains(((ActionSpec.MouseButton)o.Action).Button, valid));
    }

    [Fact]
    public void Keyboard_app_options_are_chords_the_engine_can_parse()
    {
        Assert.All(ActionCatalog.KeyboardAppOptions, o => Assert.True(ChordParser.TryParse(((ActionSpec.Keys)o.Action).Chord, out _), o.Title));
    }

    [Fact]
    public void Plain_text_joins_name_and_chord()
    {
        Assert.Equal("Snip & copy Win+Shift+S", ActionCatalog.Plain(new ActionSpec.Keys("Win+Shift+S")));
        Assert.Equal("Ctrl+Shift+P", ActionCatalog.Plain(new ActionSpec.Keys("Ctrl+Shift+P")));
    }
}

public sealed class ChordRecorderTests
{
    [Theory]
    [InlineData(false, true, false, true, 0x50, "Ctrl+Shift+P")]
    [InlineData(true, false, false, true, 0x53, "Win+Shift+S")]
    [InlineData(false, true, true, false, 0x22, "Ctrl+Alt+PageDown")]
    [InlineData(false, false, false, false, 0x74, "F5")]
    [InlineData(false, true, false, false, 0xBB, "Ctrl+Plus")]
    [InlineData(false, false, true, false, 0x25, "Alt+Left")]
    public void Compose_builds_engine_chords(bool win, bool ctrl, bool alt, bool shift, int vk, string expected)
    {
        Assert.Equal(expected, ChordRecorder.Compose(win, ctrl, alt, shift, (ushort)vk));
    }

    [Theory]
    [InlineData(false, true, false, true, 0x50)]
    [InlineData(true, false, false, true, 0x53)]
    [InlineData(false, true, true, false, 0x22)]
    [InlineData(false, false, false, false, 0x74)]
    [InlineData(false, true, false, false, 0xBB)]
    [InlineData(false, false, true, false, 0x25)]
    [InlineData(false, true, false, false, 0x31)]
    [InlineData(false, false, false, true, 0xDC)]
    public void Recorded_chords_are_accepted_by_the_engine_parser(bool win, bool ctrl, bool alt, bool shift, int vk)
    {
        string? chord = ChordRecorder.Compose(win, ctrl, alt, shift, (ushort)vk);

        Assert.NotNull(chord);
        Assert.True(ChordParser.TryParse(chord, out _), chord);
    }

    [Fact]
    public void Every_named_key_round_trips_through_the_engine_parser()
    {
        for (ushort vk = 1; vk < 256; vk++)
        {
            string? name = ChordRecorder.KeyName(vk);
            if (name is not null)
            {
                Assert.True(ChordParser.TryParse("Ctrl+" + name, out _), $"vk {vk:X2} gave '{name}'");
            }
        }
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0xA2)]
    public void Modifiers_are_modifiers(int vk)
    {
        Assert.True(ChordRecorder.IsModifier((ushort)vk));
    }

    [Fact]
    public void Keys_without_an_engine_name_compose_to_null()
    {
        Assert.Null(ChordRecorder.Compose(false, true, false, false, 0x60)); // numpad 0
        Assert.Null(ChordRecorder.Compose(false, false, false, false, 0xAF)); // volume up
    }
}

public sealed class ImageGeometryTests
{
    [Fact]
    public void Reads_webp_lossy_lossless_and_extended_sizes()
    {
        byte[] lossy = new byte[32];
        "RIFF"u8.CopyTo(lossy);
        "WEBPVP8 "u8.CopyTo(lossy.AsSpan(8));
        lossy[26] = 0x12; lossy[27] = 0x07; // 1810
        lossy[28] = 0xC0; lossy[29] = 0x03; // 960

        byte[] lossless = new byte[32];
        "RIFF"u8.CopyTo(lossless);
        "WEBPVP8L"u8.CopyTo(lossless.AsSpan(8));
        uint bits = (uint)(636 - 1) | ((uint)(1024 - 1) << 14);
        BitConverter.GetBytes(bits).CopyTo(lossless, 21);

        byte[] extended = new byte[32];
        "RIFF"u8.CopyTo(extended);
        "WEBPVP8X"u8.CopyTo(extended.AsSpan(8));
        int w = 656 - 1;
        int h = 1024 - 1;
        extended[24] = (byte)w; extended[25] = (byte)(w >> 8); extended[26] = (byte)(w >> 16);
        extended[27] = (byte)h; extended[28] = (byte)(h >> 8); extended[29] = (byte)(h >> 16);

        Assert.Equal((1810, 960), ImageSize.Parse(lossy));
        Assert.Equal((636, 1024), ImageSize.Parse(lossless));
        Assert.Equal((656, 1024), ImageSize.Parse(extended));
    }

    [Fact]
    public void Reads_png_size()
    {
        byte[] png = new byte[24];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }.CopyTo(png, 0);
        png[18] = 0x01; png[19] = 0x00; // 256
        png[22] = 0x01; png[23] = 0x00; // 256

        Assert.Equal((256, 256), ImageSize.Parse(png));
    }

    [Fact]
    public void Unknown_data_has_no_size()
    {
        Assert.Null(ImageSize.Parse(new byte[40]));
        Assert.Null(ImageSize.Parse([]));
    }

    [Fact]
    public void Mouse_hotspots_are_offset_by_the_centred_crop()
    {
        var layout = new DeviceLayout("r", "t", 636, 1024, []);
        var spot = new Hotspot(1, "x", "s", 50, 50, 1);

        (double x, double y) = HotspotMapper.ToImageFraction(DeviceKind.Mouse, spot, layout, 656, 1024);

        Assert.Equal((318 + 10) / 656.0, x, 6);
        Assert.Equal(0.5, y, 6);
    }

    [Fact]
    public void Keyboard_hotspots_aim_at_the_key_centre()
    {
        var layout = new DeviceLayout("r", "t", 1872, 728, []);
        var spot = new Hotspot(1, "x", "s", 7.5, 4.6, -1);

        (double x, double y) = HotspotMapper.ToImageFraction(DeviceKind.Keyboard, spot, layout, 1810, 960);

        Assert.Equal((7.5 + 1.7) / 100, x, 6);
        Assert.Equal(9.6 / 100 * 728 / 960, y, 6);
    }

    [Fact]
    public void Mapping_falls_back_to_plain_percent_without_image_size()
    {
        var layout = new DeviceLayout("r", "t", 0, 0, []);

        (double x, double y) = HotspotMapper.ToImageFraction(DeviceKind.Mouse, new Hotspot(1, "x", "s", 25, 75, 1), layout, 0, 0);

        Assert.Equal(0.25, x);
        Assert.Equal(0.75, y);
    }
}

public sealed class LabelLayoutTests
{
    [Fact]
    public void Rows_that_fit_stay_where_they_are()
    {
        Assert.Equal([100.0, 200.0, 300.0], LabelLayout.SpreadRows([100, 200, 300], 60, 50, 800));
    }

    [Fact]
    public void Crowded_rows_are_pushed_down_with_a_minimum_gap()
    {
        double[] ys = LabelLayout.SpreadRows([100, 110, 120], 60, 50, 800);

        Assert.Equal([100.0, 160.0, 220.0], ys);
    }

    [Fact]
    public void Rows_are_pulled_up_when_they_would_run_off_the_bottom()
    {
        double[] ys = LabelLayout.SpreadRows([700, 710, 720], 60, 50, 760);

        Assert.Equal(760, ys[2]);
        Assert.Equal(700, ys[1]);
        Assert.Equal(640, ys[0]);
    }

    [Fact]
    public void First_row_respects_the_minimum()
    {
        Assert.Equal(80, LabelLayout.SpreadRows([10], 60, 80, 800)[0]);
    }

    [Fact]
    public void Empty_input_is_fine()
    {
        Assert.Empty(LabelLayout.SpreadRows([], 60, 0, 100));
    }
}

public sealed class UiStateTests : IDisposable
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
    public void State_round_trips()
    {
        string path = Path.Combine(_dir, "ui.json");
        new UiState { Width = 1300, Height = 800, Device = DeviceIds.Keyboard, App = "excel" }.Save(path);

        UiState loaded = UiState.Load(path);

        Assert.Equal(1300, loaded.Width);
        Assert.Equal(800, loaded.Height);
        Assert.Equal(DeviceIds.Keyboard, loaded.Device);
        Assert.Equal("excel", loaded.App);
    }

    [Fact]
    public void Missing_or_corrupt_file_gives_defaults_and_size_is_clamped_to_the_minimum()
    {
        string path = Path.Combine(_dir, "ui.json");
        Assert.Equal(1160, UiState.Load(path).Width);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ nope");
        Assert.Equal(DeviceIds.Mouse, UiState.Load(path).Device);

        File.WriteAllText(path, """{ "Width": 200, "Height": 100 }""");
        UiState small = UiState.Load(path);
        Assert.Equal(1040, small.Width);
        Assert.Equal(640, small.Height);
    }
}
