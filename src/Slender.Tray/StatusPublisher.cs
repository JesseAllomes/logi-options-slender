using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Tray;

/// <summary>What the tray knows right now; StatusPublisher turns it into a SlenderStatus.</summary>
public sealed record StatusSnapshot(
    bool MouseConnected,
    int? MouseBattery,
    bool MouseCharging,
    string? ForegroundApp,
    bool KeyboardConnected = false,
    int? KeyboardBattery = null,
    bool KeyboardCharging = false,
    bool PausedForOptionsPlus = false,
    int? MouseDpi = null,
    string? CurrentVersion = null,
    string? AvailableVersion = null,
    bool UpdateReady = false);

/// <summary>
/// Writes SlenderStatus to status.json (atomic replace) whenever Changed() is called, at most once per interval.
/// A burst of changes produces one write at the end of the interval. A one-shot timer exists only while a write is pending,
/// so an idle tray owns no timers. The Options+ process check is cached for 30 seconds.
/// </summary>
public sealed class StatusPublisher : IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan OptionsPlusCacheTime = TimeSpan.FromSeconds(30);
    public const string OptionsPlusProcess = "logioptionsplus_agent";

    private readonly Func<StatusSnapshot> _snapshot;
    private readonly Action<SlenderStatus> _write;
    private readonly Func<bool> _optionsPlusRunning;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly ILog _log;
    private readonly object _gate = new();
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;
    private IDisposable? _pending;
    private bool _disposed;
    private bool _optionsKnown;
    private bool _optionsValue;
    private DateTimeOffset _optionsChecked = DateTimeOffset.MinValue;

    public StatusPublisher(
        Func<StatusSnapshot> snapshot,
        string? path = null,
        ILog? log = null,
        Action<SlenderStatus>? write = null,
        Func<bool>? optionsPlusRunning = null,
        Func<DateTimeOffset>? now = null,
        Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        string target = path ?? SlenderPaths.StatusFile;
        _write = write ?? (s => StatusJson.WriteAtomic(target, s));
        _optionsPlusRunning = optionsPlusRunning ?? DefaultOptionsPlusRunning;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _schedule = schedule ?? DefaultSchedule;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Requests a publish. Safe from any thread.</summary>
    public void Changed()
    {
        lock (_gate)
        {
            if (_disposed || _pending is not null)
            {
                return; // a write is already scheduled and will read the newest snapshot
            }

            TimeSpan wait = _lastWrite + MinInterval - _now();
            if (wait <= TimeSpan.Zero)
            {
                WriteLocked();
                return;
            }

            _pending = _schedule(wait, OnTimer);
        }
    }

    /// <summary>Writes immediately, ignoring the interval (startup and shutdown).</summary>
    public void PublishNow()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending?.Dispose();
            _pending = null;
            WriteLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending?.Dispose();
            _pending = null;
        }
    }

    private void OnTimer()
    {
        lock (_gate)
        {
            _pending?.Dispose();
            _pending = null;
            if (!_disposed)
            {
                WriteLocked();
            }
        }
    }

    private void WriteLocked()
    {
        DateTimeOffset now = _now();
        _lastWrite = now;
        try
        {
            _write(Build(_snapshot(), now));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not write status.json", e);
        }
    }

    private SlenderStatus Build(StatusSnapshot s, DateTimeOffset now)
    {
        if (!_optionsKnown || now - _optionsChecked >= OptionsPlusCacheTime)
        {
            _optionsValue = _optionsPlusRunning();
            _optionsKnown = true;
            _optionsChecked = now;
        }

        return new SlenderStatus(
            [
                new DeviceState("mx-master-4", "MX Master 4", s.MouseConnected, s.MouseConnected ? s.MouseBattery : null, s.MouseConnected && s.MouseCharging) { Dpi = s.MouseConnected ? s.MouseDpi : null },
                new DeviceState("ergo-k860", "ERGO K860", s.KeyboardConnected, s.KeyboardConnected ? s.KeyboardBattery : null, s.KeyboardConnected && s.KeyboardCharging),
            ],
            _optionsValue || s.PausedForOptionsPlus,
            s.ForegroundApp,
            now,
            s.CurrentVersion,
            s.AvailableVersion,
            s.UpdateReady);
    }

    private static bool DefaultOptionsPlusRunning()
    {
        System.Diagnostics.Process[] found = System.Diagnostics.Process.GetProcessesByName(OptionsPlusProcess);
        try
        {
            return found.Length > 0;
        }
        finally
        {
            foreach (System.Diagnostics.Process p in found)
            {
                p.Dispose();
            }
        }
    }

    private static IDisposable DefaultSchedule(TimeSpan delay, Action action) =>
        new System.Threading.Timer(_ => action(), null, delay, Timeout.InfiniteTimeSpan);
}
