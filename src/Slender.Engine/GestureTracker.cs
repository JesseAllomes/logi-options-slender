namespace Slender.Engine;

/// <summary>
/// Accumulates mouse movement while the gesture button is held and turns it into a swipe direction on release.
/// Thread-safe: Begin/End run on the HID thread, Add on the hook thread. A gesture older than
/// <see cref="TimeoutMs"/> is abandoned (Add returns false), so a lost button-up can never keep the cursor frozen.
/// </summary>
internal sealed class GestureTracker
{
    /// <summary>Accumulated movement (px, dominant axis) needed for a swipe; less is a click.</summary>
    public const int SwipeThresholdPx = 50;

    public const long TimeoutMs = 10_000;

    private readonly object _gate = new();
    private bool _active;
    private long _start;
    private long _dx, _dy;

    public bool Active { get { lock (_gate) return _active; } }

    public void Begin(long now)
    {
        lock (_gate) { _active = true; _start = now; _dx = 0; _dy = 0; }
    }

    /// <summary>Adds movement. Returns true if the move belongs to a live gesture (swallow it); false if none is live or it timed out.</summary>
    public bool Add(int dx, int dy, long now)
    {
        lock (_gate)
        {
            if (!_active) return false;
            if (now - _start > TimeoutMs) { _active = false; return false; }
            _dx += dx;
            _dy += dy;
            return true;
        }
    }

    /// <summary>Ends the gesture. Live is false if no gesture was running or it timed out; otherwise Direction is the swipe, or null for a click.</summary>
    public (bool Live, GestureDirection? Direction) End(long now)
    {
        lock (_gate)
        {
            if (!_active) return (false, null);
            _active = false;
            if (now - _start > TimeoutMs) return (false, null);
            return (true, Resolve(_dx, _dy));
        }
    }

    public void Cancel()
    {
        lock (_gate) _active = false;
    }

    /// <summary>Dominant axis wins (ties go vertical); null below the threshold. Screen Y grows downwards.</summary>
    public static GestureDirection? Resolve(long dx, long dy)
    {
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);
        if (Math.Max(ax, ay) < SwipeThresholdPx) return null;
        if (ax > ay) return dx > 0 ? GestureDirection.Right : GestureDirection.Left;
        return dy > 0 ? GestureDirection.Down : GestureDirection.Up;
    }
}
