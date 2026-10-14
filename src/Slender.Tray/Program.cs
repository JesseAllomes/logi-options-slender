using System.Diagnostics;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;
using Slender.Hidpp.Transport;

namespace Slender.Tray;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        // Options+ and Slender must never both own HID++ diverts (docs/switch-over.md).
        if (Process.GetProcessesByName("logioptionsplus_agent").Length > 0)
        {
            MessageBox.Show(
                "Logi Options+ is running. Quit it first; both cannot control the mouse at the same time.",
                "Slender", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var log = new FileLog();
        log.Info("Slender starting");
        IConfigStore store = new JsonConfigStore();

        var config = store.Load();
        using var mouse = new LogiMouse(new WindowsHidTransportFactory(),
            new LogiMouseOptions
            {
                ThumbWheelInvert = config.Pointer.InvertThumbWheel,
                SmartShift = new SmartShiftSetting(config.Pointer.SmartShiftEnabled, (byte)config.Pointer.SmartShiftThreshold),
            });
        using var hook = new LowLevelMouseHook(log);
        using var foreground = new ForegroundAppWatcher(log);
        using var excel = new ExcelBridge(log);
        var executor = new ActionExecutor(excel, foreground, log);
        using var engine = new SlenderEngine(mouse, hook, foreground, excel, executor, config, log);

        using var context = new TrayApplicationContext(store, mouse);
        context.ConfigChanged += (_, changed) =>
        {
            engine.ApplyConfig(changed);
            _ = mouse.SetThumbWheelInvertAsync(changed.Pointer.InvertThumbWheel, CancellationToken.None);
            _ = mouse.SetSmartShiftAsync(new SmartShiftSetting(changed.Pointer.SmartShiftEnabled, (byte)changed.Pointer.SmartShiftThreshold), CancellationToken.None);
        };
        engine.Start();

        Application.Run(context);
        log.Info("Slender stopping");
    }
}
