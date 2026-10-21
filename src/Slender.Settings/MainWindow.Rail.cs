using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;
using Windows.Storage.Pickers;
using Windows.System;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    private string _stripSignature = string.Empty;

    // ---- rail: devices and the gear ----

    private void BuildRail()
    {
        RailPanel.Children.Clear();
        RailVersion.Text = "v" + OwnVersion;
        foreach (DeviceKind kind in new[] { DeviceKind.Mouse, DeviceKind.Keyboard })
        {
            RailPanel.Children.Add(DeviceTile(kind));
        }

        RailPanel.Children.Add(new Border { Height = 1, Background = Ui.Rule, Margin = new Thickness(8, 6, 8, 6) });
        RailPanel.Children.Add(GearTile());
    }

    private Button DeviceTile(DeviceKind kind)
    {
        bool selected = _kind == kind && _page == Page.Device;
        DeviceState? state = _status.Device(ControlCatalog.DeviceId(kind));
        bool offline = _status.Status is null || state is not { Connected: true };

        var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        CachedDevice? device = _devices.GetValueOrDefault(kind);
        FrameworkElement thumb;
        if (device is not null)
        {
            var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            if (kind == DeviceKind.Mouse)
            {
                image.Height = 44;
                image.Source = Ui.Bitmap(device.ThumbPath, decodePixelHeight: (int)Math.Ceiling(44 * _scale));
            }
            else
            {
                image.Width = 48;
                image.Source = Ui.Bitmap(device.ThumbPath, decodePixelWidth: (int)Math.Ceiling(48 * _scale));
            }

            thumb = image;
        }
        else
        {
            thumb = new Grid
            {
                Height = 44,
                Children = { new FontIcon { Glyph = kind == DeviceKind.Mouse ? "" : "", FontSize = 26, Foreground = Ui.Muted } },
            };
        }

        thumb.Opacity = offline ? 0.5 : 1;
        content.Children.Add(thumb);
        content.Children.Add(BatteryText(state, offline));

        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = 60,
            Height = 76,
            CornerRadius = new CornerRadius(10),
            Background = selected ? Ui.Paper3 : Ui.Brush("RailTransparent"),
            BorderBrush = selected ? Ui.Mark : Ui.Brush("RailTransparent"),
            BorderThickness = new Thickness(1.5),
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        string battery = state?.BatteryPercent is { } p ? $", battery {p} percent" : string.Empty;
        AutomationProperties.SetName(button, ControlCatalog.DeviceName(kind) + battery + (offline ? ", not connected" : string.Empty));
        ToolTipService.SetToolTip(button, ControlCatalog.DeviceName(kind) + (!offline && state?.BatteryEstimate is { Length: > 0 } estimate && state.Charging is false ? " · " + estimate : string.Empty));
        button.Click += (_, _) => SelectDevice(kind);
        return button;
    }

    private static FrameworkElement BatteryText(DeviceState? state, bool offline)
    {
        string text = state is null ? "—" : offline ? "Off" : state.BatteryPercent is { } p ? $"{p}%" : state.BatteryText ?? "—";
        bool low = state is { Connected: true, BatteryPercent: < 20, Charging: false };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        if (state is { Connected: true, Charging: true })
        {
            row.Children.Add(Ui.Glyph("", 10, Ui.Live));
        }

        row.Children.Add(Ui.Text(text, 11.5, low ? Ui.Warn : Ui.Muted));
        return row;
    }

    private Button GearTile()
    {
        bool selected = _page != Page.Device;
        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = selected ? Ui.MarkSoft : Ui.Brush("RailTransparent"),
            BorderBrush = selected ? Ui.Mark : Ui.Brush("RailTransparent"),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new Grid { Children = { new FontIcon { Glyph = "", FontSize = 18, Foreground = Ui.Mark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } },
        };
        AutomationProperties.SetName(button, "Point and scroll, haptics, actions ring and general settings");
        ToolTipService.SetToolTip(button, "Point & scroll, Haptics, Actions Ring, General");
        button.Click += (_, _) => OpenPage(_page == Page.Device ? Page.Pointer : _page);
        return button;
    }

    // ---- app strip ----

    private void BuildStrip()
    {
        List<Profile> apps = _config.Config.Profiles.Where(p => p.AppProcess is not null).ToList();
        string signature = string.Join('|', apps.Select(p => p.AppProcess + ":" + ProfileOps.AppName(p))) + "#" + _app + "#" + _page;
        if (signature == _stripSignature)
        {
            return;
        }

        _stripSignature = signature;
        StripPanel.Children.Clear();
        StripPanel.Children.Add(StripTile(null, "All apps", null, 0));
        int index = 1;
        foreach (Profile p in apps)
        {
            StripPanel.Children.Add(StripTile(p, ProfileOps.AppName(p), p.AppProcess, index++));
        }

        StripPanel.Children.Add(AddAppTile());
    }

    private Button StripTile(Profile? profile, string name, string? process, int index)
    {
        bool selected = string.Equals(process, _app, StringComparison.OrdinalIgnoreCase) && AppPage;
        FrameworkElement icon;
        if (profile is null)
        {
            icon = Ui.AllAppsGlyph(40);
        }
        else
        {
            var image = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform };
            var holder = new Grid { Width = 40, Height = 40 };
            holder.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = Ui.Paper3,
                Child = new FontIcon { Glyph = "", FontSize = 18, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            holder.Children.Add(image);
            _ = LoadIconAsync(image, holder, profile.AppProcess!, profile.ExePath, 40);
            icon = holder;
        }

        var ring = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? Ui.Mark : Ui.Brush("RailTransparent"),
            Background = selected ? Ui.MarkSoft : Ui.Brush("RailTransparent"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Grid { Children = { icon } },
        };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;

        var label = Ui.Text(name, 11, selected ? Ui.Ink : Ui.Muted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        label.MaxWidth = 62;

        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = 62,
            Padding = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new StackPanel { Spacing = 3, Children = { ring, label } },
        };
        string shortcut = index < 9 ? $"Ctrl+{index + 1}" : string.Empty;
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, shortcut.Length > 0 ? $"{name} ({shortcut})" : name);
        button.Click += (_, _) => SelectApp(process);

        if (profile is not null)
        {
            var remove = new MenuFlyoutItem { Text = "Remove app", Icon = new FontIcon { Glyph = "" } };
            remove.Click += async (_, _) => await ConfirmRemoveAppAsync(profile);
            button.ContextFlyout = new MenuFlyout { Items = { remove } };
            button.KeyDown += async (_, e) =>
            {
                if (e.Key == VirtualKey.Delete)
                {
                    e.Handled = true;
                    await ConfirmRemoveAppAsync(profile);
                }
            };
        }

        return button;
    }

    private Button AddAppTile()
    {
        var plus = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Ui.Muted,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Ui.Glyph("", 14, Ui.Muted),
        };
        ((FontIcon)plus.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((FontIcon)plus.Child).VerticalAlignment = VerticalAlignment.Center;
        var label = Ui.Text("Add app", 11, Ui.Muted);
        label.HorizontalAlignment = HorizontalAlignment.Center;

        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = 62,
            Padding = new Thickness(0, 6, 0, 4),
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new StackPanel { Spacing = 4, Children = { plus, label } },
        };
        AutomationProperties.SetName(button, "Add app");
        button.Click += (_, _) => ShowAddAppFlyout(button);
        return button;
    }

    /// <summary>Loads the cached icon into the image, extracting it first if needed. The placeholder stays when none exists.</summary>
    private async Task LoadIconAsync(Image image, FrameworkElement holder, string process, string? exePath, double size)
    {
        string? path = await _icons.GetIconPathAsync(process, exePath);
        if (path is null)
        {
            return;
        }

        image.Source = Ui.Bitmap(path, decodePixelWidth: (int)Math.Ceiling(size * _scale));
        if (holder is Grid g && g.Children.Count > 1)
        {
            g.Children[0].Visibility = Visibility.Collapsed; // hide the placeholder tile
        }
    }

    /// <summary>A freestanding app icon (heading, picker rows, compare headers).</summary>
    private FrameworkElement AppIcon(Profile? profile, double size)
    {
        if (profile?.AppProcess is null)
        {
            return Ui.AllAppsGlyph(size);
        }

        var holder = new Grid { Width = size, Height = size };
        holder.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.25),
            Background = Ui.Paper3,
            Child = new FontIcon { Glyph = "", FontSize = size * 0.45, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        holder.Children.Add(image);
        _ = LoadIconAsync(image, holder, profile.AppProcess, profile.ExePath, size);
        return holder;
    }

    private async void ShowAddAppFlyout(Button anchor)
    {
        var list = new StackPanel { Spacing = 2 };
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Left,
            FlyoutPresenterStyle = Ui.Res<Style>("PopFlyoutPresenterStyle"),
        };
        var browse = new Button { Content = "Browse for an .exe…", Style = Ui.Res<Style>("QuietButtonStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        browse.Click += async (_, _) =>
        {
            flyout.Hide();
            await BrowseForExeAsync();
        };
        var root = new StackPanel { Spacing = 8, Width = 280 };
        root.Children.Add(Ui.Text("Running apps", 12, Ui.Muted));
        root.Children.Add(new ScrollViewer { MaxHeight = 320, Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(browse);
        flyout.Content = root;
        list.Children.Add(Ui.Text("Looking for open windows…", 12, Ui.Muted));
        flyout.ShowAt(anchor);

        IReadOnlyList<RunningApp> apps = await Task.Run(() => RunningAppsCatalog.List(_config.Config.Profiles.Select(p => p.AppProcess)));
        list.Children.Clear();
        if (apps.Count == 0)
        {
            list.Children.Add(Ui.Text("No other apps with a window are open.", 12, Ui.Muted));
        }

        foreach (RunningApp app in apps)
        {
            var temp = new Profile(app.Process, new Dictionary<ushort, ActionSpec>()) { ExePath = app.ExePath, DisplayName = app.DisplayName };
            var row = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                Background = Ui.Brush("RailTransparent"),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { AppIcon(temp, 24), Ui.Text(app.DisplayName, 13) },
                },
            };
            row.PointerEntered += (_, _) => row.Background = Ui.Brush("Paper3HoverBrush");
            row.PointerExited += (_, _) => row.Background = Ui.Brush("RailTransparent");
            row.Click += (_, _) =>
            {
                flyout.Hide();
                AddApp(app);
            };
            list.Children.Add(row);
        }
    }

    private async Task BrowseForExeAsync()
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".exe");
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            AddApp(RunningAppsCatalog.FromExe(file.Path));
        }
    }

    private void AddApp(RunningApp app)
    {
        _config.Update(c => ProfileOps.AddApp(c, app.Process, app.DisplayName, app.ExePath));
        SelectApp(app.Process);
    }

    private async Task ConfirmRemoveAppAsync(Profile profile)
    {
        int changed = ProfileOps.ChangedCount(_config.Config, profile.AppProcess, DeviceKind.Mouse)
            + ProfileOps.ChangedCount(_config.Config, profile.AppProcess, DeviceKind.Keyboard);
        string name = ProfileOps.AppName(profile);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"Remove {name}?",
            Content = changed == 0
                ? $"{name} has no changes. It will disappear from the app list."
                : $"Its {changed} change{(changed == 1 ? string.Empty : "s")} will be deleted. Everything else keeps following All apps.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Keep",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string? process = profile.AppProcess;
        if (string.Equals(_app, process, StringComparison.OrdinalIgnoreCase))
        {
            _app = null;
        }

        _config.Update(c => ProfileOps.RemoveApp(c, process!));
        RebuildAll();
    }
}
