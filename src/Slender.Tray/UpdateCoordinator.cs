using Slender.Engine;

namespace Slender.Tray;

/// <summary>The Velopack calls the coordinator needs, behind an interface so its decisions can be tested without Velopack.</summary>
public interface IUpdateBackend
{
    /// <summary>False when no update source is set, or this is not an installed copy (dev build, portable run).</summary>
    bool IsAvailable { get; }

    string? CurrentVersion { get; }

    /// <summary>The newer version in the source, or null when up to date. Throws when the source cannot be read.</summary>
    Task<string?> CheckAsync(CancellationToken cancel);

    /// <summary>Downloads the version the last CheckAsync found.</summary>
    Task DownloadAsync(CancellationToken cancel);

    /// <summary>Installs the downloaded version when this process exits, without restarting.</summary>
    void ApplyOnExit();

    /// <summary>Installs the downloaded version now and starts Slender again. Does not return on success.</summary>
    void ApplyAndRestart();
}

/// <summary>Closes the settings app of this install (a separate process) so an update can replace its files.</summary>
public interface ISettingsCloser
{
    bool IsRunning();

    /// <summary>Asks the settings window to close and waits up to the timeout. True when it is gone.</summary>
    Task<bool> CloseAsync(TimeSpan timeout);
}

public enum UpdateNoticeKind
{
    Info,

    /// <summary>"Slender x is ready": clicking the balloon applies the update.</summary>
    Ready,
}

/// <summary>
/// Decides when to check for an update (30 s after start, then every 6 hours, or on demand), downloads it, and applies it
/// on request. One check or download runs at a time. Quiet by default: only the on-demand check reports "up to date" or a
/// failure; a source that cannot be read is logged once. Callbacks may run on any thread; the tray marshals them.
/// </summary>
public sealed class UpdateCoordinator : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan SettingsCloseTimeout = TimeSpan.FromSeconds(5);

    public const string UpToDateText = "Slender is up to date";
    public const string UnreachableText = "Couldn't reach the update source";
    public const string NotSetUpText = "Updates aren't set up on this copy of Slender";
    public const string CloseSettingsText = "Close Slender Settings first, then choose Restart to update again";
    public const string ApplyFailedText = "Couldn't restart to update. It installs when Slender next exits";
    public const string BusyText = "Already checking for updates";

    public static string ReadyText(string version) => $"Slender {version} is ready. Click to restart and update.";

    private readonly IUpdateBackend _backend;
    private readonly ISettingsCloser _settings;
    private readonly ILog _log;
    private readonly Action<UpdateNoticeKind, string> _notify;
    private readonly Action _changed;
    private readonly Action? _applying;
    private readonly Action? _applyFailed;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private IDisposable? _timer;
    private int _busy;
    private bool _failureLogged;
    private bool _disposed;
    private volatile string? _available;
    private volatile bool _ready;

    public UpdateCoordinator(
        IUpdateBackend backend,
        ISettingsCloser settings,
        ILog log,
        Action<UpdateNoticeKind, string> notify,
        Action changed,
        Action? applying = null,
        Action? applyFailed = null,
        Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? NullLog.Instance;
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _applying = applying;
        _applyFailed = applyFailed;
        _schedule = schedule ?? DefaultSchedule;
    }

    public string? CurrentVersion => _backend.CurrentVersion;

    /// <summary>A newer version that was found; it is downloading until UpdateReady.</summary>
    public string? AvailableVersion => _available;

    /// <summary>The new version is downloaded; applying it is one restart away.</summary>
    public bool UpdateReady => _ready;

    /// <summary>Arms the first check. Nothing is scheduled when there is no usable update source.</summary>
    public void Start()
    {
        if (_backend.IsAvailable)
        {
            ScheduleNext(FirstCheckDelay);
        }
    }

    /// <summary>The tray menu item or the settings button: reports "up to date" and failures.</summary>
    public Task CheckNowAsync() => CheckAsync(onDemand: true);

    private void ScheduleNext(TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer?.Dispose();
            _timer = _schedule(delay, OnTimer);
        }
    }

    private void OnTimer() => _ = RunScheduledAsync();

    private async Task RunScheduledAsync()
    {
        try
        {
            await CheckAsync(onDemand: false).ConfigureAwait(false);
        }
        finally
        {
            ScheduleNext(CheckInterval);
        }
    }

    private async Task CheckAsync(bool onDemand)
    {
        if (_disposed)
        {
            return;
        }

        if (!_backend.IsAvailable)
        {
            if (onDemand)
            {
                _notify(UpdateNoticeKind.Info, NotSetUpText);
            }

            return;
        }

        if (_ready)
        {
            if (onDemand)
            {
                _notify(UpdateNoticeKind.Ready, ReadyText(_available ?? string.Empty));
            }

            return;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            if (onDemand) _notify(UpdateNoticeKind.Info, BusyText); // a check or download is already running
            return;
        }

        try
        {
            string? version = await _backend.CheckAsync(_lifetime.Token).ConfigureAwait(false);
            _failureLogged = false;
            if (version is null)
            {
                if (onDemand)
                {
                    _notify(UpdateNoticeKind.Info, UpToDateText);
                }

                return;
            }

            _available = version;
            _changed();
            await _backend.DownloadAsync(_lifetime.Token).ConfigureAwait(false);
            _ready = true;
            _log.Info("Update " + version + " downloaded; it installs on restart or when Slender next exits.");
            _changed();
            _notify(UpdateNoticeKind.Ready, ReadyText(version));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (_available is not null && !_ready)
            {
                _available = null;
                _changed();
            }

            if (!_failureLogged)
            {
                _failureLogged = true;
                _log.Error("Update check failed", e);
            }

            if (onDemand)
            {
                _notify(UpdateNoticeKind.Info, UnreachableText);
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>
    /// Applies the downloaded update now: asks the settings app to close (never kills it), then restarts into the new version.
    /// Does nothing when no update is ready or another check or apply is running.
    /// </summary>
    public async Task ApplyAsync()
    {
        if (_disposed || !_ready || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (_settings.IsRunning() && !await _settings.CloseAsync(SettingsCloseTimeout).ConfigureAwait(false))
            {
                _notify(UpdateNoticeKind.Info, CloseSettingsText);
                return;
            }

            _applying?.Invoke();
            _backend.ApplyAndRestart();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error("Restart to update failed", e);
            try { _applyFailed?.Invoke(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _log.Error("Restoring the tray icon failed", ex); }
            _notify(UpdateNoticeKind.Info, ApplyFailedText);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Called as the tray exits: blocks until <see cref="ApplyOnExitIfReadyAsync"/> is done.</summary>
    public void ApplyOnExitIfReady() => ApplyOnExitIfReadyAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Called as the tray exits: an update nobody restarted for installs silently now.
    /// Asks Settings to close first so the install folder is free.
    /// </summary>
    public async Task ApplyOnExitIfReadyAsync()
    {
        if (!_ready)
        {
            return;
        }

        try
        {
            if (_settings.IsRunning() && !await _settings.CloseAsync(SettingsCloseTimeout).ConfigureAwait(false))
            {
                _log.Info("Settings still running; the update applies when it closes");
            }

            _backend.ApplyOnExit();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error("Could not schedule the update for exit", e);
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

        _lifetime.Cancel();
    }

    private static IDisposable DefaultSchedule(TimeSpan delay, Action action) =>
        new System.Threading.Timer(_ => action(), null, delay, Timeout.InfiniteTimeSpan);
}
