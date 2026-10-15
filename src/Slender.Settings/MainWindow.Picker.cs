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

        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = Ui.Res<Style>("PickerFlyoutPresenterStyle"),
        };

        void Commit(ActionSpec? action)
        {
            flyout.Hide();
            _config.Update(c => ProfileOps.SetBinding(c, target, _kind, info.Id, action));
        }

        PickerTab[] tabs = _kind == DeviceKind.Mouse ? (info.Id == DeviceIds.ThumbWheelPseudoId ? ThumbTabs : MouseTabs) : KeyboardTabs;
        string initial = own switch
        {
            ActionSpec.Keys when _kind == DeviceKind.Keyboard && ActionCatalog.KeyboardAppOptions.Any(o => o.Action.Equals(own)) => "app",
            ActionSpec.Keys when _kind == DeviceKind.Keyboard && ActionCatalog.KeyboardMediaOptions.Any(o => o.Action.Equals(own)) => "media",
            ActionSpec.Keys k when _kind == DeviceKind.Mouse && ActionCatalog.TryDecodePair(k.Chord, out _, out _) => "twoway",
            ActionSpec.Keys => "keys",
            ActionSpec.MouseButton => "mouse",
            ActionSpec.Excel => "excel",
            ActionSpec.ActionsRing => "ring",
            ActionSpec.Launch or ActionSpec.Disabled => "more",
            _ => "default",
        };

        var header = Ui.Text(target is null ? info.Name : $"{info.Name} in {appName}", 12, Ui.Muted);
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
            (FrameworkElement content, FrameworkElement? focus) = BuildTab(key, info, target, own, Commit, flyout);
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
        root.Children.Add(bar);
        root.Children.Add(host);
        flyout.Content = root;

        flyout.Opened += (_, _) =>
        {
            (firstFocus as Control)?.Focus(FocusState.Keyboard);
        };
        flyout.Closed += (_, _) =>
        {
            if (anchor is Control c && c.IsLoaded)
            {
                c.Focus(FocusState.Programmatic);
            }
        };

        bar.SelectedItem = bar.Items.First(i => (string?)i.Tag == initial);
        flyout.ShowAt(anchor);
    }

    private (FrameworkElement Content, FrameworkElement? Focus) BuildTab(
        string key, ControlInfo info, string? target, ActionSpec? own, Action<ActionSpec?> commit, Flyout flyout)
    {
        switch (key)
        {
            case "keys":
                return KeysRecorder(own as ActionSpec.Keys, commit, flyout);
            case "twoway":
                return OptionList(ActionCatalog.ThumbPairOptions, own, commit);
            case "mouse":
                return OptionList(ActionCatalog.MouseOptions, own, commit);
            case "excel":
                return OptionList(ActionCatalog.ExcelOptions, own, commit);
            case "ring":
                return OptionList(ActionCatalog.RingOptions, own, commit);
            case "more":
                return MoreTab(info, own, commit);
            case "app":
                return OptionList(ActionCatalog.KeyboardAppOptions, own, commit);
            case "media":
                return OptionList(ActionCatalog.KeyboardMediaOptions, own, commit);
            default:
                string text;
                if (target is null)
                {
                    text = $"Use the device default ({info.NativeText})";
                }
                else
                {
                    ActionSpec? inherited = ProfileOps.Own(_config.Config, null, _kind, info.Id);
                    text = $"Same as All apps ({(inherited is null ? info.NativeText : ActionCatalog.Plain(inherited))})";
                }

                var option = new ActionOption(text, new ActionSpec.None());
                return OptionList([option], own is null ? new ActionSpec.None() : null, a => commit(null));
        }
    }

    /// <summary>
    /// "Turn off" plus (not for the thumb wheel) "Open app or file...". A .lnk is launched as is, so Windows resolves it and keeps
    /// its arguments. A URL, folder or Store app ("shell:AppsFolder\AUMID") can be typed instead of browsed.
    /// </summary>
    private (FrameworkElement Content, FrameworkElement? Focus) MoreTab(ControlInfo info, ActionSpec? own, Action<ActionSpec?> commit)
    {
        (FrameworkElement off, FrameworkElement? focus) = OptionList(ActionCatalog.OffOptions, own, commit);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(off);
        if (info.Id == DeviceIds.ThumbWheelPseudoId)
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
                commit(new ActionSpec.Launch(text));
            }
        }

        save.Click += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                Save();
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
        return (panel, focus);
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
            ActionOption? picked = (e.ClickedItem as ListViewItem)?.Tag as ActionOption ?? e.ClickedItem as ActionOption;
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

        recorder.PreviewKeyDown += (_, e) =>
        {
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
        };

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
