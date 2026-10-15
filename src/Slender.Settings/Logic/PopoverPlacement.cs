namespace Slender.Settings.Logic;

public readonly record struct PxPoint(int X, int Y);

/// <summary>A rectangle in physical pixels.</summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Pure placement and animation maths for the tray popover. Everything is in physical pixels.</summary>
public static class PopoverPlacement
{
    /// <summary>
    /// Which monitor edge the taskbar occupies: the side where the work area is smaller than the monitor. With an
    /// auto-hidden taskbar (work area equals monitor) the edge nearest the cursor is used.
    /// </summary>
    public static TaskbarEdge DetectEdge(PxPoint cursor, PxRect monitor, PxRect work)
    {
        if (work.Bottom < monitor.Bottom)
        {
            return TaskbarEdge.Bottom;
        }

        if (work.Y > monitor.Y)
        {
            return TaskbarEdge.Top;
        }

        if (work.X > monitor.X)
        {
            return TaskbarEdge.Left;
        }

        if (work.Right < monitor.Right)
        {
            return TaskbarEdge.Right;
        }

        int toBottom = monitor.Bottom - cursor.Y;
        int toTop = cursor.Y - monitor.Y;
        int toLeft = cursor.X - monitor.X;
        int toRight = monitor.Right - cursor.X;
        int min = Math.Min(Math.Min(toBottom, toTop), Math.Min(toLeft, toRight));
        return min == toBottom ? TaskbarEdge.Bottom : min == toTop ? TaskbarEdge.Top : min == toLeft ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    /// <summary>
    /// Window rectangle for a popover of the given size opened by a click at the cursor: against the taskbar edge, centred on
    /// the cursor along the edge, kept inside the work area, with a gap from the taskbar. A popover taller or wider than the work
    /// area is shrunk to fit.
    /// </summary>
    public static PxRect Place(PxPoint cursor, PxRect monitor, PxRect work, int width, int height, int gap)
    {
        int w = Math.Min(width, Math.Max(1, work.Width - 2 * gap));
        int h = Math.Min(height, Math.Max(1, work.Height - 2 * gap));
        int x;
        int y;
        switch (DetectEdge(cursor, monitor, work))
        {
            case TaskbarEdge.Top:
                x = cursor.X - w / 2;
                y = work.Y + gap;
                break;
            case TaskbarEdge.Left:
                x = work.X + gap;
                y = cursor.Y - h / 2;
                break;
            case TaskbarEdge.Right:
                x = work.Right - w - gap;
                y = cursor.Y - h / 2;
                break;
            default:
                x = cursor.X - w / 2;
                y = work.Bottom - h - gap;
                break;
        }

        x = Math.Clamp(x, work.X + gap, Math.Max(work.X + gap, work.Right - w - gap));
        y = Math.Clamp(y, work.Y + gap, Math.Max(work.Y + gap, work.Bottom - h - gap));
        return new PxRect(x, y, w, h);
    }

    /// <summary>
    /// Where the popover grows to when it unfolds into the window: the full size, shrunk to the work area, growing away from the
    /// corner of the work area the popover sits in (a bottom-right popover grows up and to the left).
    /// </summary>
    public static PxRect Unfold(PxRect popover, int width, int height, PxRect work, int margin = 12)
    {
        int w = Math.Min(width, Math.Max(popover.Width, work.Width - 2 * margin));
        int h = Math.Min(height, Math.Max(popover.Height, work.Height - 2 * margin));
        bool right = popover.X + popover.Width / 2 >= work.X + work.Width / 2;
        bool bottom = popover.Y + popover.Height / 2 >= work.Y + work.Height / 2;
        int x = right ? popover.Right - w : popover.X;
        int y = bottom ? popover.Bottom - h : popover.Y;
        x = Math.Clamp(x, work.X + margin, Math.Max(work.X + margin, work.Right - w - margin));
        y = Math.Clamp(y, work.Y + margin, Math.Max(work.Y + margin, work.Bottom - h - margin));
        return new PxRect(x, y, w, h);
    }

    /// <summary>Ease-out cubic, t clamped to 0..1.</summary>
    public static double EaseOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double u = 1 - t;
        return 1 - u * u * u;
    }

    public static PxRect Lerp(PxRect from, PxRect to, double t) => new(
        (int)Math.Round(from.X + (to.X - from.X) * t),
        (int)Math.Round(from.Y + (to.Y - from.Y) * t),
        (int)Math.Round(from.Width + (to.Width - from.Width) * t),
        (int)Math.Round(from.Height + (to.Height - from.Height) * t));

    /// <summary>
    /// A fresh popover launched within the grace period after the previous one was dismissed by losing focus is the same click
    /// that dismissed it (the tray icon was clicked while the popover was open): do not reopen.
    /// </summary>
    public static bool ShouldSuppressReopen(long lastDismissTick, long nowTick, int graceMs = 400) =>
        lastDismissTick > 0 && nowTick >= lastDismissTick && nowTick - lastDismissTick < graceMs;
}
