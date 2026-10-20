namespace Slender.Engine;

/// <summary>
/// A burst of notifications must not buzz endlessly: one buzz per app per PerAppMs, and at most GlobalMax buzzes in any
/// GlobalWindowMs across all apps. Rejected notifications do not extend the windows. Any thread.
/// </summary>
public sealed class NotifyRateLimiter(int perAppMs = 5000, int globalMax = 4, int globalWindowMs = 30000, Func<long>? clock = null)
{
    private readonly Dictionary<string, long> _last = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<long> _all = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly object _gate = new();

    public bool TryAcquire(string app)
    {
        lock (_gate)
        {
            long now = _clock();
            while (_all.Count > 0 && now - _all.Peek() >= globalWindowMs) _all.Dequeue();
            if (_all.Count >= globalMax) return false;
            if (_last.TryGetValue(app, out long last) && now - last < perAppMs) return false;
            if (_last.Count > 256) _last.Clear(); // the per-app memory is a courtesy, never a leak
            _last[app] = now;
            _all.Enqueue(now);
            return true;
        }
    }
}
