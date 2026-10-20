using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class PickerTabsTests
{
    [Theory]
    [InlineData("excel", "excel", false, true)]
    [InlineData("excel", "EXCEL.EXE", false, true)]
    [InlineData("excel", "outlook", false, false)]
    [InlineData("excel", null, false, false)]
    [InlineData("excel", "outlook", true, true)] // an Excel action already set stays visible
    [InlineData("more", "outlook", false, true)]
    [InlineData("keys", null, false, true)]
    public void Excel_tab_only_in_excel(string tab, string? app, bool ownIsExcel, bool shown) =>
        Assert.Equal(shown, PickerTabs.Show(tab, app, ownIsExcel, hideShortcuts: false));

    [Fact]
    public void Shortcuts_tab_follows_the_switch()
    {
        Assert.True(PickerTabs.Show("shortcuts", "outlook", false, hideShortcuts: false));
        Assert.False(PickerTabs.Show("shortcuts", "outlook", false, hideShortcuts: true));
    }
}
