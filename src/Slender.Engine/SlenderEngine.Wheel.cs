namespace Slender.Engine;

/// <summary>
/// Main wheel speed per app and thumb wheel flicks (user 2026-10-20, research build m43). The main wheel is never diverted:
/// the hook sees each native turn and, only when the app's speed is not 100 %, swallows it and sends a scaled one, the same
/// way the side scroll speed works. A flick is a fast spin of the thumb wheel that runs its own action once.
/// </summary>
public sealed partial class SlenderEngine
{
    private long _wheelRemainder;                    // engine queue: main wheel speed carry, in delta*percent units
    private readonly FlickDetector _flick = new();

    /// <summary>Hook thread, O(1): a native main wheel turn. Swallowed and resent scaled when the app has its own speed.</summary>
    private void OnVerticalWheel(object? sender, HookWheelEventArgs e)
    {
        try
        {
            if (_ringOpen) return; // the ring takes the wheel through its own capture
            int speed = ThumbWheelModes.WheelSpeedFor(_config, _foreground.ProcessName);
            if (speed == 100) return;
            e.Handled = true;
            int delta = e.Delta;
            _queue.Post(() => SendScaledWheel(delta, speed));
        }
        catch (Exception ex) { SafeLog("main wheel handler failed", ex); }
    }

    /// <summary>Engine queue: the scaled turn, carrying fractions so slow speeds still move (injected input skips the hook).</summary>
    private void SendScaledWheel(int delta, int speed)
    {
        if (_wheelRemainder != 0 && Math.Sign(_wheelRemainder) != Math.Sign(delta)) _wheelRemainder = 0;
        _wheelRemainder += (long)delta * Math.Clamp(speed, PointerSettings.SpeedMin, PointerSettings.SpeedMax);
        var send = (int)(_wheelRemainder / 100);
        _wheelRemainder -= send * 100L;
        if (send == 0) return;
        try { _executor.SendVerticalWheel(send); }
        catch (Exception ex) { _log.Error("main wheel speed send failed", ex); }
    }

    /// <summary>
    /// Any thread (rotation handler): true when this turn is part of a flick and must not scroll. The first turn that completes
    /// a flick runs the app's flick action for that direction on the engine queue.
    /// </summary>
    private bool TakeFlick(SlenderConfig cfg, int delta)
    {
        if (ThumbWheelModes.FlickFor(cfg, _foreground.ProcessName) is not { } flick) return false;
        switch (_flick.Add(delta, _clock()))
        {
            case FlickResult.Fired:
                var action = delta > 0 ? flick.Right : flick.Left;
                if (action is not ActionSpec.None) _queue.Post(() => RunGestureAction(action));
                return true;
            case FlickResult.Swallow:
                return true;
            default:
                return false;
        }
    }
}

public enum FlickResult { None, Fired, Swallow }

/// <summary>
/// Tells a flick (a fast spin) from turning. A flick is FlickUnits of travel in one direction within WindowMs. After it fires,
/// the rest of that spin is swallowed until the wheel has been still for QuietMs. Thread-safe.
/// </summary>
public sealed class FlickDetector
{
    public const int WindowMs = 120, QuietMs = 250, FlickUnits = 5 * 120;

    private readonly object _gate = new();
    private readonly Queue<(long At, int Delta)> _recent = new();
    private long _lastAt = long.MinValue / 2;
    private bool _swallowing;

    public FlickResult Add(int delta, long nowMs)
    {
        if (delta == 0) return FlickResult.None;
        lock (_gate)
        {
            bool quiet = nowMs - _lastAt >= QuietMs;
            _lastAt = nowMs;
            if (_swallowing)
            {
                if (!quiet) return FlickResult.Swallow;
                _swallowing = false;
            }

            if (_recent.Count > 0 && Math.Sign(_recent.Peek().Delta) != Math.Sign(delta)) _recent.Clear(); // a turn back starts over
            _recent.Enqueue((nowMs, delta));
            while (_recent.Count > 0 && nowMs - _recent.Peek().At > WindowMs) _recent.Dequeue();
            int travel = 0;
            foreach (var (_, d) in _recent) travel += Math.Abs(d);
            if (travel < FlickUnits) return FlickResult.None;
            _recent.Clear();
            _swallowing = true;
            return FlickResult.Fired;
        }
    }
}
