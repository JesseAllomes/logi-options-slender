using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    /// <summary>Pointer speed per app: one row per app profile. Default follows All apps (then the speed above, else the mouse's own).</summary>
    private void BuildAppDpiSection()
    {
        IReadOnlyList<Profile> apps = AppDpiModel.Apps(_config.Config);
        if (apps.Count == 0)
        {
            return;
        }

        AddSection("POINTER SPEED BY APP");
        DpiScale scale = DpiScale.From(_status.Device("mx-master-4")); // the sensor's real range once the tray has read it
        foreach (Profile app in apps)
        {
            string process = app.AppProcess!;
            string name = ProfileOps.AppName(app);
            FrameworkElement row = DefaultableSlider($"Pointer speed in {name}", scale.Min, scale.Max, scale.Step,
                AppDpiModel.Own(_config.Config, process),
                () => AppDpiModel.Inherited(_config.Config) ?? _status.Device("mx-master-4")?.Dpi,
                v => PointerMapping.DpiText(scale.Snap(v)),
                v => _config.Update(x => AppDpiModel.Set(x, process, v, scale)), PointerMapping.DpiShown);
            AddRow(name, "Slender switches the mouse to this speed while the app is in front, and back when you leave it.", row);
        }
    }

    /// <summary>One row per device: battery level and the days-left estimate from its history.</summary>
    private void BuildBatterySection()
    {
        AddSection("BATTERY");
        foreach (DeviceKind kind in new[] { DeviceKind.Mouse, DeviceKind.Keyboard })
        {
            AddRow(ControlCatalog.DeviceName(kind), "Days left comes from how fast the battery has been draining. It needs a few days of use first.",
                Ui.Text(BatteryLine.Text(_status.Device(ControlCatalog.DeviceId(kind))), 13, Ui.Muted));
        }
    }
}
