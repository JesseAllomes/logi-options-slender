using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>The gear pages: Point and scroll, Haptics, General. Plain rows: label and help on the left, control on the right.</summary>
public sealed partial class MainWindow
{
    private void BuildPage()
    {
        PagePanel.Children.Clear();
        PagePanel.Spacing = 0;

        PagePanel.Children.Add(Ui.Text("Settings", 24, Ui.Ink, Ui.Heading));
        ((TextBlock)PagePanel.Children[0]).Margin = new Thickness(0, 0, 0, 14);

        var bar = new SelectorBar { Margin = new Thickness(-8, 0, 0, 10) };
        bar.Items.Add(new SelectorBarItem { Text = "Point & scroll", Tag = Page.Pointer });
        bar.Items.Add(new SelectorBarItem { Text = "Haptics", Tag = Page.Haptics });
        bar.Items.Add(new SelectorBarItem { Text = "General", Tag = Page.General });
        bar.SelectedItem = bar.Items.First(i => (Page)i.Tag == _page);
        bar.SelectionChanged += (s, _) =>
        {
            if (s.SelectedItem?.Tag is Page page && page != _page)
            {
                _page = page;
                RebuildAll();
            }
        };
        PagePanel.Children.Add(bar);

        switch (_page)
        {
            case Page.Pointer:
                BuildPointerPage();
                break;
            case Page.Haptics:
                BuildHapticsPage();
                break;
            default:
                BuildGeneralPage();
                break;
        }
    }

    private void AddSection(string text)
    {
        var t = Ui.Text(text, 12, Ui.Muted, Ui.Heading);
        t.Margin = new Thickness(0, 18, 0, 6);
        PagePanel.Children.Add(t);
    }

    private void AddRow(string title, string? help, FrameworkElement control)
    {
        var grid = new Grid { Padding = new Thickness(0, 12, 0, 12), ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(title, 14));
        if (help is not null)
        {
            var h = Ui.Text(help, 12, Ui.Muted);
            h.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(h);
        }

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);

        var wrap = new StackPanel();
        wrap.Children.Add(grid);
        wrap.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
        PagePanel.Children.Add(wrap);
    }

    private ToggleSwitch PageToggle(string name, bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, Width = 44 };
        AutomationProperties.SetName(toggle, name);
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }

    private Slider PageSlider(string name, double min, double max, double step, double value, Action<double> changed, Func<double, string> format, out TextBlock readout)
    {
        var text = Ui.Text(format(value), 13, Ui.Muted);
        text.Width = 64;
        text.TextAlignment = TextAlignment.Right;
        text.VerticalAlignment = VerticalAlignment.Center;
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = step, Value = value, Width = 200, IsThumbToolTipEnabled = false };
        AutomationProperties.SetName(slider, name);
        slider.ValueChanged += (_, e) =>
        {
            text.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        readout = text;
        return slider;
    }

    private static FrameworkElement WithReadout(Slider slider, TextBlock readout)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(slider);
        row.Children.Add(readout);
        return row;
    }

    private void BuildPointerPage()
    {
        SlenderConfig c = _config.Config;
        AddSection("MX MASTER 4");

        bool custom = c.Pointer.Dpi is not null;
        Slider dpi = PageSlider("Pointer speed", 400, 8000, 100, c.Pointer.Dpi ?? 1600,
            v => _config.Update(x => x with { Pointer = x.Pointer with { Dpi = (int)v } }), v => $"{(int)v} DPI", out TextBlock dpiText);
        dpi.IsEnabled = custom;
        dpiText.Opacity = custom ? 1 : 0.5;
        ToggleSwitch dpiToggle = PageToggle("Set pointer speed", custom, on =>
        {
            dpi.IsEnabled = on;
            dpiText.Opacity = on ? 1 : 0.5;
            _config.Update(x => x with { Pointer = x.Pointer with { Dpi = on ? (int)dpi.Value : null } });
        });
        AddRow("Set the pointer speed", "Leave it off to keep the speed the mouse already has.", dpiToggle);
        AddRow("Pointer speed", "Dots per inch.", WithReadout(dpi, dpiText));

        AddRow("SmartShift", "Scroll wheel switches between ratchet and free-spin by itself when you spin it fast.",
            PageToggle("SmartShift", c.Pointer.SmartShiftEnabled, on => _config.Update(x => x with { Pointer = x.Pointer with { SmartShiftEnabled = on } })));

        Slider threshold = PageSlider("SmartShift sensitivity", 1, 50, 1, c.Pointer.SmartShiftThreshold,
            v => _config.Update(x => x with { Pointer = x.Pointer with { SmartShiftThreshold = (int)v } }), v => $"{(int)v}", out TextBlock thresholdText);
        AddRow("SmartShift sensitivity", "Lower switches to free-spin sooner.", WithReadout(threshold, thresholdText));

        AddRow("Reverse the scroll wheel", "Spin the wheel toward you to scroll up.",
            PageToggle("Reverse the scroll wheel", c.Pointer.InvertWheel, on => _config.Update(x => x with { Pointer = x.Pointer with { InvertWheel = on } })));
        AddRow("Reverse the thumb wheel", "Roll the thumb wheel the other way to scroll right.",
            PageToggle("Reverse the thumb wheel", c.Pointer.InvertThumbWheel, on => _config.Update(x => x with { Pointer = x.Pointer with { InvertThumbWheel = on } })));

        AddSection("EXCEL");
        AddRow("Thumb wheel scrolls columns", "In Excel the thumb wheel moves by whole columns instead of pixels.",
            PageToggle("Thumb wheel scrolls columns", c.Excel.ThumbWheelScrollsColumns, on => _config.Update(x => x with { Excel = x.Excel with { ThumbWheelScrollsColumns = on } })));
        Slider columns = PageSlider("Columns per notch", 1, 10, 1, c.Excel.ColumnsPerNotch,
            v => _config.Update(x => x with { Excel = x.Excel with { ColumnsPerNotch = (int)v } }), v => $"{(int)v}", out TextBlock columnsText);
        AddRow("Columns per notch", null, WithReadout(columns, columnsText));
    }

    private void BuildHapticsPage()
    {
        SlenderConfig c = _config.Config;
        AddSection("MX MASTER 4");
        AddRow("Haptic feedback", "The mouse taps back when something happens.",
            PageToggle("Haptic feedback", c.Haptics.Enabled, on => _config.Update(x => x with { Haptics = x.Haptics with { Enabled = on } })));

        Slider level = PageSlider("Haptic feel", 0, 3, 1, c.Haptics.Level,
            v => _config.Update(x => x with { Haptics = x.Haptics with { Level = (int)v } }), v => HapticNames[(int)v], out TextBlock levelText);
        levelText.Width = 80;
        AddRow("Haptic feel", "How strong the tap is.", WithReadout(level, levelText));

        AddRow("When you press a button", null,
            PageToggle("Haptics on button press", c.Haptics.OnButtonPress, on => _config.Update(x => x with { Haptics = x.Haptics with { OnButtonPress = on } })));
        AddRow("When the Actions Ring opens", null,
            PageToggle("Haptics on the Actions Ring", c.Haptics.OnActionsRing, on => _config.Update(x => x with { Haptics = x.Haptics with { OnActionsRing = on } })));
    }

    private void BuildGeneralPage()
    {
        SlenderConfig c = _config.Config;
        AddSection("STARTUP");
        AddRow("Launch at login", "Slender starts in the tray when you sign in to Windows.",
            PageToggle("Launch at login", c.LaunchAtLogin, on => _config.Update(x => x with { LaunchAtLogin = on })));

        AddSection("KEYBOARD");
        AddRow("F-keys first (Fn lock)", "Function keys act as F1 to F12 without holding Fn.",
            PageToggle("F-keys first", c.Keyboard.FnLock, on => _config.Update(x => x with { Keyboard = x.Keyboard with { FnLock = on } })));

        AddSection("FILES");
        var open = new Button { Content = "Open folder", Style = Ui.Res<Style>("QuietButtonStyle") };
        open.Click += (_, _) => OpenFolder(SlenderPaths.ConfigDirectory);
        AddRow("Settings file", "config.json. Slender saves every change here as you make it.", open);

        var refresh = new Button { Content = "Refresh pictures", Style = Ui.Res<Style>("QuietButtonStyle") };
        refresh.Click += async (_, _) => await RefreshDevicePicturesAsync(refresh);
        AddRow("Device pictures", "Copied from Logi Options+ on this PC. Refresh them after Options+ updates.", refresh);

        AddSection("ABOUT");
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0";
        AddRow("Slender", $"Version {version}", new TextBlock());
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using Process? p = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Services.CrashLog.Write(e);
        }
    }

    private async Task RefreshDevicePicturesAsync(Button button)
    {
        button.IsEnabled = false;
        foreach (DeviceKind kind in new[] { DeviceKind.Mouse, DeviceKind.Keyboard })
        {
            string dir = Path.Combine(SlenderPaths.AssetsDirectory, ControlCatalog.DeviceId(kind));
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Services.CrashLog.Write(e);
            }

            _devices[kind] = await Task.Run(() => _assets.Ensure(kind));
        }

        button.IsEnabled = true;
        RebuildAll();
    }
}
