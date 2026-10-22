using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.System;

namespace Slender.Settings;

/// <summary>
/// Yealink WH64 headset (m54): a rail tile, a popover row and one page. Shown only when a registry-aware tray lists the headset
/// (present, or installed and disconnected). No render exists for it, so it uses a plain glyph; nothing is downloaded.
/// </summary>
public sealed partial class MainWindow
{
    private const string HeadsetName = "Yealink WH64";
    private const string HeadsetGlyph = "";

    private bool HeadsetListed() => DevicePresenceView.Listed(_status.Status, DeviceIds.Headset);

    private Button HeadsetTile()
    {
        bool selected = _page == Page.Headset;
        bool offline = DevicePresenceView.Dimmed(_status.Status, DeviceIds.Headset);
        var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var thumb = new Grid { Height = 44, Opacity = offline ? 0.5 : 1, Children = { new FontIcon { Glyph = HeadsetGlyph, FontSize = 26, Foreground = Ui.Muted } } };
        content.Children.Add(thumb);
        content.Children.Add(Ui.Text(DevicePresenceView.BatteryWord(_status.Status, DeviceIds.Headset) ?? (_status.Device(DeviceIds.Headset)?.BatteryPercent is { } pct ? $"{pct}%" : "On"), 11.5, Ui.Muted));

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
        AutomationProperties.SetName(button, HeadsetName + DevicePresenceView.AccessibleSuffix(_status.Status, DeviceIds.Headset));
        ToolTipService.SetToolTip(button, HeadsetName);
        button.Click += (_, _) => OpenPage(Page.Headset);
        if (DevicePresenceView.Removable(_status.Status, DeviceIds.Headset))
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

    /// <summary>The headset page: the three bindable buttons, mute sync and the (experimental) lights. Saved whether or not it is plugged in.</summary>
    private void BuildHeadsetPage()
    {
        YealinkSettings y = _config.Config.Yealink;
        var title = Ui.Text(HeadsetName, 24, Ui.Ink, Ui.Heading);
        title.Margin = new Thickness(0, 0, 0, 4);
        PagePanel.Children.Add(title);
        PagePanel.Children.Add(Ui.Text(DevicePresenceView.Subtitle(_status.Status, DeviceIds.Headset), 12, Ui.Muted));
        if (_status.Status is null || !_status.TrayRunning)
        {
            AddHeadsetNote("Slender isn't running. Changes you make here are saved and apply when it starts.");
        }
        else if (DevicePresenceView.Removable(_status.Status, DeviceIds.Headset))
        {
            AddHeadsetNote("The headset isn't connected. Your changes are saved and apply when it connects.");
        }

        AddHeadsetStatus();

        AddSection("BUTTONS");
        AddHeadsetButtonRow("Hook button", "The answer and end-call button. Runs on both pick up and hang up.", y.Hook, (s, a) => s with { Hook = a });
        AddHeadsetButtonRow("Reject button", null, y.Reject, (s, a) => s with { Reject = a });
        AddHeadsetButtonRow("Teams button", null, y.TeamsButton, (s, a) => s with { TeamsButton = a });

        AddSection("BASE STATUS");
        AddRow("Read headset status", "Lets Slender read the battery, signal and settings from the base. Read only: nothing is changed on the base. Off stops it from sending the base anything.",
            PageToggle("Read headset status", y.VendorReads, on => _config.Update(x => x with { Yealink = x.Yealink with { VendorReads = on } })));

        AddSection("MUTE AND LIGHTS");
        AddRow("Mute syncs Windows microphone", "The mute button mutes the Windows microphone, and the headset's mute light follows it.",
            PageToggle("Mute syncs Windows microphone", y.MuteSync, on => _config.Update(x => x with { Yealink = x.Yealink with { MuteSync = on } })));
        AddRow("Headset lights", "Experimental. Lets Slender switch the headset's mute light. Off until it is tested on your headset; nothing is written to the headset while it is off.",
            PageToggle("Headset lights", y.LedWrites, on => _config.Update(x => x with { Yealink = x.Yealink with { LedWrites = on } })));
    }

    private bool _headsetAsked;

    /// <summary>
    /// What the base says (battery, charging, docked, signal, range) and, read-only, its own settings grouped as USB Connect does.
    /// Opening the page asks the tray for a fresh read, once per visit; the tray limits how often it really reads.
    /// </summary>
    private void AddHeadsetStatus()
    {
        if (!_headsetAsked)
        {
            _headsetAsked = true;
            AskTray(DeviceMessages.HeadsetRefreshName);
        }

        HeadsetState? h = _status.Status?.Devices.FirstOrDefault(d => d.Id == DeviceIds.Headset)?.Headset;
        var rows = HeadsetView.StatusRows(h);
        if (rows.Count > 0)
        {
            AddSection("HEADSET");
            foreach (var (label, value) in rows) AddRow(label, null, Ui.Text(value, 13, Ui.Ink));
        }

        var groups = HeadsetView.Groups(h);
        if (groups.Count == 0) return;
        AddSection("BASE SETTINGS");
        AddHeadsetNote("What the base is set to now. Change these in Yealink USB Connect; Slender only reads them.");
        foreach (var (group, items) in groups)
        {
            AddSection(group.ToUpperInvariant());
            foreach (var item in items) AddRow(item.Label, null, Ui.Text(item.Value, 13, Ui.Ink));
        }
    }

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
