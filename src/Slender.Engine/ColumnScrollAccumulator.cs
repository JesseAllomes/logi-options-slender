namespace Slender.Engine;

/// <summary>
/// Converts wheel deltas (120 per notch; hi-res wheels send smaller deltas) to whole columns, keeping the
/// remainder. Resets on direction change and after an idle gap. Thread-safe.
/// </summary>
public sealed class ColumnScrollAccumulator
{
    public const int WheelDelta = 120;
    private readonly object _gate = new();
    private readonly long _idleMs;
    private long _remainder; // in (delta * columnsPerNotch) units; WheelDelta of them = one column
    private long _lastMs = long.MinValue;

    public ColumnScrollAccumulator(long idleResetMs = 500) => _idleMs = idleResetMs;

    /// <summary>Adds a delta and returns whole columns to scroll now (signed, positive = right).</summary>
    public int Add(int delta, int columnsPerNotch, long nowMs)
    {
        if (delta == 0) return 0;
        var cpn = Math.Clamp(columnsPerNotch, 1, 50);
        lock (_gate)
        {
            if (_lastMs != long.MinValue && nowMs - _lastMs > _idleMs) _remainder = 0;
            if (_remainder != 0 && Math.Sign(_remainder) != Math.Sign(delta)) _remainder = 0;
            _lastMs = nowMs;
            var scaled = _remainder + (long)delta * cpn;
            var cols = scaled / WheelDelta; // truncates toward zero
            _remainder = scaled - cols * WheelDelta;
            return (int)cols;
        }
    }

    /// <summary>
    /// Adds a delta at <paramref name="percent"/> of normal speed and returns whole steps (one step per notch at 100%, two at
    /// 200%, one per two notches at 50%). Use either this or <see cref="Add"/> on one instance, not both.
    /// </summary>
    public int AddScaled(int delta, int percent, long nowMs)
    {
        if (delta == 0) return 0;
        var pct = Math.Clamp(percent, 1, 1000);
        const long unit = WheelDelta * 100L;
        lock (_gate)
        {
            if (_lastMs != long.MinValue && nowMs - _lastMs > _idleMs) _remainder = 0;
            if (_remainder != 0 && Math.Sign(_remainder) != Math.Sign(delta)) _remainder = 0;
            _lastMs = nowMs;
            var scaled = _remainder + (long)delta * pct;
            var steps = scaled / unit;
            _remainder = scaled - steps * unit;
            return (int)steps;
        }
    }

    public void Reset()
    {
        lock (_gate) { _remainder = 0; _lastMs = long.MinValue; }
    }
}
