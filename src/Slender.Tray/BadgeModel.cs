using Slender.Hidpp;

namespace Slender.Tray;

/// <summary>What the tray icon pulses for: nothing (plain icon), a low battery (red) or charging (green).</summary>
internal enum BadgeKind { None, Low, Charging }

/// <summary>One device as the badge sees it: connection, last battery reading and the low-battery line for that device.</summary>
internal readonly record struct BadgeInput(bool Connected, BatteryStatus? Battery, int ThresholdPercent);

/// <summary>Pure decision of which pulse to show. Low beats Charging. Full on the charger, an Error reading and no reading show nothing.</summary>
internal static class BadgeModel
{
    public static BadgeKind Decide(BadgeInput mouse, BadgeInput keyboard, bool alertsEnabled)
    {
        BadgeKind a = One(mouse, alertsEnabled), b = One(keyboard, alertsEnabled);
        return Rank(a) >= Rank(b) ? a : b;
    }

    private static BadgeKind One(BadgeInput d, bool alertsEnabled)
    {
        if (!d.Connected || d.Battery is not { } b || b.State == ChargeState.Error) return BadgeKind.None;
        if (b.Charging) return b.State == ChargeState.Full ? BadgeKind.None : BadgeKind.Charging;
        return alertsEnabled && b.Percent <= d.ThresholdPercent ? BadgeKind.Low : BadgeKind.None;
    }

    private static int Rank(BadgeKind k) => k switch { BadgeKind.Low => 2, BadgeKind.Charging => 1, _ => 0 };
}

/// <summary>Pure frame timing for the pulse (one sine period of 1.6 s in 20 frames, 80 ms apart).</summary>
internal static class BadgeTiming
{
    public const int TimerIntervalMs = 80;
    public const int PulsePeriodMs = 1600;

    public static int PeriodMs(BadgeKind kind) => kind == BadgeKind.None ? 0 : PulsePeriodMs;

    /// <summary>A timer is needed only while a pulse runs: Low or Charging with Windows animations on.</summary>
    public static bool NeedsTimer(BadgeKind kind, bool animationsOn) => animationsOn && kind != BadgeKind.None;

    /// <summary>Frames to render: 0 for the plain icon, 1 for a static tint, one loop of frames for a pulse.</summary>
    public static int FrameCount(BadgeKind kind, bool animationsOn) =>
        kind == BadgeKind.None ? 0 : animationsOn ? PulsePeriodMs / TimerIntervalMs : 1;

    public static int FrameIndex(BadgeKind kind, bool animationsOn, long elapsedMs)
    {
        if (!NeedsTimer(kind, animationsOn) || elapsedMs < 0) return 0;
        int count = FrameCount(kind, true);
        return (int)(elapsedMs % PulsePeriodMs * count / PulsePeriodMs);
    }
}
