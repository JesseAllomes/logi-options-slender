using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class TrayBadgeControllerTests
{
    private static (TrayBadgeController Badge, List<(int Kind, bool Animated, int Size, int Fill)> Calls) Make(Func<int, nint[]>? frames = null)
    {
        var calls = new List<(int, bool, int, int)>();
        nint next = 1000;
        var badge = new TrayBadgeController((kind, animated, size, fill, _) =>
        {
            calls.Add(((int)kind, animated, size, fill));
            int n = BadgeTiming.FrameCount(kind, animated);
            return frames is null ? Enumerable.Range(0, n).Select(_ => next++).ToArray() : frames(n);
        });
        return (badge, calls);
    }

    [Fact]
    public void Nothing_exists_without_a_badge()
    {
        var (badge, calls) = Make();
        Assert.False(badge.Set((BadgeKind)0, true, 16, 0, 1, 0));
        Assert.Equal(0, badge.FrameCount);
        Assert.Equal(0, badge.CurrentIcon);
        Assert.False(badge.NeedsTimer);
        Assert.Empty(calls);
    }

    [Fact]
    public void Frames_render_once_per_state_and_the_timer_runs_only_while_animating()
    {
        var (badge, calls) = Make();
        Assert.True(badge.Set(BadgeKind.Low, true, 16, 1, 1, 0));
        Assert.True(badge.NeedsTimer);
        Assert.False(badge.Set(BadgeKind.Low, true, 16, 1, 1, 50)); // same state: no re-render, no icon change
        Assert.Single(calls);

        Assert.True(badge.Set(BadgeKind.Full, true, 16, 5, 1, 100));
        Assert.False(badge.NeedsTimer);
        Assert.Equal(1, badge.FrameCount);

        Assert.True(badge.Set(BadgeKind.None, true, 16, 0, 1, 200)); // the plain icon comes back
        Assert.Equal(0, badge.CurrentIcon);
        Assert.Equal(0, badge.FrameCount);
        Assert.False(badge.NeedsTimer);
    }

    [Fact]
    public void Reduced_motion_renders_one_static_frame_and_no_timer()
    {
        var (badge, calls) = Make();
        badge.Set(BadgeKind.Charging, false, 16, 3, 1, 0);
        Assert.Equal(1, badge.FrameCount);
        Assert.False(badge.NeedsTimer);
        Assert.Equal(((int)BadgeKind.Charging, false, 16, 3), calls[0]);
    }

    [Fact]
    public void Size_change_rebuilds_the_frames()
    {
        var (badge, calls) = Make();
        badge.Set(BadgeKind.Charging, true, 16, 3, 1, 0);
        Assert.True(badge.Set(BadgeKind.Charging, true, 24, 3, 1, 10));
        Assert.Equal(2, calls.Count);
        Assert.Equal(24, calls[1].Size);
    }

    [Fact]
    public void Frames_rebuild_only_when_the_drawn_width_changes()
    {
        var (badge, calls) = Make();
        badge.Set(BadgeKind.Charging, true, 16, 3, 1, 0);
        Assert.False(badge.Set(BadgeKind.Charging, true, 16, 3, 1, 100)); // the same drawn width is a no-op, however many readings
        Assert.False(badge.Set(BadgeKind.Charging, true, 16, 3, 1, 200));
        Assert.Single(calls);
        Assert.True(badge.Set(BadgeKind.Charging, true, 16, 4, 1, 300)); // one more pixel: rebuild
        Assert.Equal(2, calls.Count);
        Assert.Equal(4, calls[1].Fill);
    }

    [Fact]
    public void Tick_reports_only_real_frame_changes()
    {
        var (badge, _) = Make();
        badge.Set(BadgeKind.Low, true, 16, 1, 1, 1000);
        Assert.False(badge.Tick(1000));
        Assert.False(badge.Tick(1040)); // still inside frame 0 (80 ms frames)
        Assert.True(badge.Tick(1090));
        Assert.False(badge.Tick(1100));
        Assert.True(badge.Tick(1000 + 1599));   // last frame
        Assert.True(badge.Tick(1000 + 1600));   // wrapped to frame 0
    }

    [Fact]
    public void Failed_render_means_no_badge()
    {
        var (badge, _) = Make(_ => []);
        Assert.False(badge.Set(BadgeKind.Low, true, 16, 1, 1, 0));
        Assert.Equal(0, badge.CurrentIcon);
        Assert.False(badge.NeedsTimer);
        Assert.Equal(BadgeKind.None, badge.Kind);
    }
}
