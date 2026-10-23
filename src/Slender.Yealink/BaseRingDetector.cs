namespace Slender.Yealink;

/// <summary>
/// Spots the base ringing from its consumer-key reports: it sends a burst of a dozen presses of one code (01 02 to start, 01 01 to
/// stop) within about 0.3 s, which a person pressing the volume keys never does. Six presses of one code within 600 ms are one event.
/// No allocations, timers or threads: it only looks at the reports it is given.
/// </summary>
public sealed class BaseRingDetector
{
    public const int BurstPresses = 6;
    public const long BurstWindowMs = 600;
    private const byte ReportId = 1, StopCode = 1, StartCode = 2;

    private readonly Func<long> _clock;
    private long _until = long.MinValue;
    private byte _code;
    private long _first;
    private int _count;
    private bool _fired;

    /// <summary>True for a ring start, false for a ring stop. Raised on the caller's thread, once per burst.</summary>
    public event Action<bool>? Changed;

    /// <param name="clock">Milliseconds clock. Default: Environment.TickCount64.</param>
    public BaseRingDetector(Func<long>? clock = null) => _clock = clock ?? (static () => Environment.TickCount64);

    /// <summary>Ignores bursts for this long (Slender's own preview ring makes the base send the same burst). Any thread.</summary>
    public void SuppressFor(TimeSpan span) => Volatile.Write(ref _until, _clock() + (long)span.TotalMilliseconds);

    /// <summary>One consumer-key input report. Reads from one thread only.</summary>
    public void Feed(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2 || report[0] != ReportId || report[1] is not (StopCode or StartCode)) return; // releases and other keys
        long now = _clock();
        if (now < Volatile.Read(ref _until))
        {
            _count = 0;
            return;
        }
        byte code = report[1];
        if (_count == 0 || code != _code || now - _first > BurstWindowMs)
        {
            _code = code;
            _first = now;
            _count = 1;
            _fired = false;
        }
        else _count++;
        if (_count < BurstPresses || _fired) return;
        _fired = true;
        Changed?.Invoke(code == StartCode);
    }
}
