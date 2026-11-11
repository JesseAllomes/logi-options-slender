using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class TrayBadgeControllerTests
{
    private static (TrayBadgeController Badge, List<(int Kind, bool Animated, int Size)> Calls) Make(Func<int, nint[]>? frames = null)
    {
        var calls = new List<(int, bool, int)>();
        nint next = 1000;
        var badge = new TrayBadgeController((kind, animated, size, _) =>
        {
            calls.Add(((int)kind, animated, size));
            int n = BadgeTiming.FrameCount(kind, animated);
            return frames is null ? Enumerable.Range(0, n).Select(_ => next++).ToArray() : frames(n);
        });
        return (badge, calls);
    }

    [Fact]
    public void Nothing_exists_without_a_badge()
    {
        var (badge, calls) = Make();
        Assert.False(badge.Set((BadgeKind)0, true, 16, 1, 0));
        Assert.Equal(0, badge.FrameCount);
        Assert.Equal(0, badge.CurrentIcon);
        Assert.False(badge.NeedsTimer);
        Assert.Empty(calls);
    }

    [Fact]
    public void Frames_render_once_per_state_and_the_timer_runs_only_while_animating()
    {
        var (badge, calls) = Make();
        Assert.True(badge.Set(BadgeKind.Low, true, 16, 1, 0));
        Assert.True(badge.NeedsTimer);
        Assert.False(badge.Set(BadgeKind.Low, true, 16, 1, 50)); // same state: no re-render, no icon change
        Assert.Single(calls);

        Assert.True(badge.Set(BadgeKind.Charging, true, 16, 1, 100)); // Low to Charging: a fresh loop
        Assert.True(badge.NeedsTimer);
        Assert.Equal(2, calls.Count);

        Assert.True(badge.Set(BadgeKind.None, true, 16, 1, 200)); // the plain icon comes back
        Assert.Equal(0, badge.CurrentIcon);
        Assert.Equal(0, badge.FrameCount);
        Assert.False(badge.NeedsTimer);
    }

    [Fact]
    public void Reduced_motion_renders_one_static_frame_and_no_timer()
    {
        var (badge, calls) = Make();
        badge.Set(BadgeKind.Charging, false, 16, 1, 0);
        Assert.Equal(1, badge.FrameCount);
        Assert.False(badge.NeedsTimer);
        Assert.Equal(((int)BadgeKind.Charging, false, 16), calls[0]);
    }

    [Fact]
    public void Size_change_rebuilds_the_frames()
    {
        var (badge, calls) = Make();
        badge.Set(BadgeKind.Charging, true, 16, 1, 0);
        Assert.True(badge.Set(BadgeKind.Charging, true, 24, 1, 10));
        Assert.Equal(2, calls.Count);
        Assert.Equal(24, calls[1].Size);
    }

    [Fact]
    public void Frames_are_released_when_the_state_ends_or_the_size_changes()
    {
        var destroyed = new List<nint>();
        var badge = new TrayBadgeController((kind, animated, size, _) => animated ? [11, 12, 12, 13] : [21], destroyed.Add);
        badge.Set(BadgeKind.Low, true, 16, 1, 0);
        Assert.Empty(destroyed);
        badge.Set(BadgeKind.Low, true, 24, 1, 5); // size change: old frames go, shared handle once
        Assert.Equal([11, 12, 13], destroyed.OrderBy(x => x));
        destroyed.Clear();
        badge.Set(BadgeKind.None, true, 24, 1, 10);
        Assert.Equal(3, destroyed.Count);
        Assert.Equal(0, badge.FrameCount);
        destroyed.Clear();
        badge.Set(BadgeKind.Charging, false, 24, 1, 20);
        badge.Dispose();
        Assert.Equal([21], destroyed);
    }

    [Fact]
    public void Tick_reports_only_real_frame_changes()
    {
        var (badge, _) = Make();
        badge.Set(BadgeKind.Low, true, 16, 1, 1000);
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
        Assert.False(badge.Set(BadgeKind.Low, true, 16, 1, 0));
        Assert.Equal(0, badge.CurrentIcon);
        Assert.False(badge.NeedsTimer);
        Assert.Equal(BadgeKind.None, badge.Kind);
    }
}
