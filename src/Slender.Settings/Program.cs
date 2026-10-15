using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Settings;

public static class Program
{
    private static readonly string Suffix = Environment.GetEnvironmentVariable("SLENDER_CONFIG_DIR") is { Length: > 0 } ? ".Test" : string.Empty;
    public static readonly string MutexName = @"Local\Slender.Settings" + Suffix;
    public static readonly string ShowEventName = @"Local\Slender.Settings.Show" + Suffix;

    /// <summary>Signalled by a second <c>--popover</c> launch (the tray icon clicked again): an open popover closes.</summary>
    public static readonly string ToggleEventName = @"Local\Slender.Settings.Popover" + Suffix;

    /// <summary>True when started with <c>--popover</c> (tray icon left-click): the compact popover instead of the full window.</summary>
    public static bool PopoverMode { get; private set; }

    /// <summary>Stopwatch ticks at the top of Main, for the first-frame measurement.</summary>
    public static long StartTimestamp { get; private set; }

    // Held for the life of the process; the OS releases them on exit.
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static EventWaitHandle? _toggleEvent;
    private static RegisteredWaitHandle? _wait;
    private static RegisteredWaitHandle? _toggleWait;

    private static string DismissMarker =>
        Path.Combine(Environment.GetEnvironmentVariable("SLENDER_CONFIG_DIR") is { Length: > 0 } d ? d : SlenderPaths.LocalDirectory, "popover-dismissed.txt");

    /// <summary>Called when the popover closes because it lost focus, so a launch from the same click does not reopen it.</summary>
    public static void NoteFocusDismissal()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DismissMarker)!);
            File.WriteAllText(DismissMarker, Environment.TickCount64.ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only a nicety; the worst case is the popover reopening once.
        }
    }

    private static bool RecentFocusDismissal()
    {
        try
        {
            return File.Exists(DismissMarker)
                && long.TryParse(File.ReadAllText(DismissMarker), out long last)
                && PopoverPlacement.ShouldSuppressReopen(last, Environment.TickCount64);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        StartTimestamp = Stopwatch.GetTimestamp();
        PopoverMode = args.Any(a => string.Equals(a, "--popover", StringComparison.OrdinalIgnoreCase));
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
        if (!first)
        {
            // Another settings process exists: a second --popover closes an open popover (or fronts the window); anything else
            // asks it to come to the front (a popover unfolds into the window).
            (PopoverMode ? _toggleEvent : _showEvent).Set();
            return;
        }

        if (PopoverMode && RecentFocusDismissal())
        {
            return; // the click that closed the previous popover; stay closed
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            _wait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (_, _) => queue.TryEnqueue(() => App.Current.OnShowSignal()),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
            _toggleWait = ThreadPool.RegisterWaitForSingleObject(
                _toggleEvent,
                (_, _) => queue.TryEnqueue(() => App.Current.OnToggleSignal()),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
            _ = new App();
        });
        _wait?.Unregister(null);
        _toggleWait?.Unregister(null);
    }
}
