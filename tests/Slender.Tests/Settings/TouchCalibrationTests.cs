using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class TouchCalibrationTests
{
    private static List<TouchSample> S(params (int Before, int During)[] xs) => xs.Select(x => new TouchSample(x.Before, x.During)).ToList();

    [Fact]
    public void Thresholds_sit_just_above_the_deliberate_touches_and_report_what_they_would_do()
    {
        var grabs = S((600, 0), (300, 900), (0, 400), (40, 5), (0, 0));
        var touches = S((0, 0), (20, 3), (60, 5), (10, 0), (0, 2));
        CalibrationResult r = TouchCalibration.Compute(grabs, touches, 150, 24)!;
        Assert.Equal(80, r.GrabBeforePx);  // 60 + 20 beats 60 * 1.25
        Assert.Equal(13, r.GrabMovePx);    // 5 + 8 beats 5 * 1.5
        Assert.Equal((3, 3, 5), (r.GrabsIgnored, r.GrabsIgnoredBefore, r.Grabs));
        Assert.Equal((5, 5, 5), (r.TouchesKept, r.TouchesKeptBefore, r.Touches));
    }

    [Fact]
    public void Large_deliberate_movement_uses_the_percentage_margin_and_limits()
    {
        var touches = S((200, 40), (0, 0), (0, 0), (0, 0), (0, 0));
        CalibrationResult r = TouchCalibration.Compute([], touches, 150, 24)!;
        Assert.Equal(250, r.GrabBeforePx);
        Assert.Equal(60, r.GrabMovePx);
        var huge = S((5000, 5000), (0, 0), (0, 0), (0, 0), (0, 0));
        CalibrationResult capped = TouchCalibration.Compute([], huge, 150, 24)!;
        Assert.Equal((PointerSettings.GrabBeforeMax, PointerSettings.GrabMoveMax), (capped.GrabBeforePx, capped.GrabMovePx));
        CalibrationResult still = TouchCalibration.Compute([], S((0, 0), (0, 0), (0, 0), (0, 0), (0, 0)), 150, 24)!;
        Assert.Equal((PointerSettings.GrabBeforeMin, PointerSettings.GrabMoveMin), (still.GrabBeforePx, still.GrabMovePx));
    }

    [Fact]
    public void Too_few_touches_give_no_result() => Assert.Null(TouchCalibration.Compute([], S((0, 0), (0, 0)), 150, 24));

    [Fact]
    public void Sampler_measures_travel_before_and_during_up_to_the_rest_time()
    {
        var s = new TouchSampler(restMs: 250);
        s.Pointer(0, 0, 0);
        s.Pointer(300, 300, 50);   // swept in from 300 px away
        s.Pointer(500, 400, 50);
        s.TouchDown(500);
        s.Pointer(600, 410, 52);   // drifts 10 px during the rest window
        s.Pointer(900, 900, 50);   // moves away after the rest would have fired: not counted
        TouchSample t = s.Lift()!.Value;
        Assert.Equal(100, t.Before); // 400 - 300 within the 400 ms before
        Assert.Equal(10, t.During);
        Assert.Null(s.Lift());
        Assert.Single(s.Samples);
    }

    [Fact]
    public void Grab_thresholds_round_trip_and_clamp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-grab-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Slender.Engine.Config.JsonConfigStore(dir);
            store.Save(SlenderConfig.Default with { Pointer = new PointerSettings(GrabBeforePx: 90, GrabMovePx: 15) });
            PointerSettings p = store.Load().Pointer;
            Assert.Equal((90, 15), (p.GrabBeforePx, p.GrabMovePx));
            File.WriteAllText(Path.Combine(dir, "config.json"), """{"version":2,"profiles":[{"bindings":{}}],"pointer":{"grabBeforePx":1,"grabMovePx":999}}""");
            p = store.Load().Pointer;
            Assert.Equal((PointerSettings.GrabBeforeMin, PointerSettings.GrabMoveMax), (p.GrabBeforePx, p.GrabMovePx));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
