using Slender.Engine;
using Slender.Hidpp;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class DpiScaleTests
{
    private static DeviceState Mouse(int? min = null, int? max = null, int? step = null, IReadOnlyList<int>? values = null) =>
        new("mx-master-4", "MX Master 4", true, 80, false) { DpiMin = min, DpiMax = max, DpiStep = step, DpiValues = values };

    [Fact]
    public void Unknown_sensor_falls_back_to_the_fixed_range()
    {
        Assert.Equal(DpiScale.Fallback, DpiScale.From(null));
        Assert.Equal(DpiScale.Fallback, DpiScale.From(Mouse()));
        Assert.Equal(DpiScale.Fallback, DpiScale.From(Mouse(400, 100, 50))); // nonsense is ignored
        Assert.Equal(DpiScale.Fallback, DpiScale.From(Mouse(400, 4000, 0)));
        Assert.Equal((200, 8000, 50), (DpiScale.Fallback.Min, DpiScale.Fallback.Max, DpiScale.Fallback.Step));
        Assert.Equal(PointerMapping.SnapDpi(1234), DpiScale.Fallback.Snap(1234));
    }

    [Theory]
    [InlineData(1234, 1200)]
    [InlineData(1250, 1300)] // halfway rounds up, like the fixed slider
    [InlineData(50, 400)]
    [InlineData(99999, 4000)]
    public void A_published_range_sets_the_limits_and_the_grid(double raw, int expected)
    {
        DpiScale s = DpiScale.From(Mouse(400, 4000, 100));
        Assert.Equal((400, 4000, 100), (s.Min, s.Max, s.Step));
        Assert.Equal(expected, s.Snap(raw));
    }

    [Theory]
    [InlineData(1000, 800)]
    [InlineData(1200, 800)] // a tie goes lower, like the engine
    [InlineData(2500, 3200)]
    [InlineData(1, 800)]
    [InlineData(9000, 3200)]
    public void An_explicit_list_snaps_to_a_listed_value(double raw, int expected)
    {
        DpiScale s = DpiScale.From(Mouse(800, 3200, 800, [3200, 800, 1600]));
        Assert.Equal(expected, s.Snap(raw));
    }

    [Fact]
    public void Snapping_agrees_with_the_engine_clamp()
    {
        var caps = DpiCapabilities.Range(400, 4000, 100);
        DpiScale s = DpiScale.From(Mouse(caps.Min, caps.Max, caps.Step, caps.IrregularValues));
        for (int v = 0; v <= 9000; v += 37) Assert.Equal(caps.Clamp(v), s.Snap(v));
        var list = DpiCapabilities.List([800, 1600, 3200]);
        DpiScale l = DpiScale.From(Mouse(list.Min, list.Max, list.Step, list.IrregularValues));
        for (int v = 0; v <= 9000; v += 37) Assert.Equal(list.Clamp(v), l.Snap(v));
    }

    [Fact]
    public void Capabilities_summarise_ranges_and_lists_compactly()
    {
        var range = DpiCapabilities.Range(200, 8000, 50);
        Assert.Equal(50, range.Step);
        Assert.Null(range.IrregularValues);

        var even = DpiCapabilities.List([400, 800, 1200, 1600]); // evenly stepped: min/max/step say it all
        Assert.Equal(400, even.Step);
        Assert.Null(even.IrregularValues);

        var uneven = DpiCapabilities.List([400, 800, 1600]);
        Assert.Equal(400, uneven.Step);
        Assert.Equal([400, 800, 1600], uneven.IrregularValues);

        var huge = DpiCapabilities.List(Enumerable.Range(0, 100).Select(i => 200 + i * i * 10));
        Assert.Null(huge.IrregularValues); // too many to publish; the grid and limits still apply
        Assert.True(huge.Step >= 1);

        Assert.Equal(1, DpiCapabilities.List([1600]).Step);
    }

    [Fact]
    public void App_speed_is_snapped_to_the_real_steps_when_given_and_to_the_fixed_ones_otherwise()
    {
        SlenderConfig cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>())] };
        DpiScale scale = DpiScale.From(Mouse(400, 4000, 100));
        Assert.Equal(1200, AppDpiModel.Own(AppDpiModel.Set(cfg, "excel", 1234, scale), "excel"));
        Assert.Equal(4000, AppDpiModel.Own(AppDpiModel.Set(cfg, "excel", 8000, scale), "excel"));
        Assert.Equal(1250, AppDpiModel.Own(AppDpiModel.Set(cfg, "excel", 1234), "excel")); // fixed 50-step grid
    }
}
