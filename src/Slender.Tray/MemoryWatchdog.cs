using Slender.Tray.Native;

namespace Slender.Tray;

/// <summary>What the watchdog wants done after a check.</summary>
public enum WatchdogVerdict
{
    Ok,
    Restart,
}

/// <summary>
/// Pure decision logic for the tray's memory watchdog. Call <see cref="Check"/> once per minute (<see cref="Interval"/>)
/// with the process's private bytes. Logs once when usage crosses the warn line (and again after it has dropped back below
/// it); asks for a restart when usage has stayed above the restart line for <see cref="RestartAfterChecks"/> consecutive checks.
/// </summary>
public sealed class MemoryWatchdog
{
    public const long WarnBytes = 60L * 1024 * 1024;
    public const long RestartBytes = 100L * 1024 * 1024;
    public const int RestartAfterChecks = 3;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly Func<long?> _readPrivateBytes;
    private readonly Action<string> _log;
    private bool _warned;
    private int _overRestart;

    public MemoryWatchdog(Func<long?> readPrivateBytes, Action<string> log)
    {
        _readPrivateBytes = readPrivateBytes ?? throw new ArgumentNullException(nameof(readPrivateBytes));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public long? LastBytes { get; private set; }

    public WatchdogVerdict Check()
    {
        long? bytes = _readPrivateBytes();
        LastBytes = bytes;
        if (bytes is not { } b)
        {
            return WatchdogVerdict.Ok; // unreadable: neither warn nor count
        }

        if (b >= WarnBytes)
        {
            if (!_warned)
            {
                _warned = true;
                _log($"Memory watchdog: private bytes {Mb(b)} MB crossed {Mb(WarnBytes)} MB");
            }
        }
        else
        {
            _warned = false;
        }

        if (b > RestartBytes)
        {
            _overRestart++;
            if (_overRestart >= RestartAfterChecks)
            {
                _log($"Memory watchdog: private bytes {Mb(b)} MB above {Mb(RestartBytes)} MB for {_overRestart} checks; restarting the tray");
                return WatchdogVerdict.Restart;
            }
        }
        else
        {
            _overRestart = 0;
        }

        return WatchdogVerdict.Ok;
    }

    /// <summary>Private bytes of this process (GetProcessMemoryInfo PrivateUsage), or null when the call fails.</summary>
    public static unsafe long? ReadCurrentPrivateBytes()
    {
        Win32.PROCESS_MEMORY_COUNTERS_EX c = default;
        c.cb = (uint)sizeof(Win32.PROCESS_MEMORY_COUNTERS_EX);
        return Win32.GetProcessMemoryInfo(Win32.GetCurrentProcess(), &c, c.cb) != 0 ? (long)c.PrivateUsage : null;
    }

    private static long Mb(long bytes) => bytes / (1024 * 1024);
}
