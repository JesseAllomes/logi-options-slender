namespace Slender.Engine;

/// <summary>Where the engine runs work that must not run on the hook/HID thread.</summary>
internal interface IWorkQueue : IDisposable
{
    void Post(Action work);
}

/// <summary>
/// Single dedicated thread (optionally STA) with a bounded queue. When full the OLDEST item is dropped
/// (its onDropped runs). Work exceptions are swallowed and logged: nothing escapes the thread.
/// </summary>
internal sealed class BoundedWorker : IWorkQueue
{
    private readonly object _gate = new();
    private readonly LinkedList<(Action Work, Action? OnDropped)> _items = new();
    private readonly int _capacity;
    private readonly ILog _log;
    private readonly Thread _thread;
    private bool _disposed;
    private long _dropped;

    public BoundedWorker(string name, int capacity, bool sta, ILog? log = null)
    {
        _capacity = Math.Max(1, capacity);
        _log = log ?? NullLog.Instance;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        if (sta) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Post(Action work) => Post(work, null);

    public void Post(Action work, Action? onDropped)
    {
        Action? dropped = null;
        lock (_gate)
        {
            if (_disposed) dropped = onDropped;
            else
            {
                if (_items.Count >= _capacity)
                {
                    dropped = _items.First!.Value.OnDropped;
                    _items.RemoveFirst();
                    Interlocked.Increment(ref _dropped);
                }
                _items.AddLast((work, onDropped));
                Monitor.Pulse(_gate);
            }
        }
        SafeRun(dropped);
    }

    private void Run()
    {
        while (true)
        {
            Action work;
            lock (_gate)
            {
                while (_items.Count == 0)
                {
                    if (_disposed) return;
                    Monitor.Wait(_gate);
                }
                work = _items.First!.Value.Work;
                _items.RemoveFirst();
            }
            SafeRun(work);
        }
    }

    private void SafeRun(Action? a)
    {
        if (a is null) return;
        try { a(); }
        catch (Exception ex) { _log.Error("worker item failed", ex); }
    }

    public void Dispose()
    {
        List<Action?> pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _items.Select(i => i.OnDropped).ToList();
            _items.Clear();
            Monitor.PulseAll(_gate);
        }
        foreach (var p in pending) SafeRun(p);
        if (Thread.CurrentThread != _thread) _thread.Join(500);
    }
}
