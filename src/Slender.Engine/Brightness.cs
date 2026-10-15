namespace Slender.Engine;

/// <summary>One monitor's brightness control. 0-100 percent. Disposed after each use.</summary>
public interface IBrightnessDevice : IDisposable
{
    bool TryGet(out int percent);
    bool TrySet(int percent);
}

/// <summary>Finds the monitor under the cursor and opens its brightness control. Abstracted so tests never touch a display.</summary>
public interface IMonitorProvider
{
    /// <summary>Opaque monitor handle under the cursor; 0 when unknown. Cheap: runs on the action thread.</summary>
    nint MonitorAtCursor();

    /// <summary>The control for that monitor, or null when it does not support brightness. May be slow (DDC/CI).</summary>
    IBrightnessDevice? Open(nint monitor);
}

public interface IBrightnessController
{
    /// <summary>Changes the brightness of the monitor under the cursor by <paramref name="stepPercent"/> points. Never throws, never blocks.</summary>
    void Adjust(int stepPercent);
}

/// <summary>
/// Brightness up/down. DDC/CI takes 50-100 ms per call, so presses are summed per monitor while a call is in flight and
/// applied as one read-modify-write afterwards (key repeat cannot build a queue). Unsupported monitors are logged once
/// and ignored.
/// </summary>
public sealed class BrightnessController : IBrightnessController
{
    private readonly IMonitorProvider _monitors;
    private readonly ILog _log;
    private readonly Action<Action> _run;
    private readonly object _gate = new();
    private readonly Dictionary<nint, int> _pending = new();
    private readonly HashSet<nint> _unsupported = new();
    private bool _running;

    public BrightnessController(IMonitorProvider monitors, ILog? log = null, Action<Action>? run = null)
    {
        _monitors = monitors;
        _log = log ?? NullLog.Instance;
        _run = run ?? (work => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null));
    }

    public void Adjust(int stepPercent)
    {
        try
        {
            if (stepPercent == 0) return;
            var monitor = _monitors.MonitorAtCursor();
            if (monitor == 0) return;
            lock (_gate)
            {
                _pending[monitor] = _pending.GetValueOrDefault(monitor) + stepPercent;
                if (_running) return;
                _running = true;
            }
            _run(Drain);
        }
        catch (Exception ex) { _log.Error("brightness request failed", ex); }
    }

    private void Drain()
    {
        while (true)
        {
            KeyValuePair<nint, int>[] batch;
            lock (_gate)
            {
                if (_pending.Count == 0) { _running = false; return; }
                batch = [.. _pending];
                _pending.Clear();
            }
            foreach (var (monitor, delta) in batch) Apply(monitor, delta);
        }
    }

    private void Apply(nint monitor, int delta)
    {
        try
        {
            if (_unsupported.Contains(monitor)) return;
            using var device = _monitors.Open(monitor);
            if (device is null || !device.TryGet(out var current))
            {
                _unsupported.Add(monitor);
                _log.Info($"brightness: monitor 0x{monitor:X} does not support brightness control; ignoring");
                return;
            }
            var target = Math.Clamp(current + delta, 0, 100);
            if (target != current && !device.TrySet(target))
                _log.Info($"brightness: monitor 0x{monitor:X} refused brightness {target}");
        }
        catch (Exception ex) { _log.Error("brightness change failed", ex); }
    }
}
