using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class SettingsLabelTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Microsoft Office\Excel.exe", "Open Excel")]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Open app")]
    [InlineData("https://www.example.com/", "Open example.com")]
    [InlineData("https://example.com/path/", "Open example.com")]
    [InlineData(@"C:\Users\me\Documents\", "Open Documents")]
    [InlineData("", "Open")]
    [InlineData("   ", "Open")]
    [InlineData("/", "Open /")]
    public void LaunchName_describes_the_target(string target, string expected) =>
        Assert.Equal(expected, ActionCatalog.LaunchName(target));

    [Fact]
    public void AutoGlyph_launch_uses_web_for_urls_and_app_otherwise()
    {
        Assert.Equal("E774", RingGlyphs.AutoGlyph(new RingSlot(new ActionSpec.Launch("https://example.com/"))));
        Assert.Equal("E8A7", RingGlyphs.AutoGlyph(new RingSlot(new ActionSpec.Launch(@"C:\Apps\Tool.exe"))));
    }

    [Fact]
    public void AutoGlyph_brightness_disabled_and_text()
    {
        Assert.Equal("E706", RingGlyphs.AutoGlyph(new RingSlot(new ActionSpec.Brightness(10))));
        Assert.Equal("E711", RingGlyphs.AutoGlyph(new RingSlot(new ActionSpec.Disabled())));
        Assert.Equal("E8D2", RingGlyphs.AutoGlyph(new RingSlot(new ActionSpec.Text("x"))));
    }

    [Fact]
    public void DefaultText_shows_the_device_dpi_when_known()
    {
        Assert.Equal("Default", PointerMapping.DefaultText(null));
        Assert.Equal("Default (1200 DPI)", PointerMapping.DefaultText(1200));
    }
}
