using System.Diagnostics;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Devices;
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
        // After a Slender update the add-in copy Excel opens may be older than the shipped one: refresh it when the add-in is on and
        // Excel is closed (a stat; hashes only when sizes match but times differ). Runs once on the pool and ends; retried next start.
        _ = Task.Run(() =>
        {
            try { log.Info("excel add-in copy: " + Slender.Engine.ExcelAddin.ExcelAddinInstaller.CreateDefault().RefreshInstalledCopy()); }
            catch (Exception ex) { log.Info("excel add-in copy refresh failed: " + ex.Message); }
        });
        using var excel = new ExcelBridge(log);
        // Slender Excel add-in (opt-in, m37): asked first, with a short time box; the bridge above stays the fallback.
        using var excelOrAddin = new Slender.Engine.ExcelAddin.AddinFirstExcelBridge(excel,
            new Slender.Engine.ExcelAddin.ExcelAddinClient(new Slender.Engine.ExcelAddin.NamedPipeExcelAddinTransport()), log);
        var executor = new ActionExecutor(excelOrAddin, foreground, log);
        using var engine = new SlenderEngine(mouse, hook, foreground, excelOrAddin, executor, config, log);
        // Macros run on their own threads (pauses never hold up the engine); Esc, a second press or the time cap ends one.
        using var macros = new Slender.Engine.Macros.MacroRunner(new Slender.Engine.Macros.WindowsMacroInput(log: log), log: log)
        {
            Lookup = id => engine.Config.Macros.FirstOrDefault(m => m.Id == id),
        };
        executor.Macros = macros;
        Slender.Engine.Macros.MacroLabels.Resolver = id => engine.Config.Macros.FirstOrDefault(m => m.Id == id)?.Name;

        // ERGO K860: per-app key bindings and Fn lock (m9). Diverts nothing until a key is bound.
        using var keyboard = new LogiKeyboard(keyboardFactory);
        mouse.LogSink = log.Info; // unhandled-notification and config-change lines (decode aid, rate limited in the device layer)
        keyboard.LogSink = log.Info;
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
        // Device registry (m52): Windows pushes HID arrival/removal, each device module runs only while its device is around, and
        // status.json says which devices are active, sleeping or disconnected. The Logi modules wrap the existing HID++ loops
        // unchanged; an arrival only cuts their reconnect wait short. Disposed before the app, the presence source after it.
        using IDevicePresenceSource? presence = noDevices ? null : new WindowsHidPresence();
        using var registry = presence is null ? null
            : new DeviceRegistry(DeviceCatalog.All, presence, new KnownDeviceStore(SyncPaths.StateDirectory, log), log);
        using var mouseModule = registry is null ? null : new LogiDeviceModule(DeviceCatalog.MxMaster4, mouse, registry.ReportLink);
        using var keyboardModule = registry is null ? null : new LogiDeviceModule(DeviceCatalog.ErgoK860, keyboard, registry.ReportLink);
        // Yealink WH64 headset (m54): in-process, opened only while the headset is present; with --no-devices there is no registry and none exists.
        var yealinkModule = registry is null ? null : new Slender.Yealink.YealinkModule(new Slender.Yealink.Windows.WindowsYealinkTransport(), log.Info)
        {
            LedWritesEnabled = config.Yealink.LedWrites,
            MuteSync = config.Yealink.MuteSync,
            VendorReadsEnabled = config.Yealink.VendorReads,
        };
        // Button presses run their bound action on the thread pool, never on the module's read thread.
        using var yealinkRouter = yealinkModule is null ? null
            : new Slender.Yealink.YealinkRouter(yealinkModule, executor, () => engine.Config.Yealink, a => ThreadPool.QueueUserWorkItem(_ => a()), log.Info);
        if (registry is not null)
        {
            registry.Register(mouseModule!);
            registry.Register(keyboardModule!);
            registry.Register(yealinkModule!);
            app.HeadsetMicMuted = yealinkModule!.GetMicMuted;
            app.ToggleHeadsetMic = yealinkModule.ToggleMicMute;
            // Battery, signal and the base's settings reach status.json and the tray menu; Settings' Headset page asks for a re-read.
            app.HeadsetInfo = yealinkModule.GetHeadset;
            app.RefreshHeadset = () => _ = Task.Run(yealinkModule.RefreshSettings);
            yealinkModule.HeadsetChanged += app.DevicesChanged;
            registry.Changed += (_, _) =>
            {
                log.Info("devices: " + string.Join(", ", registry.Devices.Select(d => $"{d.Id}={d.Presence}" + (d.Faulted ? " (faulted)" : ""))));
                app.DevicesChanged();
            };
            app.DeviceList = () => registry.Devices;
            app.ForgetDevice = registry.Forget;
            registry.Start();
        }

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
        // Other programs (a Claude Code hook, a build script) and Settings' Test button buzz the mouse over a current-user pipe.
        using var buzz = new BuzzServer(engine, log);
        buzz.Start();
        // 3CX ringer (docs/3cx-ringer.md): the Edge extension's native host sends ring start/stop over the buzz pipe. Remember the last
        // start for Settings, and after an update refresh the host copy Edge runs (a stat when set up; nothing at all when not).
        engine.RingSignalReceived += (_, e) => app.NoteRingSignal(e.At, e.Source == RingerSignalSource.Headset ? "headset" : "notification");
        if (StatusJson.TryRead(Path.Combine(SyncPaths.StateDirectory, "status.json")) is { LastRingSignal: { } lastRing, LastRingSource: { } lastRingSource })
            app.NoteRingSignal(lastRing, lastRingSource, publish: false);
        _ = Task.Run(() =>
        {
            try
            {
                var refreshed = Slender.Engine.Ringer.RingerInstaller.CreateDefault().RefreshHostCopy();
                if (refreshed != Slender.Engine.Ringer.RingerRefreshStatus.NotSetUp) log.Info("3cx ringer host copy: " + refreshed);
            }
            catch (Exception ex) { log.Info("3cx ringer host copy refresh failed: " + ex.Message); }
        });
        // Notification haptics (m45): the WinEvent hook exists only while the setting is on (see NotifyListener).
        using var notify = new NotifyListener(engine, new SeenAppsStore(SyncPaths.StateDirectory, log), log);
        void ApplyNotify(SlenderConfig c) => app.PostToUi(() => notify.SetEnabled(c.Haptics.Enabled && c.Haptics.Notify.Enabled));
        ApplyNotify(engine.Config);
        // Settings' on-keyboard remaps (K860 0x1C00): read-back and apply only on its request, never on connect, start or config reload.
        using var onboard = new OnboardServer(new OnboardRemapService(keyboard, log), log);
        onboard.Start();
        app.BatteryLow += (_, _) => engine.PlayHaptic(HapticEvent.BatteryLow);
        // Touch calibration recordings start only when a trigger file appears (see TouchRecorder); idle otherwise.
        using var touchRecorder = new TouchRecorder(engine, mouse, SyncPaths.StateDirectory, log);
        touchRecorder.Start();
        keys.RingTrigger += onTrigger;
        // Lock keys: a short label near the cursor.
        using var toast = new ThumbWheelToast(() => engine.Config, () => ring.IsOpen, log);
        app.LockKeyChanged += (_, text) => toast.Show(text);
        // The wheel flipped between ratchet and free-spin (any cause): its haptic event, off unless the user turns it on.
        app.WheelModeFlipped += (_, mode) =>
        {
            engine.PlayHaptic(HapticEvent.WheelModeChange);
            log.Info($"scroll wheel is now {mode}");
        };
        // Settings' "Identify" panel: the tray writes which spare key was pressed where Settings can read it.
        keys.SpareKeyIdentified += (_, cid) => SpareKeyReport.Write(SyncPaths.StateDirectory, cid, log);
        // Fn lock belongs to the keyboard (Fn+Esc): what it reports is saved, so an update or restart never flips it back.
        keys.FnLockChangedOnKeyboard += (_, on) => { var channel = keys.KeyboardChannel; app.PostToUi(() => app.MirrorKeyboardFnLock(on, channel)); };
        engine.StatusChanged += (_, status) =>
        {
            if (!status.Connected) app.PostToUi(() => ring.Close(RingCloseReason.DeviceLost));
        };

        engine.DpiCapabilitiesChanged += (_, _) => app.SetDpiCapabilities(engine.DpiCapabilities);
        app.ConfigChanged += (_, changed) =>
        {
            sync.NotifyConfigChanged();
            engine.ApplyConfig(changed);
            ApplyNotify(changed);
            keys.ApplyConfig(changed);
            // Reopens the headset link only if a flag changed while it is open; off the UI thread because opening touches HID.
            if (yealinkModule is not null)
                _ = Task.Run(() => { var y = engine.Config.Yealink; yealinkModule.ApplySettings(y.LedWrites, y.MuteSync, y.VendorReads); });
            _ = mouse.SetThumbWheelInvertAsync(changed.Pointer.InvertThumbWheel, CancellationToken.None);
            _ = mouse.SetSmartShiftAsync(new SmartShiftSetting(changed.Pointer.SmartShiftEnabled, (byte)changed.Pointer.SmartShiftThreshold), CancellationToken.None);
            _ = mouse.SetSmoothScrollAsync(changed.Pointer.SmoothScroll, CancellationToken.None);
            _ = engine.DpiSettled.ContinueWith(_ => app.RefreshDpi(), TaskScheduler.Default); // the engine writes the per-app DPI in ApplyConfig
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
