using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class PointerAndLabelTests
{
    [Theory]
    [InlineData(0, 400)]
    [InlineData(400, 400)]
    [InlineData(1612, 1600)]
    [InlineData(1625, 1650)]
    [InlineData(99999, 8000)]
    public void Dpi_snaps_to_50_inside_the_range(double raw, int expected) => Assert.Equal(expected, PointerMapping.SnapDpi(raw));

    [Fact]
    public void Dpi_text_says_Default_for_null() => Assert.Equal("Default", PointerMapping.DpiText(null));

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(22.4, 22)]
    [InlineData(80, 50)]
    public void Threshold_clamps_to_1_50(double raw, int expected) => Assert.Equal(expected, PointerMapping.ClampThreshold(raw));

    [Fact]
    public void Excel_sheet_commands_show_their_chord_chips()
    {
        ActionLabel l = ActionCatalog.Describe(new ActionSpec.Excel(ExcelCommand.NextSheet));
        Assert.Equal("Next sheet", l.Text);
        Assert.Equal(["Ctrl", "PgDn"], l.Chips);
        Assert.Empty(ActionCatalog.Describe(new ActionSpec.Excel(ExcelCommand.ZoomReset)).Chips);
    }

    [Fact]
    public void Keys_labels_have_name_then_chips()
    {
        ActionLabel l = ActionCatalog.Describe(new ActionSpec.Keys("Ctrl+PageUp"));
        Assert.Equal("Previous tab", l.Text);
        Assert.Equal(["Ctrl", "PgUp"], l.Chips);
    }

    [Fact]
    public void Thumb_pair_encodes_and_decodes()
    {
        string chord = ActionCatalog.EncodePair("VolumeUp", "VolumeDown");
        Assert.Equal("VolumeUp|VolumeDown", chord);
        Assert.True(ActionCatalog.TryDecodePair(chord, out string r, out string l));
        Assert.Equal(("VolumeUp", "VolumeDown"), (r, l));
        Assert.False(ActionCatalog.TryDecodePair("Ctrl+Z", out _, out _));
        Assert.False(ActionCatalog.TryDecodePair("A|", out _, out _));
        Assert.False(ActionCatalog.TryDecodePair("A|B|C", out _, out _));
    }

    [Theory]
    [InlineData("VolumeUp|VolumeDown", "Volume up / down")]
    [InlineData("MediaNext|MediaPrevious", "Next / previous track")]
    [InlineData("Ctrl+Plus|Ctrl+Minus", "Zoom in / out")]
    public void Thumb_pair_label(string chord, string expected)
    {
        ActionLabel l = ActionCatalog.Describe(new ActionSpec.Keys(chord));
        Assert.Equal(expected, l.Text);
        Assert.Empty(l.Chips);
    }

    [Fact]
    public void Store_logo_picks_the_largest_variant_named_in_the_manifest()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-logo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Images"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"), "<Package><uap:VisualElements Square44x44Logo=\"Images\\App.png\" /></Package>");
            File.WriteAllBytes(Path.Combine(dir, "Images", "App.targetsize-16.png"), new byte[10]);
            File.WriteAllBytes(Path.Combine(dir, "Images", "App.targetsize-256.png"), new byte[500]);
            File.WriteAllBytes(Path.Combine(dir, "Images", "App.targetsize-256_altform-lightunplated.png"), new byte[900]);
            File.WriteAllBytes(Path.Combine(dir, "Images", "AppOther.png"), new byte[2000]);

            Assert.Equal(Path.Combine(dir, "Images", "App.targetsize-256.png"), StoreLogo.Find(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Store_package_directory_is_the_folder_under_WindowsApps()
    {
        Assert.Equal(@"C:\Program Files\WindowsApps\MSTeams_1_x64__x", StoreLogo.PackageDirectory(@"C:\Program Files\WindowsApps\MSTeams_1_x64__x\ms-teams.exe"));
        Assert.Null(StoreLogo.PackageDirectory(@"C:\Windows\notepad.exe"));
    }
}
