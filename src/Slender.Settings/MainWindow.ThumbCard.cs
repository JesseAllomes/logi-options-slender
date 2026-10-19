using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Slender.Engine;
using Slender.Hidpp;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// The thumb wheel card (locked 2026-10-19, round 3 card 3 variant 2, drawn wheel dropped after the first look): clicking the
/// thumb wheel on the device stage opens it, for the app chosen in the strip. Two modes, Turn and Holding top button, each with
/// its left and right actions, a result preview (tab strip, sliding sheet, or a step count) and a 7-notch speed for that app;
/// then Touch (thumb resting on the wheel) and Tap. While the card is open the tray sends the real thumb wheel and top button here
/// (ThumbPreviewClient) and runs nothing, so turning the wheel previews the result at the chosen speed.
/// </summary>
public sealed partial class MainWindow
{
    private const double ThumbCardWidth = 344;
    private const double SheetColumn = 28;

    private Flyout? _thumbCard;
    private StackPanel? _thumbCardBody;
    private ControlInfo? _thumbCardInfo;
    private ThumbPreviewClient? _preview;
    private bool _previewLive;
    private bool _previewHeld;
    private ThumbModeView? _turnView;
    private ThumbModeView? _holdView;
    private Border? _touchingDot;
    private readonly Dictionary<TouchGesture, Border> _gestureDots = [];
    private bool _previewTouching;

    private void ShowThumbCard(FrameworkElement anchor, ControlInfo info)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = Ui.Res<Style>("PickerFlyoutPresenterStyle"),
        };
        var root = new StackPanel { Spacing = 2, Width = ThumbCardWidth };
        var header = Ui.Text(string.Empty, 12, Ui.Muted);
        root.Children.Add(header);
        var body = new StackPanel();
        root.Children.Add(body);
        flyout.Content = root;

        _thumbCard = flyout;
        _thumbCardBody = body;
        _thumbCardInfo = info;
        StartPreview();
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_thumbCard, flyout))
            {
                _thumbCard = null;
                _thumbCardBody = null;
                _thumbCardInfo = null;
                _turnView = _holdView = null;
                StopPreview();
            }

            if (anchor is Control c && c.IsLoaded)
            {
                c.Focus(FocusState.Programmatic);
            }
        };

        header.Text = "Thumb wheel · " + (_app is null ? "All apps" : AppDisplayName(_app));
        RefreshThumbCard();
        flyout.ShowAt(anchor);
    }

    private void StartPreview()
    {
        StopPreview();
        var client = new ThumbPreviewClient(a => DispatcherQueue.TryEnqueue(() => a()));
        client.ConnectedChanged += live =>
        {
            if (!ReferenceEquals(_preview, client))
            {
                return;
            }

            _previewLive = live;
            _previewHeld &= live;
            RefreshThumbCard();
        };
        client.Rotated += delta =>
        {
            if (ReferenceEquals(_preview, client))
            {
                OnPreviewRotated(delta);
            }
        };
        client.TopButton += down =>
        {
            if (ReferenceEquals(_preview, client))
            {
                _previewHeld = down;
                UpdateLiveWash();
            }
        };
        client.Touch += down =>
        {
            if (ReferenceEquals(_preview, client))
            {
                _previewTouching = down;
                PaintTouching();
            }
        };
        client.Gesture += gesture =>
        {
            if (ReferenceEquals(_preview, client))
            {
                FlashGesture(gesture);
            }
        };
        _preview = client;
        client.Start();
    }

    private void StopPreview()
    {
        _preview?.Dispose();
        _preview = null;
        _previewLive = false;
        _previewHeld = false;
    }

    private string AppDisplayName(string app) => ProfileOps.Find(_config.Config, app) is { } p ? ProfileOps.AppName(p) : app;

    /// <summary>Rebuilds the card's rows from the config; a no-op when the card is closed. Called after every config change.</summary>
    private void RefreshThumbCard()
    {
        if (_thumbCardBody is null || _thumbCardInfo is not { } info)
        {
            return;
        }

        StackPanel body = _thumbCardBody;
        body.Children.Clear();
        string? app = _app;
        string name = app is null ? "All apps" : AppDisplayName(app);
        SlenderConfig config = _config.Config;

        // Turn: what the thumb wheel does normally.
        (ActionSpec? binding, bool _) = CurrentProfile.Effective(_kind, info.Id);
        _turnView = new ThumbModeView(ThumbCardModel.TurnPreview(binding), () => ThumbCardModel.SideSpeed(_config.Config, _app).Value, true);
        body.Children.Add(ModeBlock(_turnView, "Turn", null, ThumbCardModel.SideSpeed(config, app),
            v => _config.Update(x => ThumbCardModel.SetSideSpeed(x, app, v)), $"Side scroll speed in {name}",
            ThumbCardModel.TurnChips(binding), chip => ShowPicker(chip, info), chip => ShowPicker(chip, info)));

        // Holding top button: the second action.
        ThumbWheelView view = ThumbWheelOps.View(config, app);
        ThumbWheelSecond? second = view.On ? view.Second : null;
        var toggle = new ToggleSwitch { IsOn = view.On, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, Width = 44, Margin = new Thickness(6, -6, 0, -6) };
        AutomationProperties.SetName(toggle, "Second action in " + name);
        toggle.Toggled += (_, _) => _config.Update(x => ThumbWheelOps.SetEnabled(x, app, toggle.IsOn));
        _holdView = new ThumbModeView(ThumbCardModel.HoldPreview(second), () => ThumbCardModel.SecondSpeed(_config.Config, _app).Value, second is not null);
        ThumbWheelSecond current = view.Second ?? new ThumbWheelSecond(true, new ActionSpec.None(), new ActionSpec.None());
        body.Children.Add(ModeBlock(_holdView, "Holding top button", toggle, ThumbCardModel.SecondSpeed(config, app),
            v => _config.Update(x => ThumbCardModel.SetSecondSpeed(x, app, v)), $"Second action speed in {name}",
            ThumbCardModel.HoldChips(second),
            chip => ShowSecondPicker(chip, "Scroll left", current.Back, name, a => _config.Update(x => ThumbWheelOps.SetBack(x, app, a))),
            chip => ShowSecondPicker(chip, "Scroll right", current.Forward, name, a => _config.Update(x => ThumbWheelOps.SetForward(x, app, a)))));

        // Touch: the thumb resting on the wheel, a tap and a double tap (they need the wheel's touch sensor). While the card is
        // open the dots are a tester: "touching" follows the thumb, and each row's dot lights when the tray recognises it with
        // the timings below (nothing runs).
        var touch = new StackPanel { Spacing = 2, Padding = new Thickness(6, 8, 6, 8) };
        _gestureDots.Clear();
        var touchHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        touchHead.Children.Add(Ui.Text("Touch", 11.5, Ui.Muted));
        _touchingDot = Dot();
        touchHead.Children.Add(_touchingDot);
        touchHead.Children.Add(Ui.Text(_previewLive ? "touching" : string.Empty, 11, Ui.Muted));
        touch.Children.Add(touchHead);
        foreach (ControlInfo control in ControlCatalog.ThumbTouchControls)
        {
            touch.Children.Add(TouchRow(control, name));
        }

        // Timing is for every app: how long a rest takes, and the longest gap between the two taps of a double tap.
        PointerSettings pointer = config.Pointer;
        touch.Children.Add(TimingRow("Rest time", "Rest time before the action runs", pointer.TouchRestMs, PointerSettings.RestMinMs, PointerSettings.RestMaxMs, 50,
            v => _config.Update(x => x with { Pointer = x.Pointer with { TouchRestMs = v } })));
        touch.Children.Add(TimingRow("Double tap gap", "Longest gap between the two taps", pointer.DoubleTapMs, PointerSettings.DoubleTapMinMs, PointerSettings.DoubleTapMaxMs, 25,
            v => _config.Update(x => x with { Pointer = x.Pointer with { DoubleTapMs = v } })));

        body.Children.Add(touch);

        // Footer: the top button's own job, inheritance, and whether the preview is live.
        (ActionSpec? top, bool _) = CurrentProfile.Effective(_kind, ControlId.ModeShift);
        string topJob = top is null or ActionSpec.None ? "ratchet / free-spin" : ActionCatalog.Plain(top);
        var foot = new StackPanel { Spacing = 6, Padding = new Thickness(0, 8, 0, 0), BorderBrush = Ui.Rule, BorderThickness = new Thickness(0, 1, 0, 0) };
        var quick = Ui.Text("Quick press of the top button: " + topJob, 11.5, Ui.Muted);
        quick.TextWrapping = TextWrapping.Wrap;
        foot.Children.Add(quick);
        if (ThumbWheelOps.InheritedText(view) is { } inherited)
        {
            foot.Children.Add(FootAction(inherited, "Customise", "Customise the second action in " + name, () => _config.Update(x => ThumbWheelOps.Customise(x, app))));
        }
        else if (app is not null && view.Source == ThumbWheelSource.Own)
        {
            foot.Children.Add(FootAction("Own settings for " + name, "Use All apps", "Use All apps' second action in " + name, () => _config.Update(x => ThumbWheelOps.FollowInherited(x, app))));
        }

        var hint = Ui.Text(_previewLive ? "Turn the thumb wheel to preview · nothing runs while this is open" : "Slender isn't running, so there's no live preview", 11.5, Ui.Muted);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;
        foot.Children.Add(hint);
        body.Children.Add(foot);
        UpdateLiveWash();
    }

    /// <summary>One mode: header (name, optional toggle, speed notches), the wheel between its chips, then the result preview.</summary>
    private FrameworkElement ModeBlock(ThumbModeView mode, string title, ToggleSwitch? toggle, ThumbSpeedView speed, Action<int> setSpeed,
        string speedName, ThumbChips chips, Action<FrameworkElement> pickLeft, Action<FrameworkElement> pickRight)
    {
        var block = new StackPanel { Spacing = 4, Padding = new Thickness(6, 8, 6, 8), CornerRadius = new CornerRadius(8) };
        mode.Block = block;

        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleText = Ui.Text(title, 11.5, Ui.Muted);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        label.Children.Add(titleText);
        if (toggle is not null)
        {
            label.Children.Add(toggle);
        }

        head.Children.Add(label);
        block.Children.Add(head);

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FrameworkElement left = ChipSide(mode, 0, chips.Left, chips.LeftCaption, chips.Mono, title, HorizontalAlignment.Left, pickLeft);
        FrameworkElement right = ChipSide(mode, 1, chips.Right, chips.RightCaption, chips.Mono, title, HorizontalAlignment.Right, pickRight);
        var turn = Ui.Text("◀ turn ▶", 11, Ui.Muted);
        turn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(turn, 1);
        Grid.SetColumn(right, 2);
        row.Children.Add(left);
        row.Children.Add(turn);
        row.Children.Add(right);
        block.Children.Add(row);

        block.Children.Add(PreviewStrip(mode));
        block.Children.Add(SpeedRow(speed, speedName, setSpeed));
        if (!mode.Enabled)
        {
            row.Opacity = 0.5;
        }

        return block;
    }

    private static FrameworkElement ChipSide(ThumbModeView mode, int side, string text, string caption, bool mono, string modeTitle,
        HorizontalAlignment align, Action<FrameworkElement> pick)
    {
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        var chipText = Ui.Text(text, mono ? 11 : 12, null, mono ? Ui.Mono : null);
        chipText.TextTrimming = TextTrimming.CharacterEllipsis;
        var chip = new Button
        {
            Style = Ui.Res<Style>("QuietButtonStyle"),
            Content = chipText,
            MaxWidth = 128,
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = align,
        };
        AutomationProperties.SetName(chip, $"{modeTitle}, {(side == 0 ? "left" : "right")}: {text}");
        chip.Click += (_, _) => pick(chip);
        var badgeText = Ui.Text(string.Empty, 10.5, Ui.Brush("PaperBrush"), Ui.Mono);
        var badge = new Border
        {
            Background = Ui.Mark,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 0, 5, 0),
            Child = badgeText,
            Opacity = 0,
            HorizontalAlignment = align == HorizontalAlignment.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -9, 0, 0),
            IsHitTestVisible = false,
        };
        var holder = new Grid { HorizontalAlignment = align };
        holder.Children.Add(chip);
        holder.Children.Add(badge);
        stack.Children.Add(holder);
        if (caption.Length > 0)
        {
            var cap = Ui.Text(caption, 11, Ui.Muted);
            cap.HorizontalAlignment = align;
            cap.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(cap);
        }

        mode.Chips[side] = chip;
        mode.Badges[side] = (badge, badgeText);
        return stack;
    }

    /// <summary>The result preview under a mode's actions: tab strip, sliding sheet, or nothing (the chips count instead).</summary>
    private static FrameworkElement PreviewStrip(ThumbModeView mode)
    {
        switch (mode.Kind)
        {
            case ThumbPreviewKind.Tabs:
                var tabs = new Grid { ColumnSpacing = 3, Height = 22 };
                for (int i = 0; i < 6; i++)
                {
                    tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var tab = new Border
                    {
                        CornerRadius = new CornerRadius(5, 5, 0, 0),
                        BorderThickness = new Thickness(1, 1, 1, 0),
                        Child = Ui.Text("Tab " + (i + 1), 10.5, Ui.Muted),
                        Padding = new Thickness(5, 2, 5, 0),
                    };
                    Grid.SetColumn(tab, i);
                    tabs.Children.Add(tab);
                    mode.Tabs.Add(tab);
                }

                mode.TabIndex = 2;
                mode.PaintTabs();
                return tabs;
            case ThumbPreviewKind.Sheet:
                const double sheetH = 22;
                var sheet = new Canvas { Width = 26 * SheetColumn, Height = sheetH };
                for (int c = 0; c < 26; c++)
                {
                    var line = new Rectangle { Width = 1, Height = sheetH, Fill = Ui.Rule };
                    Canvas.SetLeft(line, c * SheetColumn);
                    sheet.Children.Add(line);
                    var letter = Ui.Text(((char)('A' + c)).ToString(), 10, Ui.Muted, Ui.Mono);
                    Canvas.SetLeft(letter, c * SheetColumn + 10);
                    Canvas.SetTop(letter, 3);
                    sheet.Children.Add(letter);
                }

                var slide = new TranslateTransform();
                sheet.RenderTransform = slide;
                mode.Sheet = slide;
                var frame = new Border
                {
                    Height = sheetH,
                    CornerRadius = new CornerRadius(5),
                    BorderBrush = Ui.Rule,
                    BorderThickness = new Thickness(1),
                    Background = Ui.Brush("PaperBrush"),
                    Child = sheet,
                };
                frame.SizeChanged += (_, e) => frame.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
                return frame;
            default:
                return new Border { Height = 0 };
        }
    }

    /// <summary>"Speed", seven notches like Haptic feel, and a readout that says "· All apps" while the value is inherited.</summary>
    private static FrameworkElement SpeedRow(ThumbSpeedView speed, string name, Action<int> set)
    {
        var line = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Ui.Text("Speed", 11.5, Ui.Muted);
        label.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        int selected = ThumbCardModel.NotchIndex(speed.Value);
        for (int i = 0; i < ThumbCardModel.Notches.Count; i++)
        {
            int value = ThumbCardModel.Notches[i];
            var bar = new Rectangle
            {
                Width = 9,
                Height = 6 + i * 2,
                RadiusX = 2,
                RadiusY = 2,
                Fill = i <= selected ? Ui.Mark : Ui.Brush("Paper3ActiveBrush"),
                Opacity = speed.Inherited && i <= selected ? 0.6 : 1,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            var button = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                Width = 12,
                Height = 24,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Bottom,
                Content = bar,
            };
            AutomationProperties.SetName(button, $"{name}: {value}%");
            ToolTipService.SetToolTip(button, value + "%");
            button.Click += (_, _) => set(value);
            row.Children.Add(button);
        }

        Grid.SetColumn(row, 1);
        line.Children.Add(row);
        var readout = Ui.Text(speed.Value + "%" + (speed.Inherited ? " · All apps" : string.Empty), 11.5, Ui.Muted);
        readout.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(readout, 2);
        line.Children.Add(readout);
        return line;
    }

    private static Border Dot() => new()
    {
        Width = 9,
        Height = 9,
        CornerRadius = new CornerRadius(4.5),
        Background = Ui.Brush("Paper3ActiveBrush"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void PaintTouching()
    {
        if (_touchingDot is not null)
        {
            _touchingDot.Background = _previewTouching ? Ui.Mark : Ui.Brush("Paper3ActiveBrush");
        }
    }

    /// <summary>Lights the gesture's dot for 0.8 s.</summary>
    private void FlashGesture(TouchGesture gesture)
    {
        if (!_gestureDots.TryGetValue(gesture, out Border? dot))
        {
            return;
        }

        dot.Background = Ui.Mark;
        DispatcherQueueTimer t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(800);
        t.IsRepeating = false;
        t.Tick += (s, _) =>
        {
            dot.Background = Ui.Brush("Paper3ActiveBrush");
            s.Stop();
        };
        t.Start();
    }

    /// <summary>A timing row in the Touch section: label, slider in milliseconds, readout. Saves when the slider is let go.</summary>
    private FrameworkElement TimingRow(string title, string automationName, int value, int min, int max, int step, Action<int> set)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        var label = Ui.Text(title, 12, Ui.Muted);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var readout = Ui.Text(value + " ms", 11.5, Ui.Muted);
        readout.VerticalAlignment = VerticalAlignment.Center;
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = step, SmallChange = step, Value = value, IsThumbToolTipEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, automationName);
        slider.ValueChanged += (_, e) => readout.Text = (int)e.NewValue + " ms";
        // Saving on every tick would rebuild the card under the pointer; save once the drag or key press is done.
        slider.PointerCaptureLost += (_, _) => { if ((int)slider.Value != value) set((int)slider.Value); };
        slider.KeyUp += (_, _) => { if ((int)slider.Value != value) set((int)slider.Value); };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    /// <summary>A Touch row: what the control does in this app, opening the normal picker.</summary>
    private FrameworkElement TouchRow(ControlInfo control, string appName)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string what = control.Id switch
        {
            DeviceIds.ThumbWheelTouchPseudoId => "Rest thumb on the wheel",
            DeviceIds.ThumbWheelDoubleTapPseudoId => "Double tap the wheel",
            _ => "Tap the wheel",
        };
        var label = Ui.Text(what, 12.5);
        label.VerticalAlignment = VerticalAlignment.Center;
        Border dot = Dot();
        _gestureDots[control.Id switch
        {
            DeviceIds.ThumbWheelTouchPseudoId => TouchGesture.Rest,
            DeviceIds.ThumbWheelDoubleTapPseudoId => TouchGesture.DoubleTap,
            _ => TouchGesture.Tap,
        }] = dot;
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        labelRow.Children.Add(dot);
        labelRow.Children.Add(label);
        grid.Children.Add(labelRow);
        var chipText = Ui.Text(PlainActionText(control), 12);
        chipText.TextTrimming = TextTrimming.CharacterEllipsis;
        var chip = new Button { Style = Ui.Res<Style>("QuietButtonStyle"), Content = chipText, MaxWidth = 170, Padding = new Thickness(8, 3, 8, 3) };
        AutomationProperties.SetName(chip, $"{what} in {appName}: {PlainActionText(control)}");
        chip.Click += (_, _) => ShowPicker(chip, control);
        Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);
        return grid;
    }

    private static FrameworkElement FootAction(string text, string action, string automationName, Action run)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Ui.Text(text, 11.5, Ui.Muted);
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        var button = new Button { Content = action, Style = Ui.Res<Style>("QuietButtonStyle") };
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => run();
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>The action picker for one direction of the second action.</summary>
    private void ShowSecondPicker(FrameworkElement anchor, string what, ActionSpec action, string appName, Action<ActionSpec> set)
    {
        ActionSpec? own = action is ActionSpec.None ? null : action;
        string initial = own switch
        {
            ActionSpec.MouseButton => "mouse",
            ActionSpec.Excel => "excel",
            ActionSpec.ActionsRing => "ring",
            ActionSpec.Launch or ActionSpec.Disabled or ActionSpec.Brightness or ActionSpec.Text => "more",
            ActionSpec.Keys => "keys",
            _ => "default",
        };
        ShowPickerCore(anchor, new PickerSpec($"{what} in {appName} (holding top button)", MouseTabs, initial, own, "Do nothing", false,
            picked => set(picked ?? new ActionSpec.None())));
    }

    /// <summary>The faint copper wash marks which mode the real wheel is previewing.</summary>
    private void UpdateLiveWash()
    {
        Brush wash = Ui.MarkSoft;
        Brush none = Ui.Brush("RailTransparent");
        if (_turnView?.Block is { } turn)
        {
            turn.Background = _previewLive && !_previewHeld ? wash : none;
        }

        if (_holdView?.Block is { } hold)
        {
            hold.Background = _previewLive && _previewHeld ? wash : none;
        }
    }

    /// <summary>A real thumb wheel turn while the card is open: step the preview at the mode's speed.</summary>
    private void OnPreviewRotated(int delta)
    {
        ThumbModeView? mode = _previewHeld ? _holdView : _turnView;
        if (mode is null)
        {
            return;
        }

        if (!mode.Enabled)
        {
            return;
        }

        int steps = mode.Accumulator.AddScaled(delta, mode.Speed(), Environment.TickCount64);
        if (steps != 0)
        {
            mode.Step(steps, DispatcherQueue);
        }
    }

    /// <summary>Live state and visuals of one mode while the card is open.</summary>
    private sealed class ThumbModeView(ThumbPreviewKind kind, Func<int> speed, bool enabled)
    {
        public ThumbPreviewKind Kind { get; } = kind;
        public Func<int> Speed { get; } = speed;
        public bool Enabled { get; } = enabled;
        public ColumnScrollAccumulator Accumulator { get; } = new();
        public StackPanel? Block { get; set; }
        public TranslateTransform? Sheet { get; set; }
        public List<Border> Tabs { get; } = [];
        public int TabIndex { get; set; }
        public Button?[] Chips { get; } = new Button?[2];
        public (Border Badge, TextBlock Text)?[] Badges { get; } = new (Border, TextBlock)?[2];

        private int _count;
        private int _countSide = -1;
        private DispatcherQueueTimer? _fade;

        public void Step(int steps, DispatcherQueue queue)
        {
            int side = steps > 0 ? 1 : 0;
            Flash(side, queue);
            switch (Kind)
            {
                case ThumbPreviewKind.Tabs when Tabs.Count > 0:
                    TabIndex = (((TabIndex + steps) % Tabs.Count) + Tabs.Count) % Tabs.Count;
                    PaintTabs();
                    break;
                case ThumbPreviewKind.Sheet when Sheet is not null:
                    Sheet.X = Math.Clamp(Sheet.X - steps * SheetColumn, -(26 * SheetColumn - ThumbCardWidth), 0);
                    break;
                default:
                    if (_countSide != side)
                    {
                        _count = 0;
                    }

                    _countSide = side;
                    _count += Math.Abs(steps);
                    if (Badges[side] is { } b)
                    {
                        b.Text.Text = "×" + _count;
                        b.Badge.Opacity = 1;
                    }

                    if (_fade is null)
                    {
                        _fade = queue.CreateTimer();
                        _fade.Interval = TimeSpan.FromSeconds(1);
                        _fade.IsRepeating = false;
                        _fade.Tick += (_, _) => ClearCount();
                    }

                    _fade.Stop();
                    _fade.Start();
                    break;
            }
        }

        private void ClearCount()
        {
            _count = 0;
            _countSide = -1;
            foreach (var b in Badges)
            {
                if (b is { } x)
                {
                    x.Badge.Opacity = 0;
                }
            }
        }

        private void Flash(int side, DispatcherQueue queue)
        {
            if (Chips[side] is not { } chip)
            {
                return;
            }

            chip.Background = Ui.Mark;
            DispatcherQueueTimer t = queue.CreateTimer();
            t.Interval = TimeSpan.FromMilliseconds(90);
            t.IsRepeating = false;
            t.Tick += (s, _) =>
            {
                chip.ClearValue(Control.BackgroundProperty);
                s.Stop();
            };
            t.Start();
        }

        public void PaintTabs()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                bool on = i == TabIndex;
                Tabs[i].Background = on ? Ui.Paper3 : Ui.Brush("PaperBrush");
                Tabs[i].BorderBrush = on ? Ui.Mark : Ui.Rule;
                if (Tabs[i].Child is TextBlock text)
                {
                    text.Foreground = on ? Ui.Ink : Ui.Muted;
                }
            }
        }
    }
}
