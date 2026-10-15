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

        using var mouse = new LogiMouse(new WindowsHidTransportFactory());
        using var hook = new LowLevelMouseHook(log);
        using var foreground = new ForegroundAppWatcher(log);
        using var excel = new ExcelBridge(log);
        var executor = new ActionExecutor(excel, foreground, log);
        using var engine = new SlenderEngine(mouse, hook, foreground, excel, executor, store.Load(), log);

        using var context = new TrayApplicationContext(store, mouse, foreground: foreground, log: log);
        context.ConfigChanged += (_, config) => engine.ApplyConfig(config);
        engine.Start();

        Application.Run(context);
        log.Info("Slender stopping");
    }
}
