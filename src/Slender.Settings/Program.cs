using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Slender.Settings;

public static class Program
{
    public const string MutexName = @"Local\Slender.Settings";
    public const string ShowEventName = @"Local\Slender.Settings.Show";

    // Held for the life of the process; the OS releases them on exit.
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static RegisteredWaitHandle? _wait;

    [STAThread]
    public static void Main()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            // Another settings window exists: ask it to come to the front and leave.
            _showEvent.Set();
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            _wait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (_, _) => queue.TryEnqueue(() => App.Current.BringToFront()),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
            _ = new App();
        });
        _wait?.Unregister(null);
    }
}
