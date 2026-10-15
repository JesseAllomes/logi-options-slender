using System.Diagnostics;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;
using Slender.Hidpp.Transport;
using Slender.Sync;
using Velopack;

namespace Slender.Tray;

static class Program
{
    private static bool IsOptionsPlusRunning()
    {
        Process[] found = Process.GetProcessesByName(StatusPublisher.OptionsPlusProcess);
        try
        {
            return found.Length > 0;
        }
        finally
        {
            foreach (Process p in found)
            {
                p.Dispose();
            }
        }
    }

    [STAThread]
    static void Main()
    {
        // Velopack install/update/uninstall hooks. Must run first: during setup the exe is started with hook arguments and exits here.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                try
                {
                    StartupRegistration.ForCurrentProcess().Disable(); // the launch-at-login Run value would otherwise dangle
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                }
            })
            .Run();

        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        var log = new FileLog();
        log.Info("Slender starting");
        IConfigStore store = SyncPaths.Override is { } configDir ? new JsonConfigStore(configDir) : new JsonConfigStore(); // SLENDER_CONFIG_DIR: test hook shared with the settings app

        var config = store.Load();
        using var mouse = new LogiMouse(new WindowsHidTransportFactory(),
            new LogiMouseOptions
            {
                ThumbWheelInvert = config.Pointer.InvertThumbWheel,
                SmartShift = new SmartShiftSetting(config.Pointer.SmartShiftEnabled, (byte)config.Pointer.SmartShiftThreshold),
                SmoothScroll = config.Pointer.SmoothScroll,
            });
        using var hook = new LowLevelMouseHook(log);
        using var foreground = new ForegroundAppWatcher(log);
        using var excel = new ExcelBridge(log);
        var executor = new ActionExecutor(excel, foreground, log);
        using var engine = new SlenderEngine(mouse, hook, foreground, excel, executor, config, log);

        // ERGO K860: per-app key bindings and Fn lock (m9). Diverts nothing until a key is bound.
        using var keyboard = new LogiKeyboard(new WindowsHidTransportFactory(WindowsHidTransportFactory.ErgoK860ProductIds));
        using var keys = new KeyboardRouter(keyboard, foreground, executor, config, log);

        // Options+ and Slender must never both own HID++ diverts (docs/switch-over.md): the engines wait for it to go.
        var guard = new OptionsPlusGuard(IsOptionsPlusRunning, () =>
        {
            engine.Start();
            keys.Start();
        });

        using var context = new TrayApplicationContext(store, mouse, foreground: foreground, log: log, keyboard: keyboard, guard: guard);
        // Settings sync (OneDrive app folder). Inert until the user has set up sign-in and signed in; never blocks the tray.
        SyncSettings syncSettings = SyncSettings.LoadDefault();
        using var sync = new BackgroundSync(SyncServiceFactory.Create(), log);
        _ = sync.StartAsync();
        _ = SelfUpdate.CheckAsync(syncSettings.UpdateSource, log);

        context.ConfigChanged += (_, changed) =>
        {
            sync.NotifyConfigChanged();
            engine.ApplyConfig(changed);
            keys.ApplyConfig(changed);
            _ = mouse.SetThumbWheelInvertAsync(changed.Pointer.InvertThumbWheel, CancellationToken.None);
            _ = mouse.SetSmartShiftAsync(new SmartShiftSetting(changed.Pointer.SmartShiftEnabled, (byte)changed.Pointer.SmartShiftThreshold), CancellationToken.None);
            _ = mouse.SetSmoothScrollAsync(changed.Pointer.SmoothScroll, CancellationToken.None);
        };
        Application.Run(context);
        log.Info("Slender stopping");
    }
}
