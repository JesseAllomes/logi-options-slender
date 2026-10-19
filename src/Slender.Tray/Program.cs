using System.Diagnostics;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Ring;
using Slender.Tray.Ring;
using Slender.Hidpp;
using Slender.Hidpp.Transport;
using Slender.Sync;
using Velopack;

namespace Slender.Tray;

static class Program
{
    /// <summary>Measurement switch: no HID devices, inert hook. Never used in normal running.</summary>
    public const string NoDevicesArgument = "--no-devices";

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
    static int Main(string[] args)
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
        // Never keep the install folder as the working directory: every app the tray starts inherits it, and one still open
        // (OneNote from a ring slot) blocked updates, which replace that folder (user 2026-10-19).
        try { Directory.SetCurrentDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch (IOException) { }

        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            return 0;
        }

        bool restart = RunTray(args.Contains(NoDevicesArgument, StringComparer.OrdinalIgnoreCase));
        if (restart)
        {
            // Everything is disposed. Free the single-instance lock first, or the new copy would exit as a second instance.
            singleInstance.Release();
            try
            {
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                foreach (string a in args)
                {
                    info.ArgumentList.Add(a);
                }

                Process.Start(info)?.Dispose();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                new FileLog().Error("Restart after memory watchdog failed", e);
            }
        }

        return 0;
    }

    /// <summary>Builds and runs the whole tray; returns true when the memory watchdog asked for a restart.</summary>
    private static bool RunTray(bool noDevices)
    {
        var log = new FileLog();
        log.Info(noDevices ? "Slender starting (no devices)" : "Slender starting");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("Unhandled exception", e.ExceptionObject as Exception);

        string? configDir = SyncPaths.Override; // SLENDER_CONFIG_DIR: test hook shared with the settings app
        IConfigStore store = configDir is not null ? new JsonConfigStore(configDir) : new JsonConfigStore();
        StartupRegistration? startup = configDir is not null
            ? new StartupRegistration(new InMemoryRunKey(), Environment.ProcessPath ?? "Slender.Tray.exe") // a test instance must not touch the real Run key
            : null;

        var config = store.Load();
        IHidppTransportFactory mouseFactory = noDevices ? new NoDevices() : new WindowsHidTransportFactory();
        IHidppTransportFactory keyboardFactory = noDevices ? new NoDevices() : new WindowsHidTransportFactory(WindowsHidTransportFactory.ErgoK860ProductIds);
        using var mouse = new LogiMouse(mouseFactory,
            new LogiMouseOptions
            {
                ThumbWheelInvert = config.Pointer.InvertThumbWheel,
                SmartShift = new SmartShiftSetting(config.Pointer.SmartShiftEnabled, (byte)config.Pointer.SmartShiftThreshold),
                SmoothScroll = config.Pointer.SmoothScroll,
                Dpi = config.Pointer.Dpi,
                WheelResistance = config.Pointer.WheelResistance,
                SensePanelForce = config.Pointer.SensePanelForce,
                SensePanelBaseline = config.Pointer.SensePanelBaseline,
                HapticStrength = config.Haptics.Strength, // null = leave the mouse's own strength alone, like the other feel settings
            });
        using IInputHook hook = noDevices ? new InertHook() : new LowLevelMouseHook(log);
        using var foreground = new ForegroundAppWatcher(log);
        using var excel = new ExcelBridge(log);
        var executor = new ActionExecutor(excel, foreground, log);
        using var engine = new SlenderEngine(mouse, hook, foreground, excel, executor, config, log);

        // ERGO K860: per-app key bindings and Fn lock (m9). Diverts nothing until a key is bound.
        using var keyboard = new LogiKeyboard(keyboardFactory);
        using var keys = new KeyboardRouter(keyboard, foreground, executor, config, log);

        // Options+ and Slender must never both own HID++ diverts (docs/switch-over.md): the engines wait for it to go.
        var guard = new OptionsPlusGuard(IsOptionsPlusRunning, () =>
        {
            engine.Start();
            keys.Start();
        });

        var watchdog = new MemoryWatchdog(MemoryWatchdog.ReadCurrentPrivateBytes, log.Info);
        using var app = new TrayApplication(store, mouse, startup, foreground: foreground, log: log, keyboard: keyboard, guard: guard,
            watchdog: watchdog, statusPath: Path.Combine(SyncPaths.StateDirectory, "status.json"));
        // Settings sync (OneDrive app folder). Inert until the user has set up sign-in and signed in; never blocks the tray.
        SyncSettings syncSettings = SyncSettings.LoadDefault();
        using var sync = new BackgroundSync(SyncServiceFactory.Create(), log);
        _ = sync.StartAsync();
        using var updates = new UpdateCoordinator(
            new VelopackUpdateBackend(VelopackUpdateBackend.ResolveSource(syncSettings.UpdateSource)), new SettingsProcessCloser(), log,
            app.NotifyUpdate, app.UpdateStateChanged, applying: app.PrepareForUpdateRestart,
            applyFailed: app.RestoreAfterFailedRestart);
        app.AttachUpdates(updates);
        updates.Start();

        // Actions Ring overlay: window and drawing resources exist only while it is open (docs/actions-ring-optionsplus.md).
        using var ring = new RingController(() => engine.Config, foreground, hook, engine.RunRingAction, engine.PlayRingHaptic, app.PostToUi, log);
        EventHandler<RingTriggerEventArgs> onTrigger = (_, e) => app.PostToUi(() => ring.OnTrigger(e.Pressed, e.HoldOnly));
        engine.RingTrigger += onTrigger;
        // The ring needs the thumb wheel diverted while open; diverted rotation reaches it through the engine, not the mouse hook.
        ring.OpenChanged += engine.SetRingOpen;
        engine.RingThumbWheel += (_, delta) => ring.OnDivertedThumbWheel(delta);
        // Settings' thumb wheel card previews the real wheel and top button over a pipe; nothing runs while it is open.
        using var preview = new ThumbPreviewServer(engine, log);
        preview.Start();
        // Touch calibration recordings start only when a trigger file appears (see TouchRecorder); idle otherwise.
        using var touchRecorder = new TouchRecorder(engine, mouse, SyncPaths.StateDirectory, log);
        touchRecorder.Start();
        keys.RingTrigger += onTrigger;
        // Lock keys: a short label near the cursor.
        using var toast = new ThumbWheelToast(() => engine.Config, () => ring.IsOpen, log);
        app.LockKeyChanged += (_, text) => toast.Show(text);
        // Settings' "Identify" panel: the tray writes which spare key was pressed where Settings can read it.
        keys.SpareKeyIdentified += (_, cid) => SpareKeyReport.Write(SyncPaths.StateDirectory, cid, log);
        // Fn lock belongs to the keyboard (Fn+Esc): what it reports is saved, so an update or restart never flips it back.
        keys.FnLockChangedOnKeyboard += (_, on) => app.PostToUi(() => app.MirrorKeyboardFnLock(on));
        engine.StatusChanged += (_, status) =>
        {
            if (!status.Connected) app.PostToUi(() => ring.Close(RingCloseReason.DeviceLost));
        };

        app.ConfigChanged += (_, changed) =>
        {
            sync.NotifyConfigChanged();
            engine.ApplyConfig(changed);
            keys.ApplyConfig(changed);
            _ = mouse.SetThumbWheelInvertAsync(changed.Pointer.InvertThumbWheel, CancellationToken.None);
            _ = mouse.SetSmartShiftAsync(new SmartShiftSetting(changed.Pointer.SmartShiftEnabled, (byte)changed.Pointer.SmartShiftThreshold), CancellationToken.None);
            _ = mouse.SetSmoothScrollAsync(changed.Pointer.SmoothScroll, CancellationToken.None);
            _ = mouse.SetDpiAsync(changed.Pointer.Dpi, CancellationToken.None).ContinueWith(_ => app.RefreshDpi(), TaskScheduler.Default);
            _ = ApplyMouseFeel(mouse, changed).ContinueWith(_ => app.RefreshMouseValues(), TaskScheduler.Default);
        };
        app.Run();
        log.Info("Slender stopping");
        if (!app.RestartRequested)
        {
            updates.ApplyOnExitIfReady(); // an update nobody restarted for installs silently now
        }

        return app.RestartRequested;
    }

    /// <summary>Wheel resistance, Sense Panel force and haptic strength (m26): applied now and re-applied on every connect; null leaves the device alone.</summary>
    private static async Task ApplyMouseFeel(LogiMouse mouse, SlenderConfig config)
    {
        await mouse.SetWheelResistanceAsync(config.Pointer.WheelResistance, CancellationToken.None).ConfigureAwait(false);
        await mouse.SetSensePanelForceAsync(config.Pointer.SensePanelForce, CancellationToken.None).ConfigureAwait(false);
        await mouse.SetHapticStrengthAsync(config.Haptics.Strength, CancellationToken.None).ConfigureAwait(false);
    }
}
