namespace Slender.Engine.Ring;

/// <summary>One animated piece of the ring (hub, backing disc, rim or a slot): a transform about the ring centre plus an opacity.</summary>
public struct RingPart
{
    /// <summary>Uniform scale about the ring centre.</summary>
    public double Scale;

    /// <summary>0..1, multiplied into everything the part draws.</summary>
    public double Opacity;

    /// <summary>Translation in DIPs, applied after the scale and rotation.</summary>
    public double Dx, Dy;

    /// <summary>Clockwise rotation about the ring centre, in degrees.</summary>
    public double Rotation;

    public static RingPart Identity => new() { Scale = 1, Opacity = 1 };
}

/// <summary>The pose of every part for one frame. Reused from frame to frame so animating allocates nothing.</summary>
public sealed class RingPose
{
    public RingPart Hub, Disc, Rim;
    public readonly RingPart[] Slots = new RingPart[RingLayout.SlotCount];

    public void SetAll(RingPart part)
    {
        Hub = Disc = Rim = part;
        Array.Fill(Slots, part);
    }
}

/// <summary>
/// Timing and shape of the ring's opening and closing animation, with no window or drawing in it so it can be tested.
/// Speed picks a factor, each opening has a base duration, closing takes three quarters of opening; <see cref="Evaluate"/>
/// turns a progress 0..1 into the pose of each part. The animation is only visual: input never waits for it.
/// </summary>
public static class RingMotion
{
    /// <summary>Length of the plain fade used for Off and when Windows animations are switched off.</summary>
    public const double FadeMs = 90;

    public const double CloseRatio = 0.75;

    /// <summary>How far a fly-in slot starts from its place, at Medium size (DIPs).</summary>
    public const double FlyInDistance = 46;

    /// <summary>How far a slot slides out when the ring closes in fly-in style, at Medium size (DIPs).</summary>
    public const double FlyOutDistance = 40;

    private const double BloomSlotShare = 0.43, FlyInSlotShare = 0.5, FlyOutSlotShare = 0.55;

    /// <summary>Multiplies the base duration. Quickest is 1.0 and Slowest 2.6.</summary>
    public static double Factor(RingAnimationSpeed speed) => speed switch
    {
        RingAnimationSpeed.Slowest => 2.6,
        RingAnimationSpeed.Slow => 2.2,
        RingAnimationSpeed.Quick => 1.4,
        RingAnimationSpeed.Quickest => 1.0,
        _ => 1.8,
    };

    private static double BaseOpenMs(RingOpening opening) => opening switch
    {
        RingOpening.Pop => 240,
        RingOpening.FlyIn => 400,
        _ => 440,
    };

    /// <summary>Off, or any style when Windows client-area animations are off, collapses to the 90 ms fade.</summary>
    public static RingOpening Effective(RingOpening opening, bool animationsEnabled) =>
        animationsEnabled ? opening : RingOpening.Off;

    public static double OpenMs(RingOpening opening, RingAnimationSpeed speed) =>
        opening == RingOpening.Off ? FadeMs : BaseOpenMs(opening) * Factor(speed);

    public static double CloseMs(RingOpening opening, RingAnimationSpeed speed) =>
        opening == RingOpening.Off ? FadeMs : BaseOpenMs(opening) * CloseRatio * Factor(speed);

    /// <summary>Elapsed time as a fraction of the duration, clamped to 0..1.</summary>
    public static double Progress(double elapsedMs, double durationMs)
    {
        if (!(durationMs > 0)) return 1;
        return Math.Clamp(elapsedMs / durationMs, 0, 1);
    }

    /// <summary>Progress through a window that starts at <paramref name="start"/> and lasts <paramref name="length"/> (both fractions of the total), clamped to 0..1.</summary>
    public static double Window(double t, double start, double length) => Progress(t - start, length);

    // ---- easing ----

    /// <summary>CSS cubic-bezier(.2,.8,.3,1): fast start, soft landing.</summary>
    public static double EaseOut(double t) => CubicBezier(.2, .8, .3, 1, Math.Clamp(t, 0, 1));

    /// <summary>CSS ease-in, cubic-bezier(.42,0,1,1).</summary>
    public static double EaseIn(double t) => CubicBezier(.42, 0, 1, 1, Math.Clamp(t, 0, 1));

    /// <summary>The y of a CSS cubic-bezier(x1, y1, x2, y2) timing curve at x (Newton steps, bisection as the fallback).</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double s = x;
        for (var i = 0; i < 6; i++)
        {
            double err = Sample(x1, x2, s) - x;
            if (Math.Abs(err) < 1e-6) return Sample(y1, y2, s);
            double d = Slope(x1, x2, s);
            if (Math.Abs(d) < 1e-6) break;
            s -= err / d;
        }

        double lo = 0, hi = 1;
        s = x;
        for (var i = 0; i < 24; i++)
        {
            double v = Sample(x1, x2, s);
            if (Math.Abs(v - x) < 1e-6) break;
            if (v < x) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }

        return Sample(y1, y2, s);
    }

    private static double Sample(double a, double b, double s) => ((1 - 3 * b + 3 * a) * s + (3 * b - 6 * a)) * s * s + 3 * a * s;

    private static double Slope(double a, double b, double s) => 3 * (1 - 3 * b + 3 * a) * s * s + 2 * (3 * b - 6 * a) * s + 3 * a;

    /// <summary>
    /// A value that runs from <paramref name="from"/> to <paramref name="peak"/> at <paramref name="peakAt"/> of the way, then
    /// settles on <paramref name="to"/>, each leg eased out (the overshoot keyframes).
    /// </summary>
    public static double Overshoot(double u, double from, double peak, double to, double peakAt)
    {
        u = Math.Clamp(u, 0, 1);
        return u < peakAt
            ? from + (peak - from) * EaseOut(u / peakAt)
            : peak + (to - peak) * EaseOut((u - peakAt) / (1 - peakAt));
    }

    // ---- poses ----

    /// <summary>Start of slot <paramref name="index"/>'s window in a clockwise stagger where the last slot ends exactly at the total.</summary>
    public static double SlotStart(int index, double share) => index * (1 - share) / (RingLayout.SlotCount - 1);

    /// <summary>Fly-in offset (DIPs) of a slot at its own progress u (0 = fully outside), along its bisector.</summary>
    public static (double Dx, double Dy) FlyInOffset(int index, double u, double sizeScale) =>
        Along(index, FlyInDistance * sizeScale * (1 - EaseOut(u)));

    private static (double Dx, double Dy) Along(int index, double distance)
    {
        double a = RingLayout.AngleOf(index) * Math.PI / 180;
        return (distance * Math.Cos(a), distance * Math.Sin(a));
    }

    /// <summary>
    /// Fills <paramref name="pose"/> for progress <paramref name="t"/> (0..1, clamped) of an opening or closing animation.
    /// <paramref name="sizeScale"/> is the ring size factor (0.8 / 1 / 1.2) that distances scale with. Off is the plain fade.
    /// </summary>
    public static void Evaluate(RingOpening opening, bool closing, double t, double sizeScale, RingPose pose)
    {
        t = Math.Clamp(t, 0, 1);
        pose.SetAll(RingPart.Identity);
        switch (opening)
        {
            case RingOpening.Pop: Pop(closing, t, pose); break;
            case RingOpening.Bloom: Bloom(closing, t, pose); break;
            case RingOpening.FlyIn: FlyIn(closing, t, sizeScale, pose); break;
            default: Fade(closing, t, pose); break;
        }
    }

    private static void Fade(bool closing, double t, RingPose pose)
    {
        var part = RingPart.Identity;
        part.Opacity = closing ? 1 - t : t;
        pose.SetAll(part);
    }

    private static void Pop(bool closing, double t, RingPose pose)
    {
        var part = RingPart.Identity;
        if (closing)
        {
            double e = EaseIn(t);
            part.Scale = 1 - 0.14 * e;
            part.Opacity = 1 - e;
        }
        else
        {
            part.Scale = Overshoot(t, 0.70, 1.035, 1, 0.72);
            part.Opacity = EaseOut(t);
        }

        pose.SetAll(part);
    }

    private static void Bloom(bool closing, double t, RingPose pose)
    {
        int n = RingLayout.SlotCount;
        if (!closing)
        {
            double hub = EaseOut(Window(t, 0, 0.36));
            pose.Hub = new RingPart { Scale = 0.4 + 0.6 * hub, Opacity = hub };
            double disc = EaseOut(Window(t, 0, 0.60));
            pose.Disc = new RingPart { Scale = 0.5 + 0.5 * disc, Opacity = disc };
            double rim = EaseOut(t);
            pose.Rim = new RingPart { Scale = 1, Opacity = rim, Rotation = -60 * (1 - rim) };
            for (var i = 0; i < n; i++)
            {
                double u = Window(t, SlotStart(i, BloomSlotShare), BloomSlotShare);
                pose.Slots[i] = new RingPart { Scale = Overshoot(u, 0.35, 1.05, 1, 0.7), Opacity = EaseOut(u) };
            }

            return;
        }

        // Anticlockwise: slot 7 goes first, slot 0 last; the slots take the first 60 %, then the hub, then the whole ring fades.
        const double slotShare = 0.3, slotSpan = 0.6;
        for (var i = 0; i < n; i++)
        {
            double u = EaseIn(Window(t, (n - 1 - i) * (slotSpan - slotShare) / (n - 1), slotShare));
            pose.Slots[i] = new RingPart { Scale = 1 - 0.5 * u, Opacity = 1 - u };
        }

        double h = EaseIn(Window(t, 0.5, 0.3));
        pose.Hub = new RingPart { Scale = 1 - 0.5 * h, Opacity = 1 - h };
        double ring = 1 - EaseIn(Window(t, 0.6, 0.4));
        pose.Disc = new RingPart { Scale = 1, Opacity = ring };
        pose.Rim = new RingPart { Scale = 1, Opacity = ring };
    }

    private static void FlyIn(bool closing, double t, double sizeScale, RingPose pose)
    {
        int n = RingLayout.SlotCount;
        if (!closing)
        {
            for (var i = 0; i < n; i++)
            {
                double u = Window(t, SlotStart(i, FlyInSlotShare), FlyInSlotShare);
                var (dx, dy) = FlyInOffset(i, u, sizeScale);
                pose.Slots[i] = new RingPart { Scale = 1, Opacity = EaseOut(u), Dx = dx, Dy = dy, Rotation = Overshoot(u, -10, 1.5, 0, 0.7) };
            }

            double rim = EaseOut(t);
            pose.Rim = new RingPart { Scale = 1.15 - 0.15 * rim, Opacity = rim, Rotation = 90 * (1 - rim) };
            pose.Disc = new RingPart { Scale = 1, Opacity = EaseOut(Window(t, 0, 0.5)) };
            pose.Hub = new RingPart { Scale = 1, Opacity = EaseOut(Window(t, 0.12, 0.4)) };
            return;
        }

        // Slots are thrown outward with a small clockwise stagger, then the rest of the ring fades.
        for (var i = 0; i < n; i++)
        {
            double u = EaseIn(Window(t, i * 0.25 / (n - 1), FlyOutSlotShare));
            var (dx, dy) = Along(i, FlyOutDistance * sizeScale * u);
            pose.Slots[i] = new RingPart { Scale = 1, Opacity = 1 - u, Dx = dx, Dy = dy };
        }

        double ring = 1 - EaseIn(Window(t, 0.5, 0.5));
        var rest = new RingPart { Scale = 1, Opacity = ring };
        pose.Hub = pose.Disc = pose.Rim = rest;
    }
}
