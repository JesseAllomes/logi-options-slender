using System.Diagnostics;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;
using Slender.Hidpp.Transport;

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
        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        var log = new FileLog();
        log.Info("Slender starting");
        IConfigStore store = new JsonConfigStore();

        var config = store.Load();
        using var mouse = new LogiMouse(new WindowsHidTransportFactory(),
            new LogiMouseOptions
            {
                ThumbWheelInvert = config.Pointer.InvertThumbWheel,
                SmartShift = new SmartShiftSetting(config.Pointer.SmartShiftEnabled, (byte)config.Pointer.SmartShiftThreshold),
                SmoothScroll = config.Pointer.SmoothScroll,
                Dpi = config.Pointer.Dpi,
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
        context.ConfigChanged += (_, changed) =>
        {
            engine.ApplyConfig(changed);
            keys.ApplyConfig(changed);
            _ = mouse.SetThumbWheelInvertAsync(changed.Pointer.InvertThumbWheel, CancellationToken.None);
            _ = mouse.SetSmartShiftAsync(new SmartShiftSetting(changed.Pointer.SmartShiftEnabled, (byte)changed.Pointer.SmartShiftThreshold), CancellationToken.None);
            _ = mouse.SetSmoothScrollAsync(changed.Pointer.SmoothScroll, CancellationToken.None);
            _ = mouse.SetDpiAsync(changed.Pointer.Dpi, CancellationToken.None);
        };
        Application.Run(context);
        log.Info("Slender stopping");
    }
}
