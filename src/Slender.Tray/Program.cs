using Slender.Engine;
using Slender.Hidpp;

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

        IConfigStore store = new JsonConfigStore();

        // WIRE: engine. Create the MX Master 4 driver and Slender.Engine here, start the mouse,
        // and pass it in below. Stays null until the engine is wired.
        ILogiMouse? mouse = null;

        using var context = new TrayApplicationContext(store, mouse);
        Application.Run(context);
    }
}
