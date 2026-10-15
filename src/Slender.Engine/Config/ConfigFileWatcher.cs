using System.Text;

namespace Slender.Engine.Config;

/// <summary>
/// Watches config.json and reloads it through the store when another process changes it. Writes made by this process
/// (reported with NoteOwnWrite) and repeated events for unchanged content are ignored. Changed is raised on a thread-pool
/// thread; marshal as needed. No timer exists while nothing is changing.
/// </summary>
public sealed class ConfigFileWatcher : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);

    private readonly JsonConfigStore _store;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private Timer? _timer;
    private string? _lastText;
    private bool _disposed;

    public ConfigFileWatcher(JsonConfigStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lastText = ReadText();
        string dir = Path.GetDirectoryName(store.FilePath)!;
        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(store.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised with the freshly loaded config after an external change.</summary>
    public event EventHandler<SlenderConfig>? Changed;

    /// <summary>Call right after this process saved, so the resulting file event is not treated as an external change.</summary>
    public void NoteOwnWrite()
    {
        string? text = ReadText();
        lock (_gate)
        {
            _lastText = text;
        }
    }

    /// <summary>Checks the file now (the debounce target). Public so tests need no real file events.</summary>
    public void CheckNow()
    {
        string? text = ReadText();
        lock (_gate)
        {
            if (_disposed || text is null || text == _lastText)
            {
                return;
            }

            _lastText = text;
        }

        SlenderConfig config = _store.Load();
        Changed?.Invoke(this, config);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        _watcher?.Dispose();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // One-shot timer, restarted by each event in a burst; disposed after it fires.
            _timer?.Dispose();
            _timer = new Timer(_ =>
            {
                lock (_gate)
                {
                    _timer?.Dispose();
                    _timer = null;
                }

                try
                {
                    CheckNow();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Half-written or locked; the next event retries.
                }
            }, null, Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private string? ReadText()
    {
        try
        {
            return File.Exists(_store.FilePath) ? File.ReadAllText(_store.FilePath, Encoding.UTF8) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
