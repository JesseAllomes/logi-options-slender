using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>
/// Plays a <see cref="HapticCue"/> on the mouse: Pulses times, SpacingMs apart. A new cue cancels whatever is left of the
/// previous one (cues never overlap). Everything runs on the engine's work queue and timers, never on a hook or HID thread
/// (<see cref="Play"/> only queues). A cue with an exact Strength writes the mouse's stored strength before its first pulse and
/// puts the global strength back after its last; a burst of cues with the same strength therefore writes only twice, because
/// the restore waits <see cref="RestoreDelayMs"/> for a follow-up cue.
/// </summary>
internal sealed class HapticPlayer : IDisposable
{
    /// <summary>How long after the last pulse the global strength is put back (the motor plays the waveform on its own).</summary>
    public const int RestoreDelayMs = 400;

    private readonly ILogiMouse _mouse;
    private readonly IHoldTimer _timer;
    private readonly IWorkQueue _queue;
    private readonly Func<HapticSettings> _settings;
    private readonly ILog _log;
    private readonly CancellationToken _ct;
    private Task _io = Task.CompletedTask; // queue thread only: mouse calls run in order
    private IDisposable? _next;            // queue thread only: the next pulse or the restore
    private int _seq;                      // queue thread only: bumped by every new cue, so stale timers do nothing
    private int? _applied;                 // queue thread only: exact strength currently written to the mouse; null = the global one is in place
    private volatile bool _disposed;

    public HapticPlayer(ILogiMouse mouse, IHoldTimer timer, IWorkQueue queue, Func<HapticSettings> settings, ILog log, CancellationToken ct)
    {
        _mouse = mouse;
        _timer = timer;
        _queue = queue;
        _settings = settings;
        _log = log;
        _ct = ct;
    }

    /// <summary>Plays the cue (any thread, returns at once). The caller has already decided the cue is wanted.</summary>
    public void Play(HapticCue cue)
    {
        try { _queue.Post(() => Start(cue)); }
        catch (Exception ex) { SafeLog("haptic could not be queued", ex); }
    }

    private void Start(HapticCue cue)
    {
        if (_disposed) return;
        try
        {
            cue = cue.Clamped();
            CancelPending();
            var seq = _seq;
            if (cue.Strength is { } s)
            {
                if (_applied != s)
                {
                    _applied = s;
                    Io(() => _mouse.SetHapticStrengthAsync(s, _ct), "haptic strength");
                }
            }
            else if (_applied is not null) RestoreNow();
            Pulse(seq, cue, cue.Pulses);
        }
        catch (Exception ex) { _log.Error("haptic failed", ex); }
    }

    private void Pulse(int seq, HapticCue cue, int left)
    {
        if (_disposed || seq != _seq) return;
        Io(() => _mouse.PlayHapticAsync((byte)cue.Pattern, _ct), "haptic");
        if (left > 1) _next = _timer.Schedule(cue.SpacingMs, () => PostIfCurrent(seq, () => Pulse(seq, cue, left - 1)));
        else if (_applied is not null) _next = _timer.Schedule(RestoreDelayMs, () => PostIfCurrent(seq, RestoreNow));
    }

    private void PostIfCurrent(int seq, Action work)
    {
        try { _queue.Post(() =>
            {
                if (_disposed || seq != _seq) return;
                _next?.Dispose(); // the timer that just fired
                _next = null;
                work();
            }); }
        catch (Exception ex) { SafeLog("haptic timer could not be queued", ex); }
    }

    private void RestoreNow()
    {
        _applied = null;
        int global = _settings().EffectiveStrength;
        Io(() => _mouse.SetHapticStrengthAsync(global, _ct), "haptic strength restore");
    }

    private void CancelPending()
    {
        _seq++;
        _next?.Dispose();
        _next = null;
    }

    private void Io(Func<Task> op, string what)
    {
        var previous = _io;
        _io = RunAfter(previous, op, what);
    }

    private async Task RunAfter(Task previous, Func<Task> op, string what)
    {
        try { await previous.ConfigureAwait(false); } catch { }
        try { await op().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error(what + " failed", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _next?.Dispose();
        // Never leave an exact strength stored on the mouse.
        if (_applied is not null)
        {
            try { _mouse.SetHapticStrengthAsync(_settings().EffectiveStrength, CancellationToken.None).Wait(500); }
            catch { }
        }
    }

    private void SafeLog(string msg, Exception ex)
    {
        try { _log.Error(msg, ex); } catch { }
    }
}
