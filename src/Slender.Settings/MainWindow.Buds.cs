using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.System;

namespace Slender.Settings;

/// <summary>
/// Galaxy Buds4 Pro: a rail tile, a popover tile and one page. The page has a drawn hero (both buds and the case with their battery
/// and placement), a quick row (noise control, find my earbuds) and the buds' settings by group. The tray owns the Bluetooth link;
/// this page reads BudsState from status.json and posts BudsRefresh / BudsSet / BudsFind. What the page decides is in BudsView.
/// </summary>
public sealed partial class MainWindow
{
    private const string BudsName = "Galaxy Buds4 Pro";

    private bool _budsAsked;
    private (int Index, int Raw)? _budsPending;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _budsSendTimer, _budsHoldTimer;

    /// <summary>Changes the user just made, by catalog index: shown instead of the buds' report for 2 seconds.</summary>
    private readonly Dictionary<int, (int Raw, DateTimeOffset At)> _budsSent = [];

    private bool BudsListed() => DevicePresenceView.Listed(_status.Status, DeviceIds.Buds);

    private BudsState? CurrentBuds() => _status.Device(DeviceIds.Buds)?.Buds;

    private static int BudsThreshold => BatteryGlyphModel.HeadsetThreshold;

    // ---- rail and popover ----

    private Button BudsTile()
    {
        bool selected = _page == Page.Buds;
        bool offline = DevicePresenceView.Dimmed(_status.Status, DeviceIds.Buds);
        BatteryGlyphModel glyph = BudsView.Glyph(CurrentBuds(), offline, DevicePresenceView.BatteryWord(_status.Status, DeviceIds.Buds), BudsThreshold);
        Canvas art = BudsArt.Pair(0.7);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        art.Opacity = offline ? 0.5 : 1;
        var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(art);
        content.Children.Add(BatteryGlyph.Create(glyph, BatteryGlyphSize.Small, Ui.Muted, compact: true));

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
        AutomationProperties.SetName(button, BudsName + ", " + glyph.AccessibleText.ToLowerInvariant() + DevicePresenceView.AccessibleSuffix(_status.Status, DeviceIds.Buds));
        ToolTipService.SetToolTip(button, BudsName);
        button.Click += (_, _) => OpenPage(Page.Buds);
        if (DevicePresenceView.Removable(_status.Status, DeviceIds.Buds))
        {
            var remove = new MenuFlyoutItem { Text = "Remove device", Icon = new FontIcon { Glyph = "" } };
            remove.Click += async (_, _) => await ConfirmRemoveDeviceAsync(DeviceIds.Buds, BudsName, "Windows Bluetooth pairing is not changed.");
            button.ContextFlyout = new MenuFlyout { Items = { remove } };
            button.KeyDown += async (_, e) =>
            {
                if (e.Key == VirtualKey.Delete)
                {
                    e.Handled = true;
                    await ConfirmRemoveDeviceAsync(DeviceIds.Buds, BudsName, "Windows Bluetooth pairing is not changed.");
                }
            };
        }

        return button;
    }

    /// <summary>The popover's tile for the buds: the drawn pair and the lowest bud's battery, like the other tiles. Battery only; no quick action there.</summary>
    private Button BudsPopTile()
    {
        bool offline = DevicePresenceView.Dimmed(_status.Status, DeviceIds.Buds);
        BatteryGlyphModel glyph = BudsView.Glyph(CurrentBuds(), offline, DevicePresenceView.BatteryWord(_status.Status, DeviceIds.Buds), BudsThreshold);
        string name = BudsName + ", " + glyph.AccessibleText.ToLowerInvariant() + DevicePresenceView.AccessibleSuffix(_status.Status, DeviceIds.Buds);
        Canvas art = BudsArt.Pair(0.9);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        art.Opacity = offline ? 0.5 : 1;
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(art);
        stack.Children.Add(BatteryGlyph.Create(glyph, BatteryGlyphSize.Small, Ui.Muted));
        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Height = PopTileHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = Ui.Brush("Paper2Brush"),
            Content = stack,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += async (_, _) =>
        {
            _page = Page.Buds;
            RebuildAll();
            await UnfoldAsync();
        };
        return button;
    }

    // ---- page ----

    private void BuildBudsPage()
    {
        BudsState? reported = CurrentBuds();
        bool trayUp = _status.Status is not null && _status.TrayRunning;
        bool connected = BudsView.IsConnected(reported) && trayUp;
        BudsState? shown = BudsView.WithHeld(reported, HeldBudsValues());
        _dropDownOpen = _sliderHeld = false;

        var heading = Ui.Text(BudsName, 24, Ui.Ink, Ui.Heading);
        heading.Margin = new Thickness(0, 0, 0, 4);
        PagePanel.Children.Add(heading);
        PagePanel.Children.Add(Ui.Text(DevicePresenceView.Subtitle(_status.Status, DeviceIds.Buds), 12, Ui.Muted));

        AddBudsStage(reported, connected);

        if (!trayUp)
        {
            AddHeadsetNote("Slender isn't running, so the buds can't be changed.");
        }
        else if (!connected)
        {
            AddHeadsetNote(BudsView.NotConnectedLine);
        }

        if (reported?.WriteProblem is { } problem)
        {
            var warn = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Message = problem, Margin = new Thickness(0, 8, 0, 0) };
            PagePanel.Children.Add(warn);
        }

        AddBudsQuickRow(shown, connected, reported?.FindActive == true);

        foreach (BudsGroup group in BudsView.Groups(shown))
        {
            var rows = group.Rows.Where(r => r.Def.Key != GalaxyBudsSettingsCatalog.NoiseMode).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            AddSection(group.Name.ToUpperInvariant());
            foreach (BudsRow row in rows)
            {
                AddRow(row.Def.Label, row.Def.Help, BudsControl(row, connected));
            }
        }

        if (!_budsAsked)
        {
            _budsAsked = true;
            AskTray(DeviceMessages.BudsRefreshName);
        }
    }

    private Dictionary<string, int> HeldBudsValues()
    {
        var held = new Dictionary<string, int>();
        foreach (int index in _budsSent.Keys.ToList())
        {
            (int raw, DateTimeOffset at) = _budsSent[index];
            if (BudsView.HoldSent(at, DateTimeOffset.Now))
            {
                held[GalaxyBudsSettingsCatalog.All[index].Key] = raw;
            }
            else
            {
                _budsSent.Remove(index);
            }
        }

        return held;
    }

    /// <summary>The hero: left bud, right bud and case, each with its level and where it is. Drawn, theme-aware, no picture.</summary>
    private void AddBudsStage(BudsState? b, bool connected)
    {
        var grid = new Grid { ColumnSpacing = 8, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        void Part(int column, string name, FrameworkElement art, int? percent, bool? charging, string placement)
        {
            BatteryGlyphModel model = BatteryGlyphModel.Create(percent, charging == true, null, BudsThreshold, hidden: !connected);
            art.HorizontalAlignment = HorizontalAlignment.Center;
            art.Opacity = connected ? 1 : 0.5;
            var col = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(new Border { Height = BudsArt.BudHeight + 6, Child = art, VerticalAlignment = VerticalAlignment.Bottom });
            col.Children.Add(Ui.Text(name, 12, Ui.Muted));
            col.Children.Add(BatteryGlyph.Create(model, BatteryGlyphSize.Small));
            var where = Ui.Text(connected ? placement : string.Empty, 12, Ui.Muted);
            where.HorizontalAlignment = HorizontalAlignment.Center;
            where.MinHeight = 16;
            col.Children.Add(where);
            AutomationProperties.SetName(col, $"{name}, {model.AccessibleText.ToLowerInvariant()}{(placement.Length > 0 && connected ? ", " + placement.ToLowerInvariant() : string.Empty)}");
            Grid.SetColumn(col, column);
            grid.Children.Add(col);
        }

        Part(0, "Left", BudsArt.Bud(true), b?.BatteryLeft, b?.ChargingLeft, BudsView.PlacementText(b?.PlacementLeft));
        Part(1, "Right", BudsArt.Bud(false), b?.BatteryRight, b?.ChargingRight, BudsView.PlacementText(b?.PlacementRight));
        Part(2, "Case", BudsArt.Case(b?.ChargingCase == true), b?.BatteryCase, b?.ChargingCase, b?.ChargingCase == true ? "Charging" : string.Empty);
        PagePanel.Children.Add(grid);
        if (BudsView.FirmwareLine(b) is { } line)
        {
            var small = Ui.Text(line, 11.5, Ui.Muted);
            small.Margin = new Thickness(0, 8, 0, 0);
            PagePanel.Children.Add(small);
        }
    }

    /// <summary>Noise control as a four-way switch, and Find my earbuds beside it.</summary>
    private void AddBudsQuickRow(BudsState? shown, bool connected, bool finding)
    {
        BudsRow? noise = BudsView.Row(shown, GalaxyBudsSettingsCatalog.NoiseMode);
        int noiseIndex = GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.NoiseMode);
        bool enabled = connected && noise is { Enabled: true };

        var segments = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach ((int raw, string label) in BudsView.NoiseModes)
        {
            bool on = noise?.Raw == raw;
            var segment = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                Height = 34,
                MinWidth = 78,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1.5),
                Background = on ? Ui.MarkSoft : Ui.Paper3,
                BorderBrush = on ? Ui.Mark : Ui.Brush("RailTransparent"),
                Content = Ui.Text(label, 13, on ? Ui.Ink : Ui.Muted),
                IsEnabled = enabled,
            };
            AutomationProperties.SetName(segment, $"Noise control: {label}");
            ToolTipService.SetToolTip(segment, label);
            int value = raw;
            segment.Click += (_, _) =>
            {
                if (noise?.Raw != value)
                {
                    SendBuds(noiseIndex, value, rebuild: true);
                }
            };
            segments.Children.Add(segment);
        }

        var row = new Grid { Margin = new Thickness(0, 18, 0, 0), ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(Ui.Text("NOISE CONTROL", 12, Ui.Muted, Ui.Heading));
        left.Children.Add(new ScrollViewer
        {
            Content = segments,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
        });
        row.Children.Add(left);
        Button find = FindButton(connected, finding);
        find.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(find, 1);
        row.Children.Add(find);
        PagePanel.Children.Add(row);
    }

    private Button FindButton(bool connected, bool finding)
    {
        var button = new Button
        {
            Content = finding ? "Stop" : "Find my earbuds",
            Style = Ui.Res<Style>("QuietButtonStyle"),
            Height = 34,
            IsEnabled = connected || finding,
        };
        AutomationProperties.SetName(button, finding ? "Stop find my earbuds" : "Find my earbuds");
        if (finding)
        {
            button.Click += (_, _) => AskTray(DeviceMessages.BudsFindName, 0);
            return button;
        }

        var start = new Button { Content = "Start", Style = Ui.Res<Style>("PrimaryPillStyle"), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 10, Width = 260 };
        panel.Children.Add(new TextBlock { Text = "The earbuds will beep loudly. Take them out of your ears first.", TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        panel.Children.Add(start);
        var flyout = new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom, FlyoutPresenterStyle = Ui.Res<Style>("PopFlyoutPresenterStyle") };
        start.Click += (_, _) =>
        {
            flyout.Hide();
            AskTray(DeviceMessages.BudsFindName, 1);
        };
        button.Flyout = flyout;
        return button;
    }

    private FrameworkElement BudsControl(BudsRow row, bool connected)
    {
        BudsSettingDef def = row.Def;
        bool enabled = connected && row.Enabled;
        switch (row.Kind)
        {
            case BudsControlKind.Toggle:
            {
                ToggleSwitch toggle = PageToggle(def.Label, row.Raw == 1, on => SendBuds(row.CatalogIndex, on ? 1 : 0, rebuild: BudsView.ControlsOthers(def.Key)));
                toggle.IsEnabled = enabled;
                return toggle;
            }

            case BudsControlKind.Slider:
            {
                Slider slider = PageSlider(def.Label, def.Min, def.Max, 1, Math.Clamp(row.Raw, def.Min, def.Max),
                    v => QueueBudsSet(row.CatalogIndex, (int)v), v => $"{(int)v}", out TextBlock readout);
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

            case BudsControlKind.Choice:
            {
                var choices = BudsView.Choices(def);
                var combo = new ComboBox { Width = 220, IsEnabled = enabled };
                foreach ((_, string label) in choices)
                {
                    combo.Items.Add(label);
                }

                int current = choices.ToList().FindIndex(c => c.Raw == row.Raw);
                combo.SelectedIndex = current;
                if (current < 0)
                {
                    combo.PlaceholderText = row.Text;
                }

                AutomationProperties.SetName(combo, def.Label);
                combo.DropDownOpened += (_, _) => _dropDownOpen = true;
                combo.DropDownClosed += (_, _) =>
                {
                    _dropDownOpen = false;
                    EndSettingEdit();
                };
                combo.SelectionChanged += (_, _) =>
                {
                    int chosen = combo.SelectedIndex;
                    if (chosen < 0 || chosen == current)
                    {
                        return;
                    }

                    current = chosen;
                    SendBuds(row.CatalogIndex, choices[chosen].Raw, rebuild: BudsView.ControlsOthers(def.Key));
                };
                return combo;
            }

            default:
                return Ui.Text(row.Text, 13, Ui.Ink);
        }
    }

    // ---- sending ----

    /// <summary>Posts one change, holds it on screen for 2 seconds, and redraws then (or now, when it adds or removes other rows).</summary>
    private void SendBuds(int index, int raw, bool rebuild)
    {
        _budsSent[index] = (raw, DateTimeOffset.Now);
        AskTray(DeviceMessages.BudsSetName, index, raw);
        StartBudsHold();
        if (rebuild)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_page == Page.Buds)
                {
                    RebuildHeadsetPage();
                }
            });
        }
    }

    /// <summary>One one-shot timer that exists only while a change is held: when it ends the page shows the buds' own answer.</summary>
    private void StartBudsHold()
    {
        if (_budsHoldTimer is null)
        {
            _budsHoldTimer = DispatcherQueue.CreateTimer();
            _budsHoldTimer.Interval = TimeSpan.FromMilliseconds(2100);
            _budsHoldTimer.IsRepeating = false;
            _budsHoldTimer.Tick += (_, _) =>
            {
                if (_page == Page.Buds)
                {
                    RebuildHeadsetPage();
                }
            };
        }

        _budsHoldTimer.Stop();
        _budsHoldTimer.Start();
    }

    /// <summary>A slider's value goes to the tray once it has been still for a moment, so dragging does not flood the buds.</summary>
    private void QueueBudsSet(int index, int raw)
    {
        if (_budsPending is { } other && other.Index != index)
        {
            SendPendingBuds();
        }

        _budsSent[index] = (raw, DateTimeOffset.Now);
        _budsPending = (index, raw);
        if (_budsSendTimer is null)
        {
            _budsSendTimer = DispatcherQueue.CreateTimer();
            _budsSendTimer.Interval = TimeSpan.FromMilliseconds(300);
            _budsSendTimer.IsRepeating = false;
            _budsSendTimer.Tick += (_, _) =>
            {
                SendPendingBuds();
                EndSettingEdit();
            };
        }

        _budsSendTimer.Stop();
        _budsSendTimer.Start();
    }

    private void SendPendingBuds()
    {
        if (_budsPending is { } p)
        {
            _budsPending = null;
            _budsSent[p.Index] = (p.Raw, DateTimeOffset.Now);
            AskTray(DeviceMessages.BudsSetName, p.Index, p.Raw);
            StartBudsHold();
        }
    }

    /// <summary>Leaving the page: send a slider value still waiting, and drop both timers.</summary>
    private void StopBudsTimers()
    {
        _budsSendTimer?.Stop();
        SendPendingBuds();
        _budsHoldTimer?.Stop();
        _budsSent.Clear();
    }
}
