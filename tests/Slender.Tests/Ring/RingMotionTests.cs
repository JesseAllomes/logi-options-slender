using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tests.Ring;

public sealed class RingMotionTests
{
    private static RingPose Pose(RingOpening opening, bool closing, double t, double size = 1)
    {
        var pose = new RingPose();
        RingMotion.Evaluate(opening, closing, t, size, pose);
        return pose;
    }

    [Theory]
    [InlineData(RingAnimationSpeed.Quickest, 1.0)]
    [InlineData(RingAnimationSpeed.Quick, 1.4)]
    [InlineData(RingAnimationSpeed.Normal, 1.8)]
    [InlineData(RingAnimationSpeed.Slow, 2.2)]
    [InlineData(RingAnimationSpeed.Slowest, 2.6)]
    public void Speed_factor_table(RingAnimationSpeed speed, double factor) => Assert.Equal(factor, RingMotion.Factor(speed));

    [Theory]
    [InlineData(RingOpening.Pop, 240, 180)]
    [InlineData(RingOpening.Bloom, 440, 330)]
    [InlineData(RingOpening.FlyIn, 400, 300)]
    public void Base_durations_at_the_quickest_speed(RingOpening opening, double open, double close)
    {
        Assert.Equal(open, RingMotion.OpenMs(opening, RingAnimationSpeed.Quickest), 6);
        Assert.Equal(close, RingMotion.CloseMs(opening, RingAnimationSpeed.Quickest), 6);
    }

    [Fact]
    public void Normal_bloom_opens_in_792_and_closes_in_594_ms()
    {
        Assert.Equal(792, RingMotion.OpenMs(RingOpening.Bloom, RingAnimationSpeed.Normal), 6);
        Assert.Equal(594, RingMotion.CloseMs(RingOpening.Bloom, RingAnimationSpeed.Normal), 6);
    }

    [Fact]
    public void Close_is_three_quarters_of_open_for_every_style_and_speed()
    {
        foreach (var opening in new[] { RingOpening.Pop, RingOpening.Bloom, RingOpening.FlyIn })
        foreach (var speed in Enum.GetValues<RingAnimationSpeed>())
            Assert.Equal(RingMotion.OpenMs(opening, speed) * 0.75, RingMotion.CloseMs(opening, speed), 6);
    }

    [Fact]
    public void Off_and_reduced_motion_are_a_90_ms_fade()
    {
        foreach (var speed in Enum.GetValues<RingAnimationSpeed>())
        {
            Assert.Equal(90, RingMotion.OpenMs(RingOpening.Off, speed));
            Assert.Equal(90, RingMotion.CloseMs(RingOpening.Off, speed));
        }

        Assert.Equal(RingOpening.Off, RingMotion.Effective(RingOpening.Bloom, animationsEnabled: false));
        Assert.Equal(RingOpening.Bloom, RingMotion.Effective(RingOpening.Bloom, animationsEnabled: true));

        var half = Pose(RingOpening.Off, closing: false, 0.5);
        Assert.Equal(0.5, half.Hub.Opacity, 6);
        Assert.Equal(1, half.Slots[3].Scale);
        Assert.Equal(0, half.Slots[3].Dx);
        Assert.Equal(0.5, Pose(RingOpening.Off, closing: true, 0.5).Rim.Opacity, 6);
    }

    [Fact]
    public void Progress_is_clamped_to_zero_and_one()
    {
        Assert.Equal(0, RingMotion.Progress(-50, 200));
        Assert.Equal(0.25, RingMotion.Progress(50, 200));
        Assert.Equal(1, RingMotion.Progress(900, 200));
        Assert.Equal(1, RingMotion.Progress(10, 0));

        foreach (var opening in Enum.GetValues<RingOpening>())
        foreach (var closing in new[] { false, true })
        foreach (var t in new[] { -3.0, 0, 0.5, 1, 7 })
        {
            var pose = Pose(opening, closing, t);
            foreach (var part in pose.Slots.Append(pose.Hub).Append(pose.Disc).Append(pose.Rim))
                Assert.InRange(part.Opacity, 0, 1);
        }

        Assert.Equal(Pose(RingOpening.Pop, false, 1).Hub.Scale, Pose(RingOpening.Pop, false, 40).Hub.Scale);
        Assert.Equal(Pose(RingOpening.Pop, false, 0).Hub.Scale, Pose(RingOpening.Pop, false, -40).Hub.Scale);
    }

    [Fact]
    public void Easing_runs_from_zero_to_one()
    {
        Assert.Equal(0, RingMotion.EaseOut(0));
        Assert.Equal(1, RingMotion.EaseOut(1));
        Assert.True(RingMotion.EaseOut(0.3) > 0.3); // fast start
        Assert.Equal(0, RingMotion.EaseIn(0));
        Assert.Equal(1, RingMotion.EaseIn(1));
        Assert.True(RingMotion.EaseIn(0.3) < 0.3);  // slow start
    }

    [Fact]
    public void Overshoot_keyframes_hit_their_values()
    {
        Assert.Equal(0.70, RingMotion.Overshoot(0, 0.70, 1.035, 1, 0.72), 6);
        Assert.Equal(1.035, RingMotion.Overshoot(0.72, 0.70, 1.035, 1, 0.72), 6);
        Assert.Equal(1.0, RingMotion.Overshoot(1, 0.70, 1.035, 1, 0.72), 6);

        Assert.Equal(0.70, Pose(RingOpening.Pop, false, 0).Hub.Scale, 6);
        Assert.Equal(1.035, Pose(RingOpening.Pop, false, 0.72).Hub.Scale, 6);
        Assert.Equal(1.0, Pose(RingOpening.Pop, false, 1).Hub.Scale, 6);
    }

    [Fact]
    public void Pop_closes_to_86_percent_and_fades_out()
    {
        var start = Pose(RingOpening.Pop, true, 0);
        var end = Pose(RingOpening.Pop, true, 1);
        Assert.Equal(1, start.Disc.Scale, 6);
        Assert.Equal(1, start.Disc.Opacity, 6);
        Assert.Equal(0.86, end.Disc.Scale, 6);
        Assert.Equal(0, end.Disc.Opacity, 6);
    }

    [Fact]
    public void Bloom_slots_start_clockwise_from_the_top_and_the_last_ends_at_the_total()
    {
        var starts = Enumerable.Range(0, RingLayout.SlotCount).Select(i => RingMotion.SlotStart(i, 0.43)).ToArray();
        Assert.Equal(0, starts[0]);
        Assert.Equal(starts.OrderBy(x => x), starts);
        Assert.Equal(1.0, starts[^1] + 0.43, 9);

        var early = Pose(RingOpening.Bloom, false, 0.2);
        for (var i = 1; i < RingLayout.SlotCount; i++)
            Assert.True(early.Slots[i - 1].Opacity >= early.Slots[i].Opacity); // the slot at 12 o'clock is always ahead
        Assert.True(early.Slots[0].Opacity > 0);
        Assert.Equal(0, early.Slots[7].Opacity);

        var done = Pose(RingOpening.Bloom, false, 1);
        foreach (var slot in done.Slots)
        {
            Assert.Equal(1, slot.Scale, 6);
            Assert.Equal(1, slot.Opacity, 6);
        }

        // Hub first (36 %), disc by 60 %, rim over the whole time.
        Assert.Equal(1, Pose(RingOpening.Bloom, false, 0.36).Hub.Opacity, 6);
        Assert.Equal(1, Pose(RingOpening.Bloom, false, 0.6).Disc.Scale, 6);
        Assert.Equal(-60, Pose(RingOpening.Bloom, false, 0).Rim.Rotation, 6);
        Assert.Equal(0, done.Rim.Rotation, 6);
        Assert.Equal(0.4, Pose(RingOpening.Bloom, false, 0).Hub.Scale, 6);
        Assert.Equal(0.5, Pose(RingOpening.Bloom, false, 0).Disc.Scale, 6);
    }

    [Fact]
    public void Bloom_close_runs_anticlockwise_then_hub_then_the_ring()
    {
        var early = Pose(RingOpening.Bloom, true, 0.15);
        for (var i = 1; i < RingLayout.SlotCount; i++)
            Assert.True(early.Slots[i].Opacity <= early.Slots[i - 1].Opacity); // slot 7 leaves first
        Assert.True(early.Slots[7].Opacity < 1);
        Assert.Equal(1, early.Slots[0].Opacity, 6);

        var afterSlots = Pose(RingOpening.Bloom, true, 0.6);
        foreach (var slot in afterSlots.Slots)
        {
            Assert.Equal(0.5, slot.Scale, 6);
            Assert.Equal(0, slot.Opacity, 6);
        }

        Assert.True(afterSlots.Hub.Opacity > 0);
        Assert.Equal(1, afterSlots.Disc.Opacity, 6);
        Assert.Equal(0, Pose(RingOpening.Bloom, true, 1).Disc.Opacity, 6);
        Assert.Equal(0, Pose(RingOpening.Bloom, true, 1).Hub.Opacity, 6);
    }

    [Fact]
    public void Fly_in_slots_slide_along_their_own_bisector()
    {
        for (var i = 0; i < RingLayout.SlotCount; i++)
        {
            var (dx, dy) = RingMotion.FlyInOffset(i, 0, 1);
            double a = RingLayout.AngleOf(i) * Math.PI / 180;
            Assert.Equal(46 * Math.Cos(a), dx, 6);
            Assert.Equal(46 * Math.Sin(a), dy, 6);
            var (ex, ey) = RingMotion.FlyInOffset(i, 1, 1);
            Assert.Equal(0, ex, 6);
            Assert.Equal(0, ey, 6);
        }

        var up = RingMotion.FlyInOffset(0, 0, 1);
        Assert.Equal(0, up.Dx, 6);
        Assert.Equal(-46, up.Dy, 6);
        var right = RingMotion.FlyInOffset(2, 0, 1);
        Assert.Equal(46, right.Dx, 6);
        Assert.Equal(0, right.Dy, 6);
        Assert.Equal(46 * 1.2, RingMotion.FlyInOffset(2, 0, 1.2).Dx, 6);
    }

    [Fact]
    public void Fly_in_twists_through_the_overshoot_and_settles()
    {
        Assert.Equal(-10, Pose(RingOpening.FlyIn, false, 0).Slots[0].Rotation, 6);
        Assert.Equal(0, Pose(RingOpening.FlyIn, false, 1).Slots[7].Rotation, 6);
        // Slot 0 spans 0..50 % of the time; 70 % of that is 35 %.
        Assert.Equal(1.5, Pose(RingOpening.FlyIn, false, 0.35).Slots[0].Rotation, 6);

        var start = Pose(RingOpening.FlyIn, false, 0);
        Assert.Equal(90, start.Rim.Rotation, 6);
        Assert.Equal(1.15, start.Rim.Scale, 6);
        var end = Pose(RingOpening.FlyIn, false, 1);
        Assert.Equal(0, end.Rim.Rotation, 6);
        Assert.Equal(1, end.Rim.Scale, 6);
        Assert.Equal(1, end.Hub.Opacity, 6);
        Assert.Equal(0, start.Hub.Opacity, 6);
    }

    [Fact]
    public void Fly_in_close_throws_slots_40_px_outward_with_a_clockwise_stagger()
    {
        var done = Pose(RingOpening.FlyIn, true, 0.8);
        Assert.Equal(40, done.Slots[2].Dx, 6);
        Assert.Equal(0, done.Slots[2].Dy, 6);
        Assert.Equal(0, done.Slots[2].Opacity, 6);

        var early = Pose(RingOpening.FlyIn, true, 0.2);
        Assert.True(early.Slots[0].Opacity <= early.Slots[7].Opacity);
        Assert.Equal(0, Pose(RingOpening.FlyIn, true, 1).Disc.Opacity, 6);
    }
}
