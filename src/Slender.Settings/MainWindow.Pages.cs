using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>The gear pages: Point and scroll, Haptics, Actions Ring (MainWindow.Ring.cs), General. Plain rows: label and help on the left, control on the right.</summary>
public sealed partial class MainWindow
{
    private void BuildPage()
    {
        PagePanel.Children.Clear();
        PagePanel.Spacing = 0;
        PagePanel.MaxWidth = 660;

        PagePanel.Children.Add(Ui.Text("Settings", 24, Ui.Ink, Ui.Heading));
        ((TextBlock)PagePanel.Children[0]).Margin = new Thickness(0, 0, 0, 14);

        var bar = new SelectorBar { Margin = new Thickness(-8, 0, 0, 10) };
        bar.Items.Add(new SelectorBarItem { Text = "Point & scroll", Tag = Page.Pointer });
        bar.Items.Add(new SelectorBarItem { Text = "Haptics", Tag = Page.Haptics });
        bar.Items.Add(new SelectorBarItem { Text = "Actions Ring", Tag = Page.Ring });
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
            case Page.Ring:
                BuildRingPage();
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

    private void AddRow(string title, string? help, FrameworkElement control, Panel? into = null)
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
        (into ?? PagePanel).Children.Add(wrap);
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

        void Edit(Func<PointerSettings, PointerSettings> f) => _config.Update(x => x with { Pointer = f(x.Pointer) });

        Slider threshold = PageSlider("Sensitivity", PointerMapping.ThresholdMin, PointerMapping.ThresholdMax, 1, c.Pointer.SmartShiftThreshold,
            v => Edit(p => p with { SmartShiftThreshold = PointerMapping.ClampThreshold(v) }), v => $"{PointerMapping.ClampThreshold(v)}", out TextBlock thresholdText);
        threshold.IsEnabled = c.Pointer.SmartShiftEnabled;
        thresholdText.Opacity = c.Pointer.SmartShiftEnabled ? 1 : 0.5;

        AddRow("SmartShift", "Scroll wheel switches between ratchet and free-spin by itself when you spin it fast.",
            PageToggle("SmartShift", c.Pointer.SmartShiftEnabled, on =>
            {
                threshold.IsEnabled = on;
                thresholdText.Opacity = on ? 1 : 0.5;
                Edit(p => p with { SmartShiftEnabled = on });
            }));
        AddRow("Sensitivity", "Lower switches to free-spin sooner.", WithReadout(threshold, thresholdText));

        AddRow("Smooth scrolling", "Smoother scrolling in browsers and Office; older apps still scroll per notch",
            PageToggle("Smooth scrolling", c.Pointer.SmoothScroll, on => Edit(p => p with { SmoothScroll = on })));
        AddRow("Invert thumb wheel", "Roll the thumb wheel the other way to scroll right.",
            PageToggle("Invert thumb wheel", c.Pointer.InvertThumbWheel, on => Edit(p => p with { InvertThumbWheel = on })));
        AddRow("Invert main wheel", "Spin the wheel toward you to scroll up.",
            PageToggle("Invert main wheel", c.Pointer.InvertWheel, on => Edit(p => p with { InvertWheel = on })));

        AddRow("Wheel resistance", "How hard the wheel is to turn in ratchet mode. Default keeps the mouse's own setting.",
            DefaultableSlider("Wheel resistance", 0, 100, 1, c.Pointer.WheelResistance,
                () => _status.Device("mx-master-4")?.WheelResistance, PointerMapping.WheelResistanceText,
                v => Edit(p => p with { WheelResistance = v }), 75));

        DeviceState? mouse = _status.Device("mx-master-4");
        if (mouse is { SensePanelForceMin: { } fMin, SensePanelForceMax: { } fMax })
        {
            int? SliderNow() => _status.Device("mx-master-4")?.SensePanelForce is { } f ? PointerMapping.ForceToSlider(f, fMin, fMax) : null;
            AddRow("Sense Panel press force", "How hard you press the thumb panel before it counts. Light to Firm. Default keeps the mouse's own setting.",
                DefaultableSlider("Sense Panel press force", 0, 100, 1,
                    c.Pointer.SensePanelForce is { } saved ? PointerMapping.ForceToSlider(saved, fMin, fMax) : null, SliderNow,
                    v => PointerMapping.ForceText(v),
                    v => Edit(p => p with { SensePanelForce = v is { } s ? PointerMapping.SliderToForce(s, fMin, fMax) : null }), 50));
        }
        else
        {
            AddRow("Sense Panel press force", "Appears once the tray has read your mouse.", Ui.Text("Not available yet", 13, Ui.Muted));
        }

        int? DeviceDpi() => _status.Device("mx-master-4")?.Dpi; // what the mouse reports now (status.json); shown while the speed is Default
        int RestDpi() => DeviceDpi() is { } dd ? PointerMapping.SnapDpi(dd) : PointerMapping.DpiShown;
        Slider dpi = PageSlider("Pointer speed", PointerMapping.DpiMin, PointerMapping.DpiMax, PointerMapping.DpiStep, c.Pointer.Dpi ?? RestDpi(),
            v => { }, v => PointerMapping.DpiText(PointerMapping.SnapDpi(v)), out TextBlock dpiText);
        dpiText.Width = 120;
        bool setting = false;
        void ShowDefault(bool isDefault)
        {
            dpiText.Text = isDefault ? PointerMapping.DefaultText(DeviceDpi()) : PointerMapping.DpiText(PointerMapping.SnapDpi(dpi.Value));
            dpiText.Opacity = isDefault ? 0.6 : 1;
        }

        ShowDefault(c.Pointer.Dpi is null);
        dpi.ValueChanged += (_, e) =>
        {
            if (setting)
            {
                return;
            }

            int v = PointerMapping.SnapDpi(e.NewValue);
            ShowDefault(false);
            Edit(p => p with { Dpi = v });
        };
        var reset = new Button { Content = "Default", Style = Ui.Res<Style>("QuietButtonStyle") };
        reset.Click += (_, _) =>
        {
            setting = true;
            dpi.Value = RestDpi();
            setting = false;
            ShowDefault(true);
            Edit(p => p with { Dpi = null });
        };
        var dpiRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        dpiRow.Children.Add(dpi);
        dpiRow.Children.Add(dpiText);
        dpiRow.Children.Add(reset);
        void OnDpiStatus()
        {
            if (_config.Config.Pointer.Dpi is null)
            {
                setting = true;
                dpi.Value = RestDpi();
                setting = false;
                ShowDefault(true);
            }
        }

        _status.Changed += OnDpiStatus;
        dpiRow.Unloaded += (_, _) => _status.Changed -= OnDpiStatus;
        AddRow("Pointer speed", "Dots per inch. Default keeps the speed the mouse already has.", dpiRow);

        AddSection("EXCEL");
        AddRow("Thumb wheel scrolls columns", "In Excel the thumb wheel moves by whole columns instead of pixels.",
            PageToggle("Thumb wheel scrolls columns", c.Excel.ThumbWheelScrollsColumns, on => _config.Update(x => x with { Excel = x.Excel with { ThumbWheelScrollsColumns = on } })));
        Slider columns = PageSlider("Columns per notch", 1, 10, 1, c.Excel.ColumnsPerNotch,
            v => _config.Update(x => x with { Excel = x.Excel with { ColumnsPerNotch = (int)v } }), v => $"{(int)v}", out TextBlock columnsText);
        AddRow("Columns per notch", null, WithReadout(columns, columnsText));

        BuildDualThumbSection();
    }

    /// <summary>
    /// A slider with a Default button, like the pointer speed row: null = Slender leaves the device alone and the readout shows
    /// the mouse's own value (from status.json); moving the slider saves a value. Values are in slider units.
    /// </summary>
    private FrameworkElement DefaultableSlider(string name, double min, double max, double step, int? saved, Func<int?> deviceValue,
        Func<double, string> format, Action<int?> save, int fallback)
    {
        int Rest() => deviceValue() is { } d ? Math.Clamp(d, (int)min, (int)max) : fallback;
        Slider slider = PageSlider(name, min, max, step, saved ?? Rest(), v => { }, format, out TextBlock text);
        text.Width = 120;
        bool setting = false;
        void ShowDefault(bool isDefault)
        {
            text.Text = isDefault ? (deviceValue() is { } d ? $"Default ({format(Math.Clamp(d, min, max))})" : "Default") : format(slider.Value);
            text.Opacity = isDefault ? 0.6 : 1;
        }

        ShowDefault(saved is null);
        slider.ValueChanged += (_, e) =>
        {
            if (setting)
            {
                return;
            }

            ShowDefault(false);
            save((int)Math.Round(e.NewValue));
        };
        var reset = new Button { Content = "Default", Style = Ui.Res<Style>("QuietButtonStyle") };
        reset.Click += (_, _) =>
        {
            setting = true;
            slider.Value = Rest();
            setting = false;
            ShowDefault(true);
            save(null);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(slider);
        row.Children.Add(text);
        row.Children.Add(reset);
        bool IsDefault() => text.Opacity < 1;
        void OnStatus()
        {
            if (IsDefault())
            {
                setting = true;
                slider.Value = Rest();
                setting = false;
                ShowDefault(true);
            }
        }

        _status.Changed += OnStatus;
        row.Unloaded += (_, _) => _status.Changed -= OnStatus;
        return row;
    }

    private void BuildHapticsPage()
    {
        SlenderConfig c = _config.Config;
        AddSection("MX MASTER 4");
        AddRow("Haptic feedback", "The mouse taps back when something happens.",
            PageToggle("Haptic feedback", c.Haptics.Enabled, on => _config.Update(x => x with { Haptics = x.Haptics with { Enabled = on } })));

        // 0-100 strength; the four notches in the bottom bar stay as quick presets (25/50/75/100) and the slider moves them too.
        Slider level = PageSlider("Haptic strength", 0, 100, 5, c.Haptics.EffectiveStrength,
            v => _config.Update(x => x with { Haptics = x.Haptics with { Strength = (int)v, Level = HapticSettings.NotchFor((int)v) } }), v => $"{(int)v}", out TextBlock levelText);
        levelText.Width = 80;
        AddRow("Haptic strength", "How strong the tap is, 0 to 100. The four notches at the bottom are quick presets.", WithReadout(level, levelText));

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

        BuildSyncSection();

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

        var import = new Button { Content = "Import…", Style = Ui.Res<Style>("QuietButtonStyle") };
        import.Click += async (_, _) => await ImportFromOptionsPlusAsync();
        AddRow("Import from Options+", "Copies your Logi Options+ buttons and settings into Slender. You see a preview first, and the current settings are backed up.", import);

        BuildFirmwareSection();

        AddSection("ABOUT");
        BuildVersionRow();
    }

    /// <summary>The version row: what the tray published about updates (status.json) and two buttons that ask the tray to act.</summary>
    private void BuildVersionRow()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var check = new Button { Content = "Check now", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = _status.TrayRunning };
        check.Click += (_, _) => AskTray(UpdateMessages.CheckName);
        buttons.Children.Add(check);
        if (_status.Status?.UpdateReady == true)
        {
            var apply = new Button { Content = "Restart and update", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = _status.TrayRunning };
            apply.Click += (_, _) => AskTray(UpdateMessages.ApplyName);
            buttons.Children.Add(apply);
        }

        string own = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0";
        AddRow("Slender", UpdateText.Version(_status.Status, own), buttons);
    }

    /// <summary>Posts one of the registered update messages to the tray's hidden window. The tray does the update work.</summary>
    private static void AskTray(string messageName)
    {
        nint tray = Services.NativeMethods.FindWindowW(UpdateMessages.TrayWindowClass, null);
        uint message = Services.NativeMethods.RegisterWindowMessageW(messageName);
        if (tray != 0 && message != 0)
        {
            Services.NativeMethods.PostMessageW(tray, message, 0, 0);
        }
    }

    private const string OptionsPlusExe = @"C:\Program Files\LogiOptionsPlus\logioptionsplus.exe";

    /// <summary>One row per device whose firmware version the tray published (status.json). Nothing is shown when it did not.</summary>
    private void BuildFirmwareSection()
    {
        bool any = false;
        foreach (DeviceKind kind in new[] { DeviceKind.Mouse, DeviceKind.Keyboard })
        {
            if (!_status.Firmware.TryGetValue(ControlCatalog.DeviceId(kind), out string? version))
            {
                continue;
            }

            if (!any)
            {
                AddSection("FIRMWARE");
                any = true;
            }

            var update = new Button { Content = "Update firmware…", Style = Ui.Res<Style>("QuietButtonStyle") };
            AutomationProperties.SetName(update, $"Update {ControlCatalog.DeviceName(kind)} firmware");
            update.Click += async (_, _) => await ConfirmFirmwareUpdateAsync();
            AddRow($"Firmware · {ControlCatalog.DeviceName(kind)}", $"Version {version}", update);
        }
    }

    private async Task ConfirmFirmwareUpdateAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Update firmware",
            Content = new TextBlock
            {
                Text = "Slender will open Logi Options+ to update firmware, then you can close it and Slender resumes.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string? failure = null;
        if (!File.Exists(OptionsPlusExe))
        {
            failure = "Logi Options+ isn't installed on this PC, so Slender can't update the firmware. Install Logi Options+ from logitech.com, then try again.";
        }
        else
        {
            try
            {
                using Process? p = Process.Start(new ProcessStartInfo(OptionsPlusExe) { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
            {
                Services.CrashLog.Write(e);
                failure = "Logi Options+ couldn't be started. Open it from the Start menu to update the firmware.";
            }
        }

        if (failure is not null)
        {
            await new ContentDialog
            {
                Title = "Can't open Logi Options+",
                Content = new TextBlock { Text = failure, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
                XamlRoot = Root.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            }.ShowAsync();
        }
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
