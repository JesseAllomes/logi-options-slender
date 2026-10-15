using Microsoft.UI.Xaml;

namespace Slender.Settings;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Services.CrashLog.Write(e.Exception);
            e.Handled = true;
        };
    }

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Program.PopoverMode);
        _window.Activate();
        _window.AfterActivate();
    }

    /// <summary>"Settings…" from the tray, or a plain launch while this process runs: show the window (a popover unfolds into it).</summary>
    public void OnShowSignal() => _window?.ShowFromSignal();

    /// <summary>The tray icon clicked again: an open popover closes and the process exits; a full window just comes to the front.</summary>
    public void OnToggleSignal() => _window?.ToggleFromSignal();
}
