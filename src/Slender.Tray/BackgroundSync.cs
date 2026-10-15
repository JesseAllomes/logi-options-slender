using Slender.Engine;
using Slender.Sync;

namespace Slender.Tray;

/// <summary>
/// Runs sync off the UI thread: one pull at start, then a push a few seconds after the last config change (debounced).
/// Does nothing when sign-in is not set up or the user is signed out. Never blocks and never throws.
/// </summary>
public sealed class BackgroundSync : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(5);

    private readonly SyncService _service;
    private readonly ILog _log;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public BackgroundSync(SyncService service, ILog log, TimeSpan? debounce = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _debounce = debounce ?? DefaultDebounce;
    }

    /// <summary>Raised on a thread-pool thread after each pass that did something or failed.</summary>
    public event Action<SyncResult>? Completed;

    public bool Enabled => _service.IsConfigured;

    /// <summary>Pull (and push anything newer) at start.</summary>
    public Task StartAsync() => Enabled ? Task.Run(RunAsync) : Task.CompletedTask;

    /// <summary>Call on every config change. Restarts the debounce timer.</summary>
    public void NotifyConfigChanged()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => _ = Task.Run(RunAsync), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            SyncResult result = await _service.SyncNowAsync().ConfigureAwait(false);
            if (result.Outcome is SyncOutcome.NotSignedIn or SyncOutcome.NotConfigured)
            {
                return;
            }

            if (result.IsError)
            {
                _log.Error("Sync: " + result.Message);
            }
            else if (result.Outcome != SyncOutcome.UpToDate)
            {
                _log.Info("Sync: " + result.Message);
            }

            Completed?.Invoke(result);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error("Sync crashed", e);
        }
    }
}
