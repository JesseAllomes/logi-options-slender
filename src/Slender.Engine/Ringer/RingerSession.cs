namespace Slender.Engine.Ringer;

/// <summary>
/// One ringing call on the mouse. A start signal plays the ringer cue, then again every cue length plus its pause, until a stop
/// signal, any mouse button press or <see cref="MaxRingMs"/> (a ring that never got its stop must not buzz forever). Nothing exists while
/// idle: the click watch (the mouse hook's button notice) and both timers are on only between start and end. A second start while
/// ringing changes nothing. All work runs on the engine's work queue.
/// </summary>
internal sealed class RingerSession : IDisposable
{
    /// <summary>The longest one ring buzzes.</summary>
    public const int MaxRingMs = 90_000;

    private readonly IHoldTimer _timer;
    private readonly IWorkQueue _queue;
    private readonly Func<HapticCue?> _cue;
    private readonly Action<HapticCue> _play;
    private readonly Action _cancelPlay;
    private readonly Action<bool> _watchClicks;
    private readonly ILog _log;
    private IDisposable? _next, _cap; // queue thread only
    private int _round;               // queue thread only: bumped at every start and end, so stale timers do nothing
    private volatile bool _ringing;
    private volatile bool _disposed;

    /// <param name="cue">The ringer cue to play now (expanded and clamped), or null when haptics or the ringer are off.</param>
    /// <param name="play">Plays a cue on the mouse.</param>
    /// <param name="cancelPlay">Stops the cue still playing.</param>
    /// <param name="watchClicks">Turns the mouse hook's any-button notice on or off.</param>
    public RingerSession(IHoldTimer timer, IWorkQueue queue, Func<HapticCue?> cue, Action<HapticCue> play, Action cancelPlay, Action<bool> watchClicks, ILog log)
    {
        _timer = timer;
        _queue = queue;
        _cue = cue;
        _play = play;
        _cancelPlay = cancelPlay;
        _watchClicks = watchClicks;
        _log = log;
    }

    /// <summary>True from the first start until the ring ends. Read by the engine to drop ordinary buzzes. Any thread.</summary>
    public bool IsRinging => _ringing;

    /// <summary>A ring signal arrived (any thread, returns at once).</summary>
    public void Signal(bool start) => Post(() => { if (start) Begin(); else End(); });

    /// <summary>A mouse button went down (the hook thread; only called while the click watch is on).</summary>
    public void OnClick() => Post(End);

    private void Post(Action work)
    {
        if (_disposed) return;
        try { _queue.Post(() => { if (!_disposed) Safe(work); }); }
        catch (Exception ex) { try { _log.Error("ringer could not be queued", ex); } catch { } }
    }

    private void Safe(Action work)
    {
        try { work(); }
        catch (Exception ex) { try { _log.Error("ringer failed", ex); } catch { } }
    }

    private void Begin()
    {
        if (_ringing) return;
        if (_cue() is not { } cue) return;
        _ringing = true;
        int round = ++_round;
        _watchClicks(true);
        _cap = _timer.Schedule(MaxRingMs, () => PostIfCurrent(round, End));
        Ring(round, cue);
    }

    private void Ring(int round, HapticCue cue)
    {
        _play(cue);
        int period = Math.Max(cue.TotalMs + cue.RepeatGapMs, HapticCue.RepeatGapMin);
        _next = _timer.Schedule(period, () => PostIfCurrent(round, () =>
        {
            _next?.Dispose();
            _next = null;
            // The cue may have changed in Settings during the ring; the next round plays the new one (null: ringer switched off, stop).
            if (_cue() is { } now) Ring(round, now); else End();
        }));
    }

    private void PostIfCurrent(int round, Action work) => Post(() => { if (_ringing && round == _round) work(); });

    private void End()
    {
        if (!_ringing) return;
        _ringing = false;
        _round++;
        _next?.Dispose();
        _cap?.Dispose();
        _next = _cap = null;
        _watchClicks(false);
        _cancelPlay();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ringing = false;
        _next?.Dispose();
        _cap?.Dispose();
        try { _watchClicks(false); } catch { }
    }
}
