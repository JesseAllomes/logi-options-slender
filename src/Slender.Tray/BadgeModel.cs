using Slender.Hidpp;

namespace Slender.Tray;

/// <summary>What the small battery badge on the tray icon shows.</summary>
internal enum BadgeKind { None, Charging, Full, Low }

/// <summary>What the badge shows and the battery level (0..100) its fill stands for.</summary>
internal readonly record struct BadgeState(BadgeKind Kind, int Percent);

/// <summary>One device as the badge sees it: connection, last battery reading and the low-battery line for that device.</summary>
internal readonly record struct BadgeInput(bool Connected, BatteryStatus? Battery, int ThresholdPercent);

/// <summary>Pure decision of which badge to show. Low beats Charging, Charging beats Full, an Error reading shows nothing.</summary>
internal static class BadgeModel
{
    public static BadgeKind Decide(BadgeInput mouse, BadgeInput keyboard, bool alertsEnabled) =>
        DecideState(mouse, keyboard, alertsEnabled).Kind;

    /// <summary>The badge and the battery level it shows: the winning device's percent (the lower one when both tie).</summary>
    public static BadgeState DecideState(BadgeInput mouse, BadgeInput keyboard, bool alertsEnabled)
    {
        BadgeKind a = One(mouse, alertsEnabled), b = One(keyboard, alertsEnabled);
        int pa = mouse.Battery?.Percent ?? 0, pb = keyboard.Battery?.Percent ?? 0;
        bool takeMouse = Rank(a) > Rank(b) || (Rank(a) == Rank(b) && pa <= pb);
        var kind = takeMouse ? a : b;
        return kind == BadgeKind.None ? new BadgeState(BadgeKind.None, 0) : new BadgeState(kind, Math.Clamp(takeMouse ? pa : pb, 0, 100));
    }

    private static BadgeKind One(BadgeInput d, bool alertsEnabled)
    {
        if (!d.Connected || d.Battery is not { } b || b.State == ChargeState.Error) return BadgeKind.None;
        if (b.Charging) return b.State == ChargeState.Full ? BadgeKind.Full : BadgeKind.Charging;
        return alertsEnabled && b.Percent <= d.ThresholdPercent ? BadgeKind.Low : BadgeKind.None;
    }

    private static int Rank(BadgeKind k) => k switch { BadgeKind.Low => 3, BadgeKind.Charging => 2, BadgeKind.Full => 1, _ => 0 };
}

/// <summary>Pure frame timing for the animated badges (about 12 fps).</summary>
internal static class BadgeTiming
{
    public const int TimerIntervalMs = 80;
    /// <summary>Charging loop: the S holds, crossfades to a bolt, the bolt holds, crossfades back.</summary>
    public const int ChargeHoldMs = 1200, ChargeFadeMs = 400, LowPeriodMs = 1600;

    public static int PeriodMs(BadgeKind kind) => kind switch
    {
        BadgeKind.Charging => 2 * (ChargeHoldMs + ChargeFadeMs),
        BadgeKind.Low => LowPeriodMs,
        _ => 0,
    };

    private static bool Animates(BadgeKind kind, bool animationsOn) => animationsOn && (kind == BadgeKind.Charging || kind == BadgeKind.Low);

    /// <summary>A timer is needed only for Charging and Low, and only while Windows animations are on.</summary>
    public static bool NeedsTimer(BadgeKind kind, bool animationsOn) => Animates(kind, animationsOn);

    /// <summary>Frames to render: 0 for no badge, 1 for a static badge, one loop of frames for an animated one.</summary>
    public static int FrameCount(BadgeKind kind, bool animationsOn) =>
        kind == BadgeKind.None ? 0 : Animates(kind, animationsOn) ? (int)Math.Round(PeriodMs(kind) / (double)TimerIntervalMs) : 1;

    public static int FrameIndex(BadgeKind kind, bool animationsOn, long elapsedMs)
    {
        if (!Animates(kind, animationsOn) || elapsedMs < 0) return 0;
        int period = PeriodMs(kind), count = FrameCount(kind, true);
        return (int)(elapsedMs % period * count / period);
    }
}
