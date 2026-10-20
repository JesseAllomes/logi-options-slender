using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>One touch measured during calibration: pointer travel (pixels, larger axis) in the 400 ms before it, and during it.</summary>
public readonly record struct TouchSample(int Before, int During);

/// <summary>New grab thresholds and what they, and the current ones, would have done with the recorded touches.</summary>
public sealed record CalibrationResult(
    int GrabBeforePx, int GrabMovePx,
    int GrabsIgnored, int GrabsIgnoredBefore, int Grabs,
    int TouchesKept, int TouchesKeptBefore, int Touches);

/// <summary>
/// Touch calibration (user 2026-10-20): from recorded grabs and deliberate touches, the thresholds that keep every deliberate
/// touch with a margin and ignore as many grabs as that allows. Pure; the floating calibration window feeds it.
/// </summary>
public static class TouchCalibration
{
    /// <summary>Fewest deliberate touches that make a calibration worth saving.</summary>
    public const int MinTouches = 5;

    /// <summary>What the engine does with a touch: a grab when the pointer moved too far before it or during it.</summary>
    public static bool IsGrab(TouchSample s, int beforePx, int movePx) => s.Before > beforePx || s.During > movePx;

    /// <summary>Null when there are too few deliberate touches to judge.</summary>
    public static CalibrationResult? Compute(IReadOnlyList<TouchSample> grabs, IReadOnlyList<TouchSample> touches, int currentBefore, int currentMove)
    {
        if (touches.Count < MinTouches)
        {
            return null;
        }

        // Just above the most any deliberate touch moved, with a margin: 25 % or 20 px before, 50 % or 8 px during.
        int maxBefore = touches.Max(t => t.Before);
        int maxDuring = touches.Max(t => t.During);
        int before = Math.Clamp(Math.Max(maxBefore + 20, (int)Math.Ceiling(maxBefore * 1.25)), PointerSettings.GrabBeforeMin, PointerSettings.GrabBeforeMax);
        int move = Math.Clamp(Math.Max(maxDuring + 8, (int)Math.Ceiling(maxDuring * 1.5)), PointerSettings.GrabMoveMin, PointerSettings.GrabMoveMax);

        return new CalibrationResult(
            before, move,
            grabs.Count(g => IsGrab(g, before, move)), grabs.Count(g => IsGrab(g, currentBefore, currentMove)), grabs.Count,
            touches.Count(t => !IsGrab(t, before, move)), touches.Count(t => !IsGrab(t, currentBefore, currentMove)), touches.Count);
    }
}

/// <summary>
/// Turns pointer samples and touch edges into TouchSamples, the way the engine judges them: travel from the pointer's position
/// at touch-down over the 400 ms before, and during the touch up to the moment a rest would fire (a tap ends at the lift).
/// Feed it on one thread.
/// </summary>
public sealed class TouchSampler
{
    private readonly List<(long At, int X, int Y)> _points = [];
    private (long At, int X, int Y, int Before, int During)? _open;
    private readonly int _restMs;

    public TouchSampler(int restMs) => _restMs = restMs;

    public List<TouchSample> Samples { get; } = [];

    public void Pointer(long at, int x, int y)
    {
        _points.Add((at, x, y));
        _points.RemoveAll(p => p.At < at - 1000); // a second of history is plenty
        if (_open is { } o && at <= o.At + _restMs)
        {
            _open = o with { During = Math.Max(o.During, Math.Max(Math.Abs(x - o.X), Math.Abs(y - o.Y))) };
        }
    }

    public void TouchDown(long at)
    {
        if (_points.Count == 0)
        {
            return;
        }

        var (_, x, y) = _points[^1];
        int before = _points.Where(p => p.At >= at - SlenderEngine.GrabLookbackMs)
            .Select(p => Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y)))
            .DefaultIfEmpty(0)
            .Max();
        _open = (at, x, y, before, 0);
    }

    /// <summary>The thumb lifted: the touch is recorded. Returns it, or null when no touch was open.</summary>
    public TouchSample? Lift()
    {
        if (_open is not { } o)
        {
            return null;
        }

        _open = null;
        var sample = new TouchSample(o.Before, o.During);
        Samples.Add(sample);
        return sample;
    }
}
