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
        _window = new MainWindow();
        _window.Activate();
    }

    public void BringToFront() => _window?.BringToFront();
}
