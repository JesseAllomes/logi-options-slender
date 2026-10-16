using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.System;
using Windows.UI.Core;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    private sealed record PickerTab(string Key, string Title);

    private static readonly PickerTab[] MouseTabs =
    [
        new("keys", "Keys"), new("mouse", "Mouse"), new("excel", "Excel"), new("ring", "Ring"), new("more", "More"), new("default", "Default"),
    ];

    private static readonly PickerTab[] ThumbTabs =
    [
        new("twoway", "Two-way"), new("keys", "Keys"), new("mouse", "Mouse"), new("excel", "Excel"), new("ring", "Ring"), new("more", "More"), new("default", "Default"),
    ];

    private static readonly PickerTab[] KeyboardTabs =
    [
        new("keys", "Keys"), new("app", "App"), new("media", "Media"), new("more", "More"), new("default", "Default"),
    ];

    /// <summary>What one open picker offers: its tabs, what is chosen now, and where a choice goes.</summary>
    private sealed record PickerSpec(
        string Header, PickerTab[] Tabs, string Initial, ActionSpec? Own, string DefaultText, bool ThumbWheel, Action<ActionSpec?> Apply)
    {
        /// <summary>Extra content under the header (the ring's Name box).</summary>
        public Func<FrameworkElement>? Extra { get; init; }

        /// <summary>Builds a tab the picker does not know (the ring's Icon tab).</summary>
        public Func<string, (FrameworkElement Content, FrameworkElement? Focus)?>? CustomTab { get; init; }

        /// <summary>Runs when the flyout closes (the ring returns focus to its preview).</summary>
        public Action? Closed { get; init; }

        /// <summary>Opens at this point of the anchor instead of under it (the ring anchors to its preview, whose slots are redrawn).</summary>
        public Windows.Foundation.Point? At { get; init; }

        /// <summary>App profiles only: the "Normal ... in app" option that stores an explicit None (pass-through).</summary>
        public string? PassThroughText { get; init; }

        /// <summary>True when the control already holds an explicit None in this app, so the pass-through option is the chosen one.</summary>
        public bool IsPassThrough { get; init; }

        /// <summary>The More tab offers "Press / Hold" (buttons and keys, not the gesture button, a swipe or the thumb wheel).</summary>
        public bool AllowTapHold { get; init; }

        /// <summary>The Sense Panel: Press / Hold shows the one-click "Hold opens the Actions Ring" preset.</summary>
        public bool SensePanel { get; init; }
    }

    /// <summary>
    /// The action picker: a flyout anchored under the label. Tabs for the device, a recorder for key chords. Nothing is saved
    /// until an option is chosen or Enter/Save is pressed; Esc cancels.
    /// </summary>
    private void ShowPicker(FrameworkElement anchor, ControlInfo info, string? app = null, bool useCurrentApp = true)
    {
        string? target = useCurrentApp ? _app : app;
        Profile? profile = ProfileOps.Find(_config.Config, target);
        string appName = target is null ? "All apps" : profile is null ? target : ProfileOps.AppName(profile);
        ActionSpec? own = ProfileOps.Own(_config.Config, target, _kind, info.Id);

        PickerTab[] tabs = _kind == DeviceKind.Mouse ? (info.Id == DeviceIds.ThumbWheelPseudoId ? ThumbTabs : MouseTabs) : KeyboardTabs;
        string initial = own switch
        {
            ActionSpec.Keys when _kind == DeviceKind.Keyboard && ActionCatalog.KeyboardAppOptions.Any(o => o.Action.Equals(own)) => "app",
            ActionSpec.Keys when _kind == DeviceKind.Keyboard && ActionCatalog.KeyboardMediaOptions.Any(o => o.Action.Equals(own)) => "media",
            ActionSpec.Keys k when _kind == DeviceKind.Mouse && ActionCatalog.TryDecodePair(k.Chord, out _, out _) => "twoway",
            ActionSpec.Keys => "keys",
            ActionSpec.MouseButton or ActionSpec.ToggleThumbWheelMode => "mouse",
            ActionSpec.Excel => "excel",
            ActionSpec.ActionsRing => "ring",
            ActionSpec.Brightness when _kind == DeviceKind.Keyboard => "media",
            ActionSpec.Launch or ActionSpec.Disabled or ActionSpec.Brightness or ActionSpec.Text or ActionSpec.TapHold => "more",
            _ => "default",
        };

        string defaultText;
        if (target is null)
        {
            defaultText = $"Use the device default ({info.NativeText})";
        }
        else
        {
            ActionSpec? inherited = ProfileOps.Own(_config.Config, null, _kind, info.Id);
            defaultText = $"Same as All apps ({(inherited is null ? info.NativeText : ActionCatalog.Plain(inherited))})";
        }

        ShowPickerCore(anchor, new PickerSpec(
            target is null ? info.Name : $"{info.Name} in {appName}", tabs, initial, own, defaultText, info.Id == DeviceIds.ThumbWheelPseudoId,
            action => _config.Update(c => ProfileOps.SetBinding(c, target, _kind, info.Id, action)))
        {
            PassThroughText = target is null ? null : $"Normal {info.NativeText} in {appName}",
            IsPassThrough = target is not null && ProfileOps.IsPassThrough(_config.Config, target, _kind, info.Id),
            AllowTapHold = ActionCatalog.CanTapHold(_kind, info.Id),
            SensePanel = _kind == DeviceKind.Mouse && info.Id == Slender.Hidpp.ControlId.SensePanel,
        });
    }

    private void ShowPickerCore(FrameworkElement anchor, PickerSpec spec)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = Ui.Res<Style>("PickerFlyoutPresenterStyle"),
        };

        void Commit(ActionSpec? action)
        {
            flyout.Hide();
            spec.Apply(action);
        }

        PickerTab[] tabs = spec.Tabs;
        string initial = spec.Initial;

        var header = Ui.Text(spec.Header, 12, Ui.Muted);
        var host = new Grid { MinHeight = 150 };
        var bar = new SelectorBar();
        foreach (PickerTab t in tabs)
        {
            bar.Items.Add(new SelectorBarItem { Text = t.Title, Tag = t.Key });
        }

        FrameworkElement? firstFocus = null;
        void ShowTab(string key)
        {
            host.Children.Clear();
            (FrameworkElement content, FrameworkElement? focus) = BuildTab(key, spec, Commit, flyout);
            host.Children.Add(content);
            firstFocus = focus;
        }

        bar.SelectionChanged += (s, _) =>
        {
            if (s.SelectedItem?.Tag is string key)
            {
                ShowTab(key);
                DispatcherQueue.TryEnqueue(() => (firstFocus as Control)?.Focus(FocusState.Keyboard));
            }
        };

        var root = new StackPanel { Spacing = 10, Width = 316 };
        root.Children.Add(header);
        if (spec.Extra is not null)
        {
            root.Children.Add(spec.Extra());
        }

        root.Children.Add(bar);
        root.Children.Add(host);
        flyout.Content = root;

        flyout.Opened += (_, _) =>
        {
            (firstFocus as Control)?.Focus(FocusState.Keyboard);
        };
        flyout.Closed += (_, _) =>
        {
            spec.Closed?.Invoke();
            if (anchor is Control c && c.IsLoaded)
            {
                c.Focus(FocusState.Programmatic);
            }
        };

        bar.SelectedItem = bar.Items.First(i => (string?)i.Tag == initial);
        if (spec.At is { } at)
        {
            flyout.ShowAt(anchor, new FlyoutShowOptions { Position = at, Placement = FlyoutPlacementMode.Bottom });
        }
        else
        {
            flyout.ShowAt(anchor);
        }
    }

    private (FrameworkElement Content, FrameworkElement? Focus) BuildTab(string key, PickerSpec spec, Action<ActionSpec?> commit, Flyout flyout)
    {
        ActionSpec? own = spec.Own;
        if (spec.CustomTab?.Invoke(key) is { } custom)
        {
            return custom;
        }

        switch (key)
        {
            case "keys":
                return KeysRecorder(own as ActionSpec.Keys, commit, flyout);
            case "twoway":
                return OptionList(ActionCatalog.ThumbPairOptions, own, commit);
            case "mouse":
                return OptionList([.. ActionCatalog.MouseOptions, .. ActionCatalog.ThumbModeOptions], own, commit);
            case "excel":
                return OptionList(ActionCatalog.ExcelOptions, own, commit);
            case "ring":
                return OptionList(ActionCatalog.RingOptions, own, commit);
            case "more":
                return MoreTab(spec.ThumbWheel, own, commit, spec.AllowTapHold, spec.SensePanel);
            case "app":
                return OptionList(ActionCatalog.KeyboardAppOptions, own, commit);
            case "media":
                return OptionList(ActionCatalog.KeyboardMediaOptions, own, commit);
            default:
                // Option actions here are only markers: None = "same as All apps / device default" (commits null),
                // Disabled = "normal function in this app" (commits an explicit None, the pass-through).
                var sameAs = new ActionOption(spec.DefaultText, new ActionSpec.None());
                if (spec.PassThroughText is null)
                {
                    return OptionList([sameAs], own is null ? new ActionSpec.None() : null, a => commit(null));
                }

                var passThrough = new ActionOption(spec.PassThroughText, new ActionSpec.Disabled());
                ActionSpec? chosen = spec.IsPassThrough ? new ActionSpec.Disabled() : own is null ? new ActionSpec.None() : null;
                return OptionList([sameAs, passThrough], chosen, a => commit(a is ActionSpec.Disabled ? new ActionSpec.None() : null));
        }
    }

    /// <summary>
    /// "Turn off" plus (not for the thumb wheel) "Open app or file...". A .lnk is launched as is, so Windows resolves it and keeps
    /// its arguments. A URL, folder or Store app ("shell:AppsFolder\AUMID") can be typed instead of browsed.
    /// </summary>
    private (FrameworkElement Content, FrameworkElement? Focus) MoreTab(bool thumbWheel, ActionSpec? own, Action<ActionSpec?> commit, bool allowTapHold = false, bool sensePanel = false)
    {
        (FrameworkElement off, FrameworkElement? focus) = OptionList([.. ActionCatalog.OffOptions, .. ActionCatalog.BrightnessOptions], own, commit);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(off);
        if (thumbWheel)
        {
            return (panel, focus);
        }

        string current = (own as ActionSpec.Launch)?.Target ?? string.Empty;
        var browse = new Button { Content = "Open app or file...", Style = Ui.Res<Style>("QuietButtonStyle") };
        var box = new TextBox { PlaceholderText = "Or type a URL, folder or app path", Text = current };
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryPillStyle"), IsEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "URL, folder or app path to open");

        box.TextChanged += (_, _) => save.IsEnabled = box.Text.Trim().Length > 0 && box.Text.Trim() != current;
        void Save()
        {
            string text = box.Text.Trim().Trim('"');
            if (text.Length > 0)
            {
                string? args = own is ActionSpec.Launch l && string.Equals(l.Target, text, StringComparison.Ordinal) ? l.Arguments : null;
                commit(new ActionSpec.Launch(text, args));
            }
        }

        save.Click += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                if (save.IsEnabled)
                {
                    Save();
                }
            }
        };
        browse.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add("*");
            Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                commit(new ActionSpec.Launch(file.Path));
            }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(save);
        panel.Children.Add(browse);
        panel.Children.Add(box);
        panel.Children.Add(row);
        panel.Children.Add(TypeTextEditor(own as ActionSpec.Text, commit));
        if (allowTapHold)
        {
            panel.Children.Add(TapHoldEditor(own as ActionSpec.TapHold, sensePanel, commit));
        }

        return (panel, focus);
    }

    /// <summary>
    /// "Press / Hold": a quick press runs one action; holding for 350 ms runs another (released with the button). Each half is a
    /// list of actions plus a box for a typed shortcut; neither can itself be a Press / Hold.
    /// </summary>
    private FrameworkElement TapHoldEditor(ActionSpec.TapHold? current, bool sensePanel, Action<ActionSpec?> commit)
    {
        Action refresh = () => { };
        (FrameworkElement quickPanel, Func<ActionSpec?> quick, _) = TapHoldHalf("Quick press", current?.Tap, () => refresh());
        (FrameworkElement holdPanel, Func<ActionSpec?> hold, Action<ActionSpec> setHold) = TapHoldHalf($"Hold ({ActionSpec.TapHold.HoldMs} ms)", current?.Hold, () => refresh());
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryPillStyle"), IsEnabled = false };

        refresh = () => save.IsEnabled = ActionCatalog.TryBuildTapHold(quick(), hold(), out ActionSpec.TapHold built) && !built.Equals(current);
        save.Click += (_, _) =>
        {
            if (ActionCatalog.TryBuildTapHold(quick(), hold(), out ActionSpec.TapHold built))
            {
                commit(built);
            }
        };

        var inner = new StackPanel { Spacing = 8 };
        inner.Children.Add(quickPanel);
        inner.Children.Add(holdPanel);
        if (sensePanel)
        {
            var preset = new Button { Content = "Hold opens the Actions Ring", Style = Ui.Res<Style>("QuietButtonStyle") };
            preset.Click += (_, _) =>
            {
                setHold(new ActionSpec.ActionsRing());
                refresh();
            };
            inner.Children.Add(preset);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(save);
        inner.Children.Add(row);
        return new Expander
        {
            Header = "Press / Hold",
            IsExpanded = current is not null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = inner,
        };
    }

    /// <summary>One half of a Press / Hold: a list of actions and a typed-shortcut box. The getter is null while the typed shortcut is invalid.</summary>
    private (FrameworkElement Panel, Func<ActionSpec?> Get, Action<ActionSpec> Set) TapHoldHalf(string title, ActionSpec? current, Action changed)
    {
        var options = new List<ActionOption>(ActionCatalog.TapHoldChoices(_kind));
        string typed = string.Empty;
        ActionSpec chosen = current ?? new ActionSpec.None();
        if (chosen is ActionSpec.Keys k && !options.Any(o => o.Action.Equals(chosen)))
        {
            typed = k.Chord;
        }
        else if (chosen is not ActionSpec.TapHold && !options.Any(o => o.Action.Equals(chosen)))
        {
            options.Insert(0, new ActionOption("Current: " + ActionCatalog.Plain(chosen), chosen)); // keeps a Launch or Type text the user set elsewhere
        }

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (ActionOption o in options)
        {
            combo.Items.Add(o.Title);
        }

        int index = options.FindIndex(o => o.Action.Equals(chosen));
        combo.SelectedIndex = typed.Length > 0 ? 0 : Math.Max(0, index);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, title);
        var box = new TextBox { PlaceholderText = "Or type a shortcut, e.g. Ctrl+Shift+P", Text = typed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, title + " shortcut");

        ActionSpec? Get()
        {
            if (box.Text.Trim().Length > 0)
            {
                return ActionCatalog.TryParseShortcut(box.Text, out ActionSpec.Keys keys) ? keys : null;
            }

            return combo.SelectedIndex >= 0 && combo.SelectedIndex < options.Count ? options[combo.SelectedIndex].Action : null;
        }

        var panel = new StackPanel { Spacing = 4 };
        combo.SelectionChanged += (_, _) =>
        {
            if (box.Text.Length > 0)
            {
                box.Text = string.Empty; // picking from the list replaces a typed shortcut
            }

            changed();
        };
        box.TextChanged += (_, _) => changed();
        panel.Children.Add(Ui.Text(title, 12, Ui.Muted));
        panel.Children.Add(combo);
        panel.Children.Add(box);

        void Set(ActionSpec action)
        {
            box.Text = string.Empty;
            int i = options.FindIndex(o => o.Action.Equals(action));
            if (i >= 0)
            {
                combo.SelectedIndex = i;
            }
        }

        return (panel, Get, Set);
    }

    /// <summary>"Type text...": a multi-line box; the saved text is typed into the foreground app. Ctrl+Enter saves.</summary>
    private static FrameworkElement TypeTextEditor(ActionSpec.Text? current, Action<ActionSpec?> commit)
    {
        string saved = current?.Value ?? string.Empty;
        var open = new Button { Content = "Type text...", Style = Ui.Res<Style>("QuietButtonStyle") };
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxHeight = 140,
            MaxLength = 2000,
            PlaceholderText = "Text to type",
            Text = saved,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Text to type");
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryPillStyle"), IsEnabled = false };
        var hint = Ui.Text("Ctrl+Enter saves. Enter starts a new line.", 12, Ui.Muted);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        var area = new StackPanel { Spacing = 8, Visibility = current is null ? Visibility.Collapsed : Visibility.Visible };
        area.Children.Add(box);
        area.Children.Add(hint);
        area.Children.Add(buttons);

        box.TextChanged += (_, _) => save.IsEnabled = box.Text.Trim().Length > 0 && box.Text != saved;
        void Save()
        {
            if (box.Text.Trim().Length > 0)
            {
                commit(new ActionSpec.Text(box.Text.Replace("\r\n", "\n").Replace('\r', '\n')));
            }
        }

        save.Click += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && Down(VirtualKey.Control))
            {
                e.Handled = true;
                Save();
            }
        };
        open.Click += (_, _) =>
        {
            area.Visibility = Visibility.Visible;
            box.Focus(FocusState.Keyboard);
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(open);
        panel.Children.Add(area);
        return panel;
    }

    private static (FrameworkElement Content, FrameworkElement? Focus) OptionList(IReadOnlyList<ActionOption> options, ActionSpec? current, Action<ActionSpec?> commit)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxHeight = 264,
        };
        ListViewItem? selected = null;
        foreach (ActionOption option in options)
        {
            var item = new ListViewItem
            {
                Content = Ui.Text(option.Title, 13),
                Tag = option,
                MinHeight = 36,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 0, 10, 0),
            };
            list.Items.Add(item);
            if (current is not null && option.Action.Equals(current))
            {
                selected = item;
            }
        }

        if (selected is not null)
        {
            list.SelectedItem = selected;
        }

        list.ItemClick += (_, e) =>
        {
            ActionOption? picked = ClickedItems.Find<ActionOption>(e.ClickedItem,
                list.Items.OfType<ListViewItem>().Select(i => ((object)i, (object?)i.Content, (object?)i.Tag)));
            if (picked is not null)
            {
                commit(picked.Action);
            }
        };

        // Focus the chosen row (or the first) when the tab opens.
        FrameworkElement focus = selected ?? (FrameworkElement)list.Items[0];
        return (list, focus);
    }

    private (FrameworkElement Content, FrameworkElement? Focus) KeysRecorder(ActionSpec.Keys? current, Action<ActionSpec?> commit, Flyout flyout)
    {
        string? recorded = current?.Chord;
        var capHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var hint = Ui.Text("Press the shortcut you want, then Enter to save.", 12, Ui.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryPillStyle"), IsEnabled = false };
        var cancel = new Button { Content = "Cancel", Style = Ui.Res<Style>("QuietButtonStyle") };

        void Show()
        {
            capHost.Children.Clear();
            if (recorded is null)
            {
                capHost.Children.Add(Ui.Text("Press the keys…", 13, Ui.Muted));
            }
            else
            {
                foreach (string part in ActionCatalog.ChordParts(recorded))
                {
                    capHost.Children.Add(Ui.Chip(part, 13));
                }
            }

            save.IsEnabled = recorded is not null && recorded != current?.Chord;
        }

        Show();

        var box = new Grid { Height = 72 };
        box.Children.Add(new Rectangle
        {
            RadiusX = 6,
            RadiusY = 6,
            Stroke = Ui.Mark,
            StrokeThickness = 1.2,
            StrokeDashArray = [4, 3],
            Fill = Ui.Brush("MarkSoftBrush"),
        });
        box.Children.Add(capHost);

        var recorder = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = box,
            CornerRadius = new CornerRadius(6),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(recorder, "Key recorder. Press a shortcut.");

        // Keys are captured anywhere in the picker while this tab is showing, not only when the recorder has focus:
        // after clicking the Keys tab, focus can stay on the tab strip and the shortcut would otherwise be lost.
        void OnKey(object sender, KeyRoutedEventArgs e)
        {
            if (!recorder.IsLoaded || e.Handled)
            {
                return;
            }

            ushort vk = (ushort)e.Key;
            bool ctrl = Down(VirtualKey.Control);
            bool alt = Down(VirtualKey.Menu);
            bool shift = Down(VirtualKey.Shift);
            bool win = Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows);
            bool plain = !ctrl && !alt && !shift && !win;

            if (ChordRecorder.IsModifier(vk))
            {
                e.Handled = true;
                return;
            }

            object? focused = recorder.XamlRoot is null ? null : FocusManager.GetFocusedElement(recorder.XamlRoot);
            if (plain && (vk == (ushort)VirtualKey.Enter || vk == (ushort)VirtualKey.Space) && (ReferenceEquals(focused, save) || ReferenceEquals(focused, cancel)))
            {
                return; // let Save and Cancel act on their own keys
            }

            if (plain && vk == (ushort)VirtualKey.Enter)
            {
                e.Handled = true;
                if (recorded is not null && recorded != current?.Chord)
                {
                    commit(new ActionSpec.Keys(recorded));
                }

                return;
            }

            if (plain && (vk == (ushort)VirtualKey.Escape || vk == (ushort)VirtualKey.Tab))
            {
                return; // Esc cancels the flyout; Tab moves on to Save and Cancel
            }

            string? chord = ChordRecorder.Compose(win, ctrl, alt, shift, vk);
            e.Handled = true;
            if (chord is null)
            {
                hint.Text = "Slender cannot send that key. Try another.";
                return;
            }

            hint.Text = "Press Enter to save, or press other keys to change it.";
            recorded = chord;
            Show();
        }

        if (flyout.Content is UIElement pickerRoot)
        {
            pickerRoot.PreviewKeyDown += OnKey;
            recorder.Unloaded += (_, _) => pickerRoot.PreviewKeyDown -= OnKey;
        }
        else
        {
            recorder.PreviewKeyDown += OnKey;
        }

        // Focus once the recorder is really in the tree; Focus() before that is silently ignored.
        recorder.Loaded += (_, _) => recorder.Focus(FocusState.Keyboard);

        save.Click += (_, _) =>
        {
            if (recorded is not null)
            {
                commit(new ActionSpec.Keys(recorded));
            }
        };
        cancel.Click += (_, _) => flyout.Hide();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(recorder);
        panel.Children.Add(hint);
        panel.Children.Add(buttons);
        return (panel, recorder);
    }

    private static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
