using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Hidpp;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Slender.Sync;

namespace Slender.Settings;

/// <summary>ERGO K860 extras: keys switched off on the keyboard, the Caps/Num Lock label, Fn lock per Easy-Switch channel, spare keys.</summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _identifyTimer;
    private DateTimeOffset _identifyOpened;
    private ushort _identified;

    /// <summary>Rows added under KEYBOARD on the General page.</summary>
    private void BuildKeyboardExtras(SlenderConfig c)
    {
        foreach ((DisabledKeys key, string name) in KeyboardOps.DisableChoices)
        {
            DisabledKeys captured = key;
            AddRow($"Turn off {name}", captured == DisabledKeys.CapsLock ? "Switched off on the keyboard itself, so it also works when Slender is closed." : null,
                PageToggle($"Turn off {name}", KeyboardOps.IsDisabled(c.Keyboard, captured),
                    on => _config.Update(x => x with { Keyboard = KeyboardOps.SetDisabled(x.Keyboard, captured, on) })));
        }

        AddRow("Show Caps Lock and Num Lock changes", "A short label near the pointer when either one flips, while the K860 is connected.",
            PageToggle("Show Caps Lock and Num Lock changes", c.Keyboard.ShowLockKeyChanges,
                on => _config.Update(x => x with { Keyboard = x.Keyboard with { ShowLockKeyChanges = on } })));

        int? current = _status.Device(DeviceIds.Keyboard)?.Channel;
        _channelChoices = new ComboBox[LogiKeyboard.ChannelCount];
        for (int i = 0; i < LogiKeyboard.ChannelCount; i++)
        {
            int channel = i;
            var choice = new ComboBox { MinWidth = 170 };
            _channelChoices[i] = choice;
            foreach (string text in KeyboardOps.ChannelChoices)
            {
                choice.Items.Add(text);
            }

            choice.SelectedIndex = KeyboardOps.ChoiceIndex(c.Keyboard.FnLockChannels[channel]);
            AutomationProperties.SetName(choice, $"Fn lock on Easy-Switch channel {channel + 1}");
            choice.SelectionChanged += (_, _) =>
            {
                if (_syncingFn)
                {
                    return;
                }

                bool? value = KeyboardOps.FromChoiceIndex(choice.SelectedIndex);
                _config.Update(x => x with { Keyboard = x.Keyboard.WithChannel(channel, value) });
            };
            AddRow(KeyboardOps.ChannelTitle(channel, current), channel == 0 ? "Fn lock for each Easy-Switch channel. Written to the keyboard when you change it; Fn+Esc updates the channel you are on." : null, choice);
        }
    }

    /// <summary>Puts the keyboard's current Fn lock (saved by the tray after Fn+Esc) into the General page's controls without saving.</summary>
    private void RefreshFnControls()
    {
        KeyboardSettings k = _config.Config.Keyboard;
        _syncingFn = true;
        try
        {
            if (_pageFnToggle is not null && _pageFnToggle.IsOn != k.FnLock)
            {
                _pageFnToggle.IsOn = k.FnLock;
            }

            for (int i = 0; _channelChoices is not null && i < _channelChoices.Length; i++)
            {
                int index = KeyboardOps.ChoiceIndex(k.FnLockChannels[i]);
                if (_channelChoices[i].SelectedIndex != index)
                {
                    _channelChoices[i].SelectedIndex = index;
                }
            }
        }
        finally
        {
            _syncingFn = false;
        }
    }

    /// <summary>The spare keys under the keyboard picture: bind them like the others, plus an Identify helper.</summary>
    private void AddSpareKeysRow()
    {
        if (_kbList is null)
        {
            return;
        }

        string appName = _app is null ? "All apps" : ProfileOps.AppName(ProfileOps.Find(_config.Config, _app)!);
        var heading = Ui.Text(_app is null ? "Spare keys" : $"Spare keys in {appName}", 12, Ui.Muted, Ui.Heading);
        heading.Margin = new Thickness(0, 10, 0, 0);
        _kbList.Children.Add(heading);

        bool identifying = _config.Config.Keyboard.IdentifySpareKeys;
        // The cards share the width (long names end in "..."), so Identify always fits beside them however narrow the window is.
        var row = new Grid { ColumnSpacing = 8 };
        foreach (ControlInfo info in ControlCatalog.SpareKeyControls)
        {
            bool hit = identifying && _identified == info.Id;
            var panel = new StackPanel { Spacing = 1 };
            TextBlock name = Ui.Text(info.Name, 11.5, Ui.Muted);
            TextBlock action = Ui.Text(hit ? "You pressed this one" : PlainActionText(info), 13);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            action.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(name);
            panel.Children.Add(action);
            var button = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 4, 9, 4),
                Height = LabelHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = hit ? Ui.MarkSoft : Ui.Brush("RailTransparent"),
                BorderBrush = hit ? Ui.Mark : Ui.Brush("RuleBrush"),
                Content = panel,
            };
            AutomationProperties.SetName(button, info.Name + ": " + PlainActionText(info));
            ControlInfo captured = info;
            button.Click += (_, _) => ShowPicker(button, captured);
            ToolTipService.SetToolTip(button, info.Name + ": " + PlainActionText(info));
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, row.Children.Count);
            row.Children.Add(button);
        }

        var identify = new Button
        {
            Content = identifying ? "Stop identifying" : "Identify",
            Style = Ui.Res<Style>("QuietButtonStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(identify, "Press a spare key on the keyboard to see which one it is. Needs the tray running.");
        identify.Click += (_, _) => ToggleIdentify();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(identify, row.Children.Count);
        row.Children.Add(identify);
        _kbList.Children.Add(row);
    }

    private void ToggleIdentify()
    {
        if (_config.Config.Keyboard.IdentifySpareKeys)
        {
            StopIdentify();
        }
        else
        {
            _identified = 0;
            _identifyOpened = DateTimeOffset.UtcNow;
            _config.Update(c => c with { Keyboard = c.Keyboard with { IdentifySpareKeys = true } });
            _identifyTimer ??= DispatcherQueue.CreateTimer();
            _identifyTimer.Interval = TimeSpan.FromMilliseconds(250);
            _identifyTimer.IsRepeating = true;
            _identifyTimer.Tick -= OnIdentifyTick;
            _identifyTimer.Tick += OnIdentifyTick;
            _identifyTimer.Start();
        }

        RefreshKeyboardList();
    }

    /// <summary>Turns Identify off (the tray then returns the spare keys to native). Safe to call when it is not running.</summary>
    private void StopIdentify()
    {
        _identifyTimer?.Stop();
        if (_config.Config.Keyboard.IdentifySpareKeys)
        {
            _config.Update(c => c with { Keyboard = c.Keyboard with { IdentifySpareKeys = false } });
        }
    }

    private void OnIdentifyTick(DispatcherQueueTimer sender, object args)
    {
        if (SpareKeyReport.Read(SyncPaths.StateDirectory) is { } hit && hit.At > _identifyOpened && hit.Cid != _identified)
        {
            _identified = hit.Cid;
            RefreshKeyboardList();
        }
    }
}
