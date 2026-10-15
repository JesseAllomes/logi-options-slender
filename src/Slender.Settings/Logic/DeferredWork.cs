namespace Slender.Settings.Logic;

/// <summary>
/// Work that must not delay the first frame. Items run one at a time, each posted separately through <c>post</c> (a low priority
/// dispatcher enqueue in the app), so input and painting can interleave. A failing item is recorded and does not stop the rest.
/// </summary>
public sealed class DeferredWork
{
    private readonly Action<Action> _post;
    private readonly Queue<(string Name, Action Work)> _queue = new();
    private bool _started;
    private bool _cancelled;

    public DeferredWork(Action<Action> post) => _post = post;

    /// <summary>Names of the items that have run, in order.</summary>
    public List<string> Completed { get; } = [];

    /// <summary>Names of the items that threw.</summary>
    public List<string> Failed { get; } = [];

    public int Pending => _queue.Count;

    public void Add(string name, Action work)
    {
        _queue.Enqueue((name, work));
        if (_started && _queue.Count == 1)
        {
            _post(RunNext);
        }
    }

    /// <summary>Call once the first frame is up. Does nothing if already started.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (_queue.Count > 0)
        {
            _post(RunNext);
        }
    }

    public void Cancel()
    {
        _cancelled = true;
        _queue.Clear();
    }

    private void RunNext()
    {
        if (_cancelled || _queue.Count == 0)
        {
            return;
        }

        (string name, Action work) = _queue.Dequeue();
        try
        {
            work();
            Completed.Add(name);
        }
        catch (Exception)
        {
            Failed.Add(name);
        }

        if (_queue.Count > 0)
        {
            _post(RunNext);
        }
    }
}
