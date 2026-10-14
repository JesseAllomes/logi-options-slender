using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Contracts;

public class ConfigDefaultsTests
{
    [Fact]
    public void Default_has_global_and_excel_profiles()
    {
        var cfg = SlenderConfig.Default;

        Assert.Contains(cfg.Profiles, p => p.AppProcess is null);
        var excel = Assert.Single(cfg.Profiles, p => p.AppProcess == "excel");
        Assert.Equal(new ActionSpec.Excel(ExcelCommand.NextSheet), excel.Bindings[ControlId.Forward]);
        Assert.True(cfg.Excel.ThumbWheelScrollsColumns);
    }
}
