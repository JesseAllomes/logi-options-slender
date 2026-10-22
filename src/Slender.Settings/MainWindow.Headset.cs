using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.System;

namespace Slender.Settings;

/// <summary>
/// Yealink WH64 (m54, split in m59): two rail tiles (headset, base), a popover row and two pages. Shown only when a registry-aware
/// tray lists the headset (present, or installed and disconnected). The tiles use the cached Yealink pictures, else a plain glyph.
/// </summary>
public sealed partial class MainWindow
{
    private const string HeadsetName = "Yealink WH64";
    private const string BaseName = "WH64 base";
    private const string HeadsetGlyph = "";

    private bool HeadsetListed() => DevicePresenceView.Listed(_status.Status, DeviceIds.Headset);

    private YealinkView HeadsetPictureView()
    {
        HeadsetState? h = _status.Device(DeviceIds.Headset)?.Headset;
        return YealinkPictures.ChooseHeadset(h?.Model, HeadsetSettingValue(h, "Platform"));
    }

    /// <summary>Copies the pictures once, the first time the rail lists the headset. The rail redraws when they are ready.</summary>
    private void EnsureYealinkPictures()
    {
        YealinkView view = HeadsetPictureView();
        if (!_yealinkViews.ContainsKey(view))
        {
            LoadYealinkView(view);
        }
    }

    private Button HeadsetTile(bool baseTile = false)
    {
        Page page = baseTile ? Page.Base : Page.Headset;
        string name = baseTile ? BaseName : HeadsetName;
        bool selected = _page == page;
        bool offline = DevicePresenceView.Dimmed(_status.Status, DeviceIds.Headset);
        var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement thumb;
        if (_yealinkViews.GetValueOrDefault(baseTile ? YealinkView.Base : HeadsetPictureView()) is { } device)
        {
            thumb = new Image
            {
                Height = 44,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Source = Ui.Bitmap(device.ThumbPath, decodePixelHeight: (int)Math.Ceiling(44 * _scale)),
            };
        }
        else
        {
            thumb = new Grid { Height = 44, Children = { new FontIcon { Glyph = HeadsetGlyph, FontSize = 26, Foreground = Ui.Muted } } };
        }

        thumb.Opacity = offline ? 0.5 : 1;
        content.Children.Add(thumb);
        string caption = DevicePresenceView.BatteryWord(_status.Status, DeviceIds.Headset)
            ?? (baseTile ? "Base" : _status.Device(DeviceIds.Headset)?.BatteryPercent is { } pct ? $"{pct}%" : "On");
        content.Children.Add(Ui.Text(caption, 11.5, Ui.Muted));

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
        AutomationProperties.SetName(button, name + DevicePresenceView.AccessibleSuffix(_status.Status, DeviceIds.Headset));
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => OpenPage(page);
        if (!baseTile && DevicePresenceView.Removable(_status.Status, DeviceIds.Headset))
        {
            var remove = new MenuFlyoutItem { Text = "Remove device", Icon = new FontIcon { Glyph = "" } };
            remove.Click += async (_, _) => await ConfirmRemoveDeviceAsync(DeviceIds.Headset, HeadsetName, "Windows is not changed.");
            button.ContextFlyout = new MenuFlyout { Items = { remove } };
            button.KeyDown += async (_, e) =>
            {
                if (e.Key == VirtualKey.Delete)
                {
                    e.Handled = true;
                    await ConfirmRemoveDeviceAsync(DeviceIds.Headset, HeadsetName, "Windows is not changed.");
                }
            };
        }

        return button;
    }

    private FrameworkElement HeadsetPopRow()
    {
        bool offline = DevicePresenceView.Dimmed(_status.Status, DeviceIds.Headset);
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 12, 8), CornerRadius = new CornerRadius(8), Background = Ui.Brush("Paper2Brush") };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Grid { Height = 36, Opacity = offline ? 0.5 : 1, Children = { new FontIcon { Glyph = HeadsetGlyph, FontSize = 22, Foreground = Ui.Muted } } });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(Ui.Text(HeadsetName, 14, Ui.Ink, Ui.Heading));
        text.Children.Add(Ui.Text(DevicePresenceView.Subtitle(_status.Status, DeviceIds.Headset), 12, Ui.Muted));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        AutomationProperties.SetName(row, HeadsetName + DevicePresenceView.AccessibleSuffix(_status.Status, DeviceIds.Headset));
        return row;
    }

    private void AddHeadsetHeader(string title)
    {
        _dropDownOpen = _sliderHeld = false;
        var heading = Ui.Text(title, 24, Ui.Ink, Ui.Heading);
        heading.Margin = new Thickness(0, 0, 0, 4);
        PagePanel.Children.Add(heading);
        PagePanel.Children.Add(Ui.Text(DevicePresenceView.Subtitle(_status.Status, DeviceIds.Headset), 12, Ui.Muted));
        if (_status.Status is null || !_status.TrayRunning)
        {
            AddHeadsetNote("Slender isn't running. Changes you make here are saved and apply when it starts.");
        }
        else if (DevicePresenceView.Removable(_status.Status, DeviceIds.Headset))
        {
            AddHeadsetNote("The headset isn't connected. Your changes are saved and apply when it connects.");
        }

        if (!_headsetAsked)
        {
            _headsetAsked = true;
            AskTray(DeviceMessages.HeadsetRefreshName);
        }
    }

    /// <summary>The base page: its picture and keys, the switch that lets Slender ask the base anything, then the base's own settings.</summary>
    private void BuildBasePage()
    {
        YealinkSettings y = _config.Config.Yealink;
        HeadsetState? h = _status.Device(DeviceIds.Headset)?.Headset;
        AddHeadsetHeader(BaseName);
        AddHeadsetPicture(h, y, YealinkView.Base);

        AddSection("BASE STATUS");
        AddRow("Read headset status", "Lets Slender talk to the base: read the battery, signal and settings, and change the settings on these pages. Off stops it from sending the base anything.",
            PageToggle("Read headset status", y.VendorReads, on => _config.Update(x => x with { Yealink = x.Yealink with { VendorReads = on } })));

        AddHeadsetSettings(h, y, YealinkTarget.Base);
    }

    /// <summary>The headset page: its picture, what it reports, the bindable buttons, mute sync and lights, then the headset's own settings. Saved whether or not it is plugged in.</summary>
    private void BuildHeadsetPage()
    {
        YealinkSettings y = _config.Config.Yealink;
        HeadsetState? h = _status.Device(DeviceIds.Headset)?.Headset;
        AddHeadsetHeader(HeadsetName);
        AddHeadsetPicture(h, y, HeadsetPictureView());

        var rows = HeadsetView.StatusRows(h);
        if (rows.Count > 0)
        {
            AddSection("HEADSET");
            foreach (var (label, value) in rows) AddRow(label, null, Ui.Text(value, 13, Ui.Ink));
        }

        AddSection("BUTTONS");
        AddHeadsetButtonRow("Hook button", "The answer and end-call button. Runs on both pick up and hang up.", y.Hook, (s, a) => s with { Hook = a });
        AddHeadsetButtonRow("Reject button", null, y.Reject, (s, a) => s with { Reject = a });
        AddHeadsetButtonRow("Teams button", null, y.TeamsButton, (s, a) => s with { TeamsButton = a });

        AddSection("MUTE AND LIGHTS");
        AddRow("Mute syncs Windows microphone", "The mute button mutes the Windows microphone, and the headset's mute light follows it.",
            PageToggle("Mute syncs Windows microphone", y.MuteSync, on => _config.Update(x => x with { Yealink = x.Yealink with { MuteSync = on } })));
        AddRow("Headset lights", "Experimental. Lets Slender switch the headset's mute light. Off until it is tested on your headset; nothing is written to the headset while it is off.",
            PageToggle("Headset lights", y.LedWrites, on => _config.Update(x => x with { Yealink = x.Yealink with { LedWrites = on } })));

        AddHeadsetSettings(h, y, YealinkTarget.Headset);
    }

    private bool _headsetAsked;

    private void AddHeadsetNote(string text)
    {
        var note = Ui.Text(text, 12, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 6, 0, 0);
        PagePanel.Children.Add(note);
    }

    private void AddHeadsetButtonRow(string name, string? help, ActionSpec current, Func<YealinkSettings, ActionSpec, YealinkSettings> set)
    {
        string text = HeadsetActionText(current);
        var button = new Button { Content = text, Style = Ui.Res<Style>("QuietButtonStyle"), MinWidth = 150 };
        AutomationProperties.SetName(button, $"{name}: {text}");
        button.Click += (_, _) => ShowHeadsetPicker(button, name, current, set);
        AddRow(name, help ?? "Runs when you press it. Leave to phone app keeps your phone app in charge.", button);
    }

    private static string HeadsetActionText(ActionSpec action) => action is ActionSpec.None ? "Leave to phone app" : ActionCatalog.Plain(action);

    private void ShowHeadsetPicker(FrameworkElement anchor, string name, ActionSpec current, Func<YealinkSettings, ActionSpec, YealinkSettings> set)
    {
        ActionSpec? own = current is ActionSpec.None ? null : current;
        string initial = own switch
        {
            ActionSpec.MouseButton => "mouse",
            ActionSpec.Excel or ActionSpec.ExcelMacro => "excel",
            ActionSpec.ActionsRing => "ring",
            ActionSpec.Launch or ActionSpec.Disabled or ActionSpec.Brightness or ActionSpec.Text or ActionSpec.Macro => "more",
            ActionSpec.Keys => "keys",
            _ => "default",
        };

        ShowPickerCore(anchor, new PickerSpec(name, MouseTabs, initial, own, "Leave to phone app", false,
            action => _config.Update(c => c with { Yealink = set(c.Yealink, action ?? new ActionSpec.None()) })));
    }
}
