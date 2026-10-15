namespace Slender.Engine;

/// <summary>One-shot timer for the hold threshold. The callback may run on any thread; tests fire it by hand.</summary>
internal interface IHoldTimer
{
    IDisposable Schedule(int delayMs, Action fire);
}

internal sealed class ThreadingHoldTimer : IHoldTimer
{
    public IDisposable Schedule(int delayMs, Action fire) => new Timer(_ => fire(), null, delayMs, Timeout.Infinite);
}

/// <summary>
/// Press/hold bookkeeping for controls bound to <see cref="ActionSpec.TapHold"/>. The owner calls Press and TryRelease on its
/// work queue; the threshold callback posts back to that queue, so everything runs in order and never on a hook or HID thread.
/// The spec captured at press is the one released (an app switch while held cannot change it).
/// </summary>
internal sealed class TapHoldTracker(IHoldTimer timer, IWorkQueue queue, ILog log) : IDisposable
{
    private sealed class Entry(ActionSpec.TapHold spec)
    {
        public ActionSpec.TapHold Spec { get; } = spec;
        public bool Held;
        public IDisposable? Timer;
    }

    private readonly object _gate = new();
    private readonly Dictionary<ushort, Entry> _entries = new();
    private bool _disposed;

    /// <summary>The control went down. <paramref name="onHold"/> runs on the queue if it is still down after HoldMs.</summary>
    public void Press(ushort id, ActionSpec.TapHold spec, Action<ActionSpec.TapHold> onHold)
    {
        var entry = new Entry(spec);
        lock (_gate)
        {
            if (_disposed) return;
            if (_entries.Remove(id, out var old)) old.Timer?.Dispose();
            _entries[id] = entry;
        }
        entry.Timer = timer.Schedule(ActionSpec.TapHold.HoldMs, () =>
        {
            try { queue.Post(() => Fire(id, entry, onHold)); }
            catch (Exception ex) { try { log.Error("hold timer dispatch failed", ex); } catch { } }
        });
    }

    private void Fire(ushort id, Entry entry, Action<ActionSpec.TapHold> onHold)
    {
        lock (_gate)
        {
            if (_disposed || entry.Held || !_entries.TryGetValue(id, out var current) || current != entry) return;
            entry.Held = true;
        }
        try { onHold(entry.Spec); }
        catch (Exception ex) { log.Error("hold start failed", ex); }
    }

    /// <summary>The control came up: false if it was not tracked; otherwise the spec and whether the hold had already started.</summary>
    public bool TryRelease(ushort id, out ActionSpec.TapHold spec, out bool held)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_entries.Remove(id, out entry)) { spec = null!; held = false; return false; }
            held = entry.Held;
        }
        entry.Timer?.Dispose();
        spec = entry.Spec;
        return true;
    }

    public void Dispose()
    {
        Entry[] all;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            all = [.. _entries.Values];
            _entries.Clear();
        }
        foreach (var e in all) e.Timer?.Dispose();
    }
}
