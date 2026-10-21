using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class PopoverPlacementTests
{
    // The user's desk (2026-10-21): ultrawide primary, 1440p right, a 4K laptop at 200%, and a 2560x720 strip holding the taskbar.
    private static readonly List<(PxRect Work, double Scale, bool Primary)> Desk =
    [
        (new PxRect(6000, 1026, 3000, 1780), 2.0, false),
        (new PxRect(3440, 0, 2560, 1392), 1.0, false),
        (new PxRect(0, 0, 3440, 1392), 1.0, true),
        (new PxRect(1455, 1440, 2560, 672), 1.0, false),
    ];

    [Fact]
    public void Full_window_too_tall_for_the_strip_opens_centred_on_the_primary_screen()
    {
        PxRect? r = PopoverPlacement.OtherScreen(Desk, last: null, 1166, 900);
        Assert.Equal(new PxRect((3440 - 1166) / 2, (1392 - 900) / 2, 1166, 900), r);
    }

    [Fact]
    public void It_returns_to_the_spot_it_was_last_on()
    {
        Assert.Equal(new PxRect(3500, 100, 1166, 900), PopoverPlacement.OtherScreen(Desk, new PxPoint(3500, 100), 1166, 900));
    }

    [Fact]
    public void Size_follows_the_chosen_screens_scale()
    {
        Assert.Equal(new PxRect(6100, 1100, 2332, 1400), PopoverPlacement.OtherScreen(Desk, new PxPoint(6100, 1100), 1166, 700));
    }

    [Fact]
    public void A_last_spot_on_a_screen_it_does_not_fit_falls_back_to_the_primary()
    {
        // 900 DIPs at 200% is taller than the laptop's work area, and the strip is too short.
        Assert.Equal(new PxRect((3440 - 1166) / 2, (1392 - 900) / 2, 1166, 900), PopoverPlacement.OtherScreen(Desk, new PxPoint(6100, 1100), 1166, 900));
        Assert.Equal(new PxRect((3440 - 1166) / 2, (1392 - 900) / 2, 1166, 900), PopoverPlacement.OtherScreen(Desk, new PxPoint(2000, 1500), 1166, 900));
    }

    [Fact]
    public void Nowhere_big_enough_gives_null() =>
        Assert.Null(PopoverPlacement.OtherScreen(Desk, null, 4000, 3000));

    private static readonly PxRect Monitor = new(0, 0, 1920, 1080);
    private static readonly PxRect WorkBottomTaskbar = new(0, 0, 1920, 1032);

    [Fact]
    public void Bottom_taskbar_puts_the_popover_above_it_centred_on_the_cursor()
    {
        PxRect r = PopoverPlacement.Place(new PxPoint(1500, 1050), Monitor, WorkBottomTaskbar, 330, 400, 12);

        Assert.Equal(new PxRect(1335, 1032 - 400 - 12, 330, 400), r);
    }

    [Fact]
    public void Popover_is_kept_inside_the_work_area_near_the_right_corner()
    {
        PxRect r = PopoverPlacement.Place(new PxPoint(1915, 1050), Monitor, WorkBottomTaskbar, 330, 400, 12);

        Assert.Equal(1920 - 330 - 12, r.X);
        Assert.True(r.Right <= 1920 - 12);
    }

    [Fact]
    public void Popover_is_kept_inside_the_work_area_near_the_left_corner()
    {
        PxRect r = PopoverPlacement.Place(new PxPoint(5, 1050), Monitor, WorkBottomTaskbar, 330, 400, 12);

        Assert.Equal(12, r.X);
    }

    [Fact]
    public void Top_taskbar_puts_the_popover_below_it()
    {
        var work = new PxRect(0, 48, 1920, 1032);

        PxRect r = PopoverPlacement.Place(new PxPoint(960, 10), Monitor, work, 330, 400, 12);

        Assert.Equal(TaskbarEdge.Top, PopoverPlacement.DetectEdge(new PxPoint(960, 10), Monitor, work));
        Assert.Equal(48 + 12, r.Y);
        Assert.Equal(960 - 165, r.X);
    }

    [Fact]
    public void Left_and_right_taskbars_put_the_popover_beside_them()
    {
        PxRect left = PopoverPlacement.Place(new PxPoint(10, 500), Monitor, new PxRect(60, 0, 1860, 1080), 330, 400, 12);
        PxRect right = PopoverPlacement.Place(new PxPoint(1910, 500), Monitor, new PxRect(0, 0, 1860, 1080), 330, 400, 12);

        Assert.Equal(60 + 12, left.X);
        Assert.Equal(300, left.Y);
        Assert.Equal(1860 - 330 - 12, right.X);
        Assert.Equal(300, right.Y);
    }

    [Fact]
    public void Auto_hidden_taskbar_uses_the_edge_nearest_the_cursor()
    {
        Assert.Equal(TaskbarEdge.Bottom, PopoverPlacement.DetectEdge(new PxPoint(900, 1075), Monitor, Monitor));
        Assert.Equal(TaskbarEdge.Top, PopoverPlacement.DetectEdge(new PxPoint(900, 3), Monitor, Monitor));
        Assert.Equal(TaskbarEdge.Left, PopoverPlacement.DetectEdge(new PxPoint(2, 500), Monitor, Monitor));
        Assert.Equal(TaskbarEdge.Right, PopoverPlacement.DetectEdge(new PxPoint(1918, 500), Monitor, Monitor));
    }

    [Fact]
    public void Second_monitor_offsets_are_respected()
    {
        var monitor = new PxRect(1920, 0, 2560, 1440);
        var work = new PxRect(1920, 0, 2560, 1392);

        PxRect r = PopoverPlacement.Place(new PxPoint(3000, 1420), monitor, work, 412, 500, 15);

        Assert.True(r.X >= 1920 && r.Right <= 4480);
        Assert.Equal(1392 - 500 - 15, r.Y);
    }

    [Fact]
    public void A_popover_taller_than_the_work_area_is_shrunk()
    {
        PxRect r = PopoverPlacement.Place(new PxPoint(960, 1050), Monitor, WorkBottomTaskbar, 330, 5000, 12);

        Assert.Equal(1032 - 24, r.Height);
        Assert.Equal(12, r.Y);
    }

    [Fact]
    public void Unfold_grows_up_and_left_from_a_bottom_right_popover()
    {
        var popover = new PxRect(1578, 620, 330, 400);

        PxRect r = PopoverPlacement.Unfold(popover, 1160, 700, WorkBottomTaskbar);

        Assert.Equal(popover.Right, r.Right);
        Assert.Equal(popover.Bottom, r.Bottom);
        Assert.Equal(new PxRect(748, 320, 1160, 700), r);
    }

    [Fact]
    public void Unfold_grows_down_and_right_from_a_top_left_popover_and_stays_on_screen()
    {
        var popover = new PxRect(20, 60, 330, 400);

        PxRect r = PopoverPlacement.Unfold(popover, 1160, 700, WorkBottomTaskbar);

        Assert.Equal(20, r.X);
        Assert.Equal(60, r.Y);
        Assert.True(r.Right <= 1920 && r.Bottom <= 1032);
    }

    [Fact]
    public void Unfold_shrinks_to_a_small_work_area()
    {
        PxRect r = PopoverPlacement.Unfold(new PxRect(400, 300, 330, 400), 1160, 700, new PxRect(0, 0, 1024, 728));

        Assert.True(r.Width <= 1024 && r.Height <= 728);
        Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= 1024 && r.Bottom <= 728);
    }

    [Fact]
    public void Ease_out_starts_fast_ends_at_one_and_is_clamped()
    {
        Assert.Equal(0, PopoverPlacement.EaseOut(-1));
        Assert.Equal(1, PopoverPlacement.EaseOut(2));
        Assert.True(PopoverPlacement.EaseOut(0.5) > 0.5);
        Assert.True(PopoverPlacement.EaseOut(0.25) < PopoverPlacement.EaseOut(0.5));
    }

    [Fact]
    public void Lerp_hits_both_ends()
    {
        var a = new PxRect(10, 20, 330, 400);
        var b = new PxRect(110, 220, 1160, 700);

        Assert.Equal(a, PopoverPlacement.Lerp(a, b, 0));
        Assert.Equal(b, PopoverPlacement.Lerp(a, b, 1));
        Assert.Equal(new PxRect(60, 120, 745, 550), PopoverPlacement.Lerp(a, b, 0.5));
    }

    [Fact]
    public void A_launch_right_after_a_focus_loss_dismissal_is_suppressed()
    {
        Assert.True(PopoverPlacement.ShouldSuppressReopen(10_000, 10_150));
        Assert.False(PopoverPlacement.ShouldSuppressReopen(10_000, 10_500));
        Assert.False(PopoverPlacement.ShouldSuppressReopen(0, 10_000)); // no marker
        Assert.False(PopoverPlacement.ShouldSuppressReopen(10_000, 9_000)); // marker from before a reboot
    }
}

public sealed class DeferredWorkTests
{
    [Fact]
    public void Nothing_runs_until_started_and_each_item_is_posted_separately()
    {
        var posted = new Queue<Action>();
        var work = new DeferredWork(posted.Enqueue);
        var ran = new List<string>();
        work.Add("a", () => ran.Add("a"));
        work.Add("b", () => ran.Add("b"));

        Assert.Empty(posted);
        work.Start();
        Assert.Single(posted);
        Assert.Empty(ran);

        posted.Dequeue()();
        Assert.Equal(["a"], ran);
        Assert.Single(posted);

        posted.Dequeue()();
        Assert.Equal(["a", "b"], ran);
        Assert.Empty(posted);
        Assert.Equal(["a", "b"], work.Completed);
    }

    [Fact]
    public void A_failing_item_is_recorded_and_the_rest_still_run()
    {
        var posted = new Queue<Action>();
        var work = new DeferredWork(posted.Enqueue);
        bool ran = false;
        work.Add("bad", () => throw new InvalidOperationException());
        work.Add("good", () => ran = true);
        work.Start();

        while (posted.Count > 0)
        {
            posted.Dequeue()();
        }

        Assert.True(ran);
        Assert.Equal(["bad"], work.Failed);
        Assert.Equal(["good"], work.Completed);
    }

    [Fact]
    public void Items_added_after_start_are_posted_and_cancel_drops_the_rest()
    {
        var posted = new Queue<Action>();
        var work = new DeferredWork(posted.Enqueue);
        work.Start();
        Assert.Empty(posted);

        bool late = false;
        work.Add("late", () => late = true);
        Assert.Single(posted);
        posted.Dequeue()();
        Assert.True(late);

        work.Add("x", () => { });
        work.Cancel();
        while (posted.Count > 0)
        {
            posted.Dequeue()();
        }

        Assert.DoesNotContain("x", work.Completed);
        Assert.Equal(0, work.Pending);
    }

    [Fact]
    public void Start_twice_does_not_post_twice()
    {
        var posted = new Queue<Action>();
        var work = new DeferredWork(posted.Enqueue);
        work.Add("a", () => { });
        work.Start();
        work.Start();

        Assert.Single(posted);
    }
}

public sealed class PopoverModelTests
{
    [Fact]
    public void Summary_wording()
    {
        Assert.Equal("You're in Excel · 3 buttons changed", PopoverModel.Summary("Excel", 3));
        Assert.Equal("You're in Excel · 1 button changed", PopoverModel.Summary("Excel", 1));
        Assert.Equal("You're in Chrome · no buttons changed", PopoverModel.Summary("Chrome", 0));
        Assert.Null(PopoverModel.Summary(null, 2));
        Assert.Null(PopoverModel.Summary("  ", 2));
    }

    [Fact]
    public void Firmware_is_read_per_device()
    {
        const string json = """
            {"devices":[{"id":"mx-master-4","name":"MX Master 4","connected":true,"firmware":"RBM12.00_B0011"},
                        {"id":"ergo-k860","firmwareVersion":"MPK10.03_B0004"}],
             "optionsPlusRunning":false,"foregroundApp":"excel","updatedAt":"2026-10-15T01:02:03+00:00"}
            """;

        IReadOnlyDictionary<string, string> fw = PopoverModel.ParseFirmware(json);

        Assert.Equal("RBM12.00_B0011", fw["mx-master-4"]);
        Assert.Equal("MPK10.03_B0004", fw["ergo-k860"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"devices\":null}")]
    [InlineData("{\"devices\":{}}")]
    [InlineData("{\"devices\":[1,\"x\",null,[]]}")]
    [InlineData("{\"devices\":[{\"name\":\"no id\",\"firmware\":\"1\"}]}")]
    [InlineData("{\"devices\":[{\"id\":7,\"firmware\":\"1\"}]}")]
    [InlineData("{\"devices\":[{\"id\":\"mx-master-4\"}]}")]
    [InlineData("{\"devices\":[{\"id\":\"mx-master-4\",\"firmware\":null}]}")]
    [InlineData("{\"devices\":[{\"id\":\"mx-master-4\",\"firmware\":\"  \"}]}")]
    [InlineData("{\"devices\":[{\"id\":\"mx-master-4\",\"firmware\":{\"a\":1}}]}")]
    public void Missing_or_odd_firmware_gives_no_entry_and_never_throws(string? json)
    {
        Assert.Empty(PopoverModel.ParseFirmware(json));
    }

    [Fact]
    public void Firmware_tolerates_case_numbers_and_a_bad_sibling()
    {
        const string json = "{\"Devices\":[{\"ID\":\"mx-master-4\",\"Firmware\":12.5},{\"id\":\"ergo-k860\",\"firmware\":false}]}";

        IReadOnlyDictionary<string, string> fw = PopoverModel.ParseFirmware(json);

        Assert.Equal("12.5", fw["mx-master-4"]);
        Assert.False(fw.ContainsKey("ergo-k860"));
    }

    [Fact]
    public void Existing_status_files_without_firmware_still_parse_in_the_engine_reader()
    {
        const string json = "{\"devices\":[{\"id\":\"mx-master-4\",\"name\":\"MX Master 4\",\"connected\":true,\"batteryPercent\":80,\"charging\":false,\"firmware\":\"x\"}],\"optionsPlusRunning\":false,\"foregroundApp\":null,\"updatedAt\":\"2026-10-15T01:02:03+00:00\"}";

        Slender.Engine.SlenderStatus? s = Slender.Engine.Config.StatusJson.Parse(json);

        Assert.NotNull(s);
        Assert.Equal(80, s!.Devices[0].BatteryPercent);
    }
}
