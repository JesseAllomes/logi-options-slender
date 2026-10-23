using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// The WH64 base's own settings on the Headset and Base pages (m59): a list, switch or slider for what the catalog lets Slender set,
/// plain text for the rest. A change is posted to the tray, which validates it, writes it, reads it back and republishes the status.
/// </summary>
public sealed partial class MainWindow
{
    private bool _dropDownOpen, _sliderHeld, _settingRebuildPending;
    private (int Index, long Value)? _settingPending;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _settingTimer, _previewRingTimer;
    private readonly Dictionary<int, (string Raw, DateTimeOffset At)> _sentSettings = [];

    private bool SettingBusy => _dropDownOpen || _sliderHeld || _settingTimer?.IsRunning == true;

    private void AddHeadsetSettings(HeadsetState? h, YealinkSettings y, YealinkTarget target)
    {
        var groups = HeadsetView.Groups(h, target);
        string? why = !y.VendorReads ? "Turn on Talk to the base on the base page to see and change these."
            : _status.Status is null || !_status.TrayRunning ? "Slender isn't running, so these can't be changed."
            : DevicePresenceView.Dimmed(_status.Status, DeviceIds.Headset) || _status.Device(DeviceIds.Headset)?.Presence == DevicePresenceView.Sleeping ? "The headset isn't connected, so these can't be changed."
            : null;
        if (groups.Count == 0 && why is null)
        {
            return;
        }

        AddSection("SETTINGS");
        if (why is not null)
        {
            AddHeadsetNote(why);
        }

        if (h?.WriteProblem is { } problem)
        {
            var warn = Ui.Text(problem, 12, Ui.Warn);
            warn.TextWrapping = TextWrapping.Wrap;
            warn.Margin = new Thickness(0, 6, 0, 0);
            PagePanel.Children.Add(warn);
        }

        foreach (var (group, items) in groups)
        {
            AddSection(group.ToUpperInvariant());
            foreach (HeadsetSettingItem reported in items)
            {
                HeadsetSettingItem item = Shown(reported, h);
                string? caution = item.Def.Caution switch
                {
                    YealinkCaution.Restarts => "The base restarts about 20 seconds after this change.",
                    YealinkCaution.ModeSwitch => "Switches the base between Teams and UC behaviour.",
                    YealinkCaution.HearingProtection => "Hearing protection. Slender asks before changing it.",
                    _ => null,
                };
                string? help = item.Def.Help is { } h1 && caution is not null ? h1 + " " + caution : item.Def.Help ?? caution;
                AddRow(item.Def.Label, help, item.Adjustable ? HeadsetSettingControl(item, why is null) : Ui.Text(item.Text, 13, Ui.Ink));
            }

            if (group == YealinkSettingsCatalog.BaseRinger && target == YealinkTarget.Base)
            {
                AddRow("Preview ring", "Rings the base for a moment at the PC calls ring volume, so you can hear it. The ring sound setting doesn't change this ring.",
                    PreviewRingButton(why is null));
            }
        }
    }

    private Button PreviewRingButton(bool enabled)
    {
        var play = new Button { Content = "Play", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = enabled && _previewRingTimer?.IsRunning != true };
        AutomationProperties.SetName(play, "Preview ring");
        play.Click += (_, _) =>
        {
            _settingTimer?.Stop();
            SendPendingSetting();
            AskTray(DeviceMessages.HeadsetPreviewRingName);
            play.IsEnabled = false;
            // A one-shot timer that exists only for these two seconds.
            var timer = DispatcherQueue.CreateTimer();
            _previewRingTimer = timer;
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;
            timer.Tick += (s, _) =>
            {
                s.Stop();
                _previewRingTimer = null;
                play.IsEnabled = enabled;
                if (_page is Page.Headset or Page.Base)
                {
                    RebuildHeadsetPage(); // a redraw during the 2 s made a new, still disabled button
                }
            };
            timer.Start();
        };
        return play;
    }

    private void RememberSent(int index, long value)
    {
        if (YealinkSettingsCatalog.All[index].RawFor(value) is { } raw) _sentSettings[index] = (raw, DateTimeOffset.Now);
    }

    /// <summary>Shows a value just sent until the base confirms it, refuses it (a newer write problem) or 6 s pass.</summary>
    private HeadsetSettingItem Shown(HeadsetSettingItem item, HeadsetState? h)
    {
        if (!_sentSettings.TryGetValue(item.CatalogIndex, out var sent)) return item;
        bool settled = item.Raw == sent.Raw
            || (h?.WriteProblem is not null && _status.Status?.UpdatedAt > sent.At)
            || DateTimeOffset.Now - sent.At > TimeSpan.FromSeconds(6);
        if (settled)
        {
            _sentSettings.Remove(item.CatalogIndex);
            return item;
        }
        return item with { Raw = sent.Raw, Text = item.Def.Text(sent.Raw) };
    }

    private FrameworkElement HeadsetSettingControl(HeadsetSettingItem item, bool enabled)
    {
        YealinkSettingDef def = item.Def;
        if (def.IsRange)
        {
            if (!int.TryParse(item.Raw, out int now))
            {
                return Ui.Text(item.Text, 13, Ui.Ink);
            }

            Slider slider = PageSlider(def.Label, def.Min, def.Max, 1, Math.Clamp(now, def.Min, def.Max),
                v => QueueHeadsetSet(item.CatalogIndex, (long)v), v => $"{(int)v}", out TextBlock readout);
            slider.IsEnabled = enabled;
            slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _sliderHeld = true), true);
            var release = new PointerEventHandler((_, _) =>
            {
                _sliderHeld = false;
                EndSettingEdit();
            });
            slider.AddHandler(UIElement.PointerReleasedEvent, release, true);
            slider.AddHandler(UIElement.PointerCaptureLostEvent, release, true);
            return WithReadout(slider, readout);
        }

        if (HeadsetView.IsOnOff(def))
        {
            ToggleSwitch toggle = PageToggle(def.Label, item.Raw == "1", on =>
            {
                RememberSent(item.CatalogIndex, on ? 1 : 0);
                AskTray(DeviceMessages.HeadsetSetName, item.CatalogIndex, on ? 1 : 0);
            });
            toggle.IsEnabled = enabled;
            return toggle;
        }

        var combo = new ComboBox { Width = 200, IsEnabled = enabled };
        for (int i = 0; i < def.ChoiceCount; i++)
        {
            combo.Items.Add(def.ChoiceLabel(i));
        }

        int current = def.ChoiceIndex(item.Raw);
        combo.SelectedIndex = current;
        if (current < 0)
        {
            combo.PlaceholderText = item.Text;
        }

        AutomationProperties.SetName(combo, def.Label);
        bool reverting = false;
        combo.DropDownOpened += (_, _) => _dropDownOpen = true;
        combo.DropDownClosed += (_, _) =>
        {
            _dropDownOpen = false;
            EndSettingEdit();
        };
        combo.SelectionChanged += async (_, _) =>
        {
            int chosen = combo.SelectedIndex;
            if (reverting || chosen < 0 || chosen == current)
            {
                return;
            }

            if (def.Caution != YealinkCaution.None && !await ConfirmHeadsetChangeAsync(def, def.ChoiceLabel(chosen)))
            {
                reverting = true;
                combo.SelectedIndex = current;
                reverting = false;
                return;
            }

            current = chosen;
            RememberSent(item.CatalogIndex, chosen);
            AskTray(DeviceMessages.HeadsetSetName, item.CatalogIndex, chosen);
        };
        return combo;
    }

    private async Task<bool> ConfirmHeadsetChangeAsync(YealinkSettingDef def, string to)
    {
        var dialog = new ContentDialog
        {
            Title = $"Change {def.Label.ToLowerInvariant()}",
            Content = new TextBlock
            {
                Text = def.Caution switch
                {
                    YealinkCaution.Restarts => $"The base restarts about 20 seconds after this change. Set it to {to}?",
                    YealinkCaution.HearingProtection => $"This changes the headset's hearing protection. Set it to {to}?",
                    _ => $"This switches the base between Teams and UC behaviour. Set it to {to}?",
                },
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Change",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>A status change redraws the headset and base pages, unless a setting control is in use: that would pull it out from under the user.</summary>
    private void RebuildHeadsetPage()
    {
        if (SettingBusy)
        {
            _settingRebuildPending = true;
            return;
        }

        _settingRebuildPending = false;
        BuildPage();
    }

    private void EndSettingEdit()
    {
        if (_settingRebuildPending && !SettingBusy && _page is Page.Headset or Page.Base)
        {
            RebuildHeadsetPage();
        }
    }

    /// <summary>Sends a slider's value once it has been still for a moment, so dragging does not flood the tray.</summary>
    private void QueueHeadsetSet(int index, long value)
    {
        if (_settingPending is { } other && other.Index != index)
        {
            SendPendingSetting();
        }

        _settingPending = (index, value);
        if (_settingTimer is null)
        {
            _settingTimer = DispatcherQueue.CreateTimer();
            _settingTimer.Interval = TimeSpan.FromMilliseconds(300);
            _settingTimer.IsRepeating = false;
            _settingTimer.Tick += (_, _) =>
            {
                SendPendingSetting();
                EndSettingEdit();
            };
        }

        _settingTimer.Stop();
        _settingTimer.Start();
    }

    private void SendPendingSetting()
    {
        if (_settingPending is { } p)
        {
            _settingPending = null;
            RememberSent(p.Index, p.Value);
            AskTray(DeviceMessages.HeadsetSetName, p.Index, (nint)p.Value);
        }
    }
}
