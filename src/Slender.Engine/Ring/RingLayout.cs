namespace Slender.Engine.Ring;

/// <summary>
/// Sizes for one ring style at one size, in device-independent pixels (96 DPI) relative to the ring centre.
/// Extent is the half-width of the square the ring needs, labels included; the overlay window is 2 x Extent across.
/// </summary>
public sealed record RingMetrics(
    double Scale,
    double DeadZone,
    double InnerRadius,
    double OuterRadius,
    double SlotRadius,
    double BubbleRadius,
    double KeyWidth,
    double KeyHeight,
    double KeyGap,
    double LabelRadius,
    double Extent);

/// <summary>
/// Geometry shared by the tray overlay and the Settings preview: slot positions per style and size, which slot a
/// pointer offset selects, and where the ring sits on a monitor. Slots run clockwise from the top (index 0 = up).
/// Values come from the round-1 prototypes (Medium); Small is 0.8x and Large 1.2x.
/// </summary>
public static class RingLayout
{
    public const int SlotCount = Profile.RingSlotCount;

    public static double ScaleOf(RingSize size) => size switch
    {
        RingSize.Small => 0.8,
        RingSize.Large => 1.2,
        _ => 1.0,
    };

    public static RingMetrics Metrics(RingStyle style, RingSize size)
    {
        double k = ScaleOf(size);
        return style switch
        {
            RingStyle.Compass => new RingMetrics(k, DeadZone: 28 * k, InnerRadius: 0, OuterRadius: 125 * k, SlotRadius: 100 * k,
                BubbleRadius: 25 * k, KeyWidth: 0, KeyHeight: 0, KeyGap: 0, LabelRadius: 146 * k, Extent: 220 * k),
            RingStyle.Keypad => new RingMetrics(k, DeadZone: 36 * k, InnerRadius: 0, OuterRadius: 0, SlotRadius: 0,
                BubbleRadius: 0, KeyWidth: 104 * k, KeyHeight: 72 * k, KeyGap: 8 * k, LabelRadius: 0, Extent: 180 * k),
            _ => new RingMetrics(k, DeadZone: 60 * k, InnerRadius: 60 * k, OuterRadius: 128 * k, SlotRadius: 94 * k,
                BubbleRadius: 0, KeyWidth: 0, KeyHeight: 0, KeyGap: 0, LabelRadius: 52 * k, Extent: 150 * k),
        };
    }

    /// <summary>Centre of slot <paramref name="index"/> relative to the ring centre (screen axes: y grows downwards).</summary>
    public static (double X, double Y) SlotCenter(RingStyle style, RingSize size, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SlotCount);
        RingMetrics m = Metrics(style, size);
        if (style == RingStyle.Keypad)
        {
            (int col, int row) = KeypadCell(index);
            return ((col - 1) * (m.KeyWidth + m.KeyGap), (row - 1) * (m.KeyHeight + m.KeyGap));
        }

        double a = AngleOf(index) * Math.PI / 180;
        return (m.SlotRadius * Math.Cos(a), m.SlotRadius * Math.Sin(a));
    }

    /// <summary>Keypad grid cell (column, row; 0..2) for a slot: up, up-right, right, down-right, down, down-left, left, up-left.</summary>
    public static (int Col, int Row) KeypadCell(int index) => index switch
    {
        0 => (1, 0),
        1 => (2, 0),
        2 => (2, 1),
        3 => (2, 2),
        4 => (1, 2),
        5 => (0, 2),
        6 => (0, 1),
        7 => (0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Slot direction in degrees, screen axes: 0 = up is -90.</summary>
    public static double AngleOf(int index) => -90 + index * 45;

    /// <summary>
    /// The slot a pointer offset from the ring centre points at, or null inside the dead zone (centre = cancel, or
    /// back inside a folder). Each slot owns a 45-degree sector centred on its direction, for every style.
    /// </summary>
    public static int? SlotAt(double dx, double dy, RingMetrics metrics)
    {
        if (Math.Sqrt(dx * dx + dy * dy) < metrics.DeadZone)
        {
            return null;
        }

        double degrees = Math.Atan2(dy, dx) * 180 / Math.PI + 90; // 0 = up, clockwise
        int slot = (int)Math.Round(degrees / 45, MidpointRounding.AwayFromZero);
        return ((slot % SlotCount) + SlotCount) % SlotCount;
    }

    /// <summary>
    /// Ring centre in physical pixels: at the cursor or the monitor centre, then moved so the whole ring
    /// (Extent x dpiScale) stays inside the monitor's work area.
    /// </summary>
    public static (int X, int Y) Place(RingPosition position, int cursorX, int cursorY,
        int workLeft, int workTop, int workRight, int workBottom, RingMetrics metrics, double dpiScale)
    {
        int extent = (int)Math.Ceiling(metrics.Extent * dpiScale);
        int x = position == RingPosition.MonitorCentre ? (workLeft + workRight) / 2 : cursorX;
        int y = position == RingPosition.MonitorCentre ? (workTop + workBottom) / 2 : cursorY;
        x = ClampCentre(x, workLeft, workRight, extent);
        y = ClampCentre(y, workTop, workBottom, extent);
        return (x, y);
    }

    private static int ClampCentre(int v, int lo, int hi, int extent) =>
        hi - lo < 2 * extent ? (lo + hi) / 2 : Math.Clamp(v, lo + extent, hi - extent);
}
