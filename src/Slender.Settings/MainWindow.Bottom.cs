using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    private static readonly string[] HapticNames = ["Very light", "Light", "Medium", "Strong"];

    private InfoBar _infoTray = null!;
    private InfoBar _infoDevice = null!;
    private InfoBar _infoOptions = null!;
    private InfoBar _infoBattery = null!;

    private ToggleSwitch? _smartShiftToggle;
    private ToggleSwitch? _fnLockToggle;
    private ToggleSwitch? _pageFnToggle;     // General page
    private ComboBox[]? _channelChoices;     // General page, per Easy-Switch channel
    private bool _syncingFn;                 // a refresh is setting the Fn controls: not a user edit
    private readonly List<Rectangle> _notchBars = [];

    // ---- InfoBars at the top of the stage ----

    private void BuildInfoBars()
    {
        _infoTray = new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            IsClosable = false,
            Title = "Slender isn't running",
            Message = "Start it from the Start menu. Changes you make here are saved and apply when it starts.",
        };

        _infoDevice = new InfoBar { Severity = InfoBarSeverity.Informational, IsClosable = false };

        var quit = new Button { Content = "Quit Options+", Style = Ui.Res<Style>("QuietButtonStyle") };
        quit.Click += async (_, _) =>
        {
            quit.IsEnabled = false;
            await Task.Run(StatusMonitor.QuitOptionsPlus);
            quit.IsEnabled = true;
            UpdateInfoBars();
        };
        _infoOptions = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            Title = "Logi Options+ is running",
            Message = "Quit it so Slender can control your mouse. Both cannot do it at once.",
            ActionButton = quit,
        };

        _infoBattery = new InfoBar { Severity = InfoBarSeverity.Warning, IsClosable = false };

        foreach (InfoBar bar in new[] { _infoTray, _infoOptions, _infoDevice, _infoBattery })
        {
            bar.IsOpen = false;
            InfoHost.Children.Add(bar);
        }
    }

    private void UpdateInfoBars()
    {
        string deviceName = ControlCatalog.DeviceName(_kind);
        DeviceState? state = _status.Device(ControlCatalog.DeviceId(_kind));
        bool trayDown = _status.Status is null || !_status.TrayRunning;

        _infoTray.IsOpen = trayDown;
        _infoOptions.IsOpen = _status.OptionsPlusRunning;

        string deviceId = ControlCatalog.DeviceId(_kind);
        var info = trayDown || !DevicePresenceView.Visible(_status.Status, deviceId)
            ? null
            : DevicePresenceView.InfoBar(_status.Status, deviceId, deviceName, _kind == DeviceKind.Mouse);
        _infoDevice.IsOpen = info is not null;
        if (info is { } text)
        {
            _infoDevice.Title = text.Title;
            _infoDevice.Message = text.Message;
        }

        bool low = !trayDown && !DevicePresenceView.Dimmed(_status.Status, deviceId) && state is { BatteryPercent: < 20, Charging: false };
        _infoBattery.IsOpen = low;
        if (low && state?.BatteryPercent is { } percent)
        {
            _infoBattery.Title = $"{deviceName} battery is at {percent}%";
            _infoBattery.Message = "Charge it soon.";
        }
    }

    // ---- bottom bar ----

    private void BuildBottomBar()
    {
        BottomBarItems.Children.Clear();
        _notchBars.Clear();
        _smartShiftToggle = null;
        _fnLockToggle = null;

        if (_kind == DeviceKind.Mouse)
        {
            BottomBarItems.Children.Add(LabelWith("Haptic feel", HapticNotches()));
            _smartShiftToggle = Toggle("SmartShift free-spin", on => _config.Update(c => c with { Pointer = c.Pointer with { SmartShiftEnabled = on } }));
            ToolTipService.SetToolTip(_smartShiftToggle, "On: the scroll wheel switches to free-spin by itself when you spin it fast.");
            BottomBarItems.Children.Add(LabelWith("SmartShift free-spin", _smartShiftToggle));
        }
        else
        {
            _fnLockToggle = Toggle("F-keys first (Fn lock)", on =>
            {
                if (!_syncingFn) _config.Update(c => c with { Keyboard = c.Keyboard with { FnLock = on } });
            });
            ToolTipService.SetToolTip(_fnLockToggle, "On: F1 to F12 work without holding Fn; hold Fn for the media keys. Fn+Esc on the keyboard flips it too.");
            BottomBarItems.Children.Add(LabelWith("F-keys first (Fn lock)", _fnLockToggle));
            BottomBarItems.Children.Add(LabelWith("Easy-Switch", EasySwitchChips()));
        }

        SyncBottomBar();
    }

    private static FrameworkElement LabelWith(string label, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Ui.Text(label, 13));
        row.Children.Add(control);
        return row;
    }

    private ToggleSwitch Toggle(string name, Action<bool> changed)
    {
        var toggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            MinWidth = 0,
            Width = 44,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(toggle, name);
        toggle.Toggled += (_, _) =>
        {
            if (!_syncing)
            {
                changed(toggle.IsOn);
            }
        };
        return toggle;
    }

    private FrameworkElement HapticNotches()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            int level = i;
            var bar = new Rectangle { Width = 20, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Ui.Brush("Paper3ActiveBrush") };
            _notchBars.Add(bar);
            var button = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                Width = 24,
                Height = 28,
                Content = bar,
            };
            AutomationProperties.SetName(button, $"Haptic feel: {HapticNames[level]}");
            ToolTipService.SetToolTip(button, $"{HapticNames[level]}. {HapticFeelTip}");
            button.Click += (_, _) => _config.Update(c =>
            {
                bool off = c.Haptics.Enabled && c.Haptics.Level == 0 && level == 0; // tapping the only lit notch turns haptics off
                return c with { Haptics = c.Haptics with { Enabled = !off, Level = level, Strength = (level + 1) * 25 } };
            });
            row.Children.Add(button);
        }

        return row;
    }

    private FrameworkElement EasySwitchChips()
    {
        // The host currently in use is not part of the status contract yet, so the chips only describe the three slots.
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        string[] labels = ["1", "2", "3"];
        foreach (string l in labels)
        {
            row.Children.Add(Ui.Chip(l, 12));
        }

        ToolTipService.SetToolTip(row, "The keyboard's Easy-Switch keys 1, 2 and 3. Each one connects it to a different computer or phone; press one on the keyboard to switch.");
        return row;
    }

    private void SyncBottomBar()
    {
        _syncing = true;
        try
        {
            SlenderConfig c = _config.Config;
            if (_smartShiftToggle is not null)
            {
                _smartShiftToggle.IsOn = c.Pointer.SmartShiftEnabled;
            }

            if (_fnLockToggle is not null && _fnLockToggle.IsOn != c.Keyboard.FnLock)
            {
                _syncingFn = true;
                try { _fnLockToggle.IsOn = c.Keyboard.FnLock; }
                finally { _syncingFn = false; }
            }

            int lit = c.Haptics.Enabled ? HapticSettings.NotchFor(c.Haptics.EffectiveStrength) + 1 : 0;
            for (int i = 0; i < _notchBars.Count; i++)
            {
                _notchBars[i].Fill = i < lit ? Ui.Mark : Ui.Brush("Paper3ActiveBrush");
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
