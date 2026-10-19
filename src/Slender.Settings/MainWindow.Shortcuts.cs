using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Slender.Engine;
using Slender.Settings.Services;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// The picker's Shortcuts tab: the user's own shortcuts ("My shortcuts", in the config so they sync), then suggestions for the
/// app (ShortcutLibrary), common and Windows ones. Click one to use it. My shortcuts are added, edited and deleted in place
/// (name, recorded keys, All apps or this app only). Suggestions can be hidden; "Show hidden" brings them back. Hidden rows and
/// the tab's on/off live in settings-ui.json.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>True when the chord is a suggestion the Shortcuts tab shows for this app (tab on, row not hidden).</summary>
    private bool ShortcutShown(string? app, string chord) =>
        !_ui.HideShortcutSuggestions
        && (MyShortcutOps.For(_config.Config, app).Any(e => string.Equals(e.Shortcut.Chord, chord, StringComparison.OrdinalIgnoreCase))
        || ShortcutLibrary.For(app).Any(g => g.Items.Any(s =>
            string.Equals(s.Chord, chord, StringComparison.OrdinalIgnoreCase)
            && !_ui.HiddenShortcuts.Contains(ShortcutLibrary.HideKey(g.Id, s.Chord), StringComparer.OrdinalIgnoreCase))));

    private (FrameworkElement Content, FrameworkElement? Focus) ShortcutsTab(string? app, ActionSpec? own, Action<ActionSpec?> commit)
    {
        string? appName = app is not null && ProfileOps.Find(_config.Config, app) is { } profile ? ProfileOps.AppName(profile) : null;
        IReadOnlyList<ShortcutGroup> groups = ShortcutLibrary.For(app, appName);
        string? current = (own as ActionSpec.Keys)?.Chord;
        var hidden = new HashSet<string>(_ui.HiddenShortcuts, StringComparer.OrdinalIgnoreCase);
        bool showHidden = false;

        var list = new StackPanel { Spacing = 2 };
        var scroller = new ScrollViewer
        {
            Content = list,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 0),
        };
        var toggleHidden = new Button { Style = Ui.Res<Style>("QuietButtonStyle") };
        FrameworkElement? first = null;

        FrameworkElement Row(Shortcut s, string key, bool isHidden)
        {
            bool chosen = string.Equals(current, s.Chord, StringComparison.OrdinalIgnoreCase);
            var inner = new Grid { ColumnSpacing = 8 };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Text(s.Name, 13, isHidden ? Ui.Muted : null, weight: chosen ? FontWeights.SemiBold : null);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            foreach (string part in ActionCatalog.ChordParts(s.Chord))
            {
                chips.Children.Add(Ui.Chip(part, 10));
            }

            Grid.SetColumn(chips, 1);
            inner.Children.Add(name);
            inner.Children.Add(chips);

            var pick = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(6),
                Content = inner,
            };
            if (chosen)
            {
                pick.Background = Ui.Brush("MarkSoftBrush");
            }

            AutomationProperties.SetName(pick, $"{s.Name}, {s.Chord}");
            pick.Click += (_, _) => commit(new ActionSpec.Keys(s.Chord));

            var eye = new Button
            {
                Style = Ui.Res<Style>("QuietButtonStyle"),
                Content = Ui.Glyph(isHidden ? "" : "", 12),
                Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(eye, isHidden ? "Show again" : "Hide");
            AutomationProperties.SetName(eye, (isHidden ? "Show again: " : "Hide: ") + s.Name);
            eye.Click += (_, _) =>
            {
                if (isHidden)
                {
                    hidden.Remove(key);
                }
                else
                {
                    hidden.Add(key);
                }

                _ui.HiddenShortcuts = [.. hidden.Order(StringComparer.OrdinalIgnoreCase)];
                SaveUiNow();
                Build();
            };

            var row = new Grid { ColumnSpacing = 2 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(eye, 1);
            row.Children.Add(pick);
            row.Children.Add(eye);
            first ??= pick;
            return row;
        }

        int editing = int.MinValue; // int.MinValue = no editor; -1 = adding; >= 0 = editing that entry

        FrameworkElement MineRow(MyShortcutEntry entry)
        {
            CustomShortcut s = entry.Shortcut;
            bool chosen = string.Equals(current, s.Chord, StringComparison.OrdinalIgnoreCase);
            var inner = new Grid { ColumnSpacing = 8 };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(s.Name, 13, null, weight: chosen ? FontWeights.SemiBold : null);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(name);
            if (s.App is not null)
            {
                names.Children.Add(Ui.Text((appName ?? s.App) + " only", 11, Ui.Muted));
            }

            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            foreach (string part in ActionCatalog.ChordParts(s.Chord))
            {
                chips.Children.Add(Ui.Chip(part, 10));
            }

            Grid.SetColumn(chips, 1);
            inner.Children.Add(names);
            inner.Children.Add(chips);
            var pick = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(6),
                Content = inner,
                Background = chosen ? Ui.Brush("MarkSoftBrush") : Ui.Brush("RailTransparent"),
            };
            AutomationProperties.SetName(pick, $"{s.Name}, {s.Chord}");
            pick.Click += (_, _) => commit(new ActionSpec.Keys(s.Chord));

            Button Tool(string glyph, string tip, Action run)
            {
                var b = new Button { Style = Ui.Res<Style>("QuietButtonStyle"), Content = Ui.Glyph(glyph, 12), Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetToolTip(b, tip);
                AutomationProperties.SetName(b, tip + ": " + s.Name);
                b.Click += (_, _) => run();
                return b;
            }

            var edit = Tool("\uE70F", "Edit", () => { editing = entry.Index; Build(); });
            var delete = Tool("\uE74D", "Delete", () => { _config.Update(c => MyShortcutOps.Remove(c, entry.Index)); editing = int.MinValue; Build(); });
            var row = new Grid { ColumnSpacing = 2 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(edit, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(pick);
            row.Children.Add(edit);
            row.Children.Add(delete);
            first ??= pick;
            return row;
        }

        void AddMine()
        {
            var head = new Grid { Margin = new Thickness(2, 0, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Ui.Text("MY SHORTCUTS", 11, Ui.Muted);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            if (editing == int.MinValue)
            {
                var add = new Button { Content = "+ Add", Style = Ui.Res<Style>("QuietButtonStyle"), Padding = new Thickness(8, 2, 8, 2) };
                AutomationProperties.SetName(add, "Add one of my shortcuts");
                add.Click += (_, _) => { editing = -1; Build(); };
                Grid.SetColumn(add, 1);
                head.Children.Add(add);
            }

            list.Children.Add(head);
            IReadOnlyList<MyShortcutEntry> mine = MyShortcutOps.For(_config.Config, app);
            if (editing == -1)
            {
                list.Children.Add(MineEditor(null));
            }

            foreach (MyShortcutEntry e in mine)
            {
                list.Children.Add(editing == e.Index ? MineEditor(e) : MineRow(e));
            }

            if (mine.Count == 0 && editing == int.MinValue)
            {
                var empty = Ui.Text("Add the shortcuts you use often. They show here, for every button.", 12, Ui.Muted);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.Margin = new Thickness(8, 0, 0, 4);
                list.Children.Add(empty);
            }
        }

        FrameworkElement MineEditor(MyShortcutEntry? entry)
        {
            string? chord = entry?.Shortcut.Chord;
            var panel = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                BorderBrush = Ui.Mark,
                BorderThickness = new Thickness(1),
                Background = Ui.Brush("MarkSoftBrush"),
            };
            var nameBox = new TextBox { PlaceholderText = "Name, for example Paste as values", Text = entry?.Shortcut.Name ?? string.Empty, MaxLength = MyShortcutOps.MaxNameLength };
            AutomationProperties.SetName(nameBox, "Shortcut name");
            panel.Children.Add(nameBox);

            var keysHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var recorder = new Button
            {
                Style = Ui.Res<Style>("QuietButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 34,
                Content = keysHost,
            };
            AutomationProperties.SetName(recorder, "Keys. Click, then press the shortcut.");
            bool listening = false;
            RecorderKeyHook? hook = null;
            void ShowKeys()
            {
                keysHost.Children.Clear();
                if (listening)
                {
                    keysHost.Children.Add(Ui.Text("Press the keys now", 12.5, Ui.Mark));
                }
                else if (chord is null)
                {
                    keysHost.Children.Add(Ui.Text("Click, then press the keys", 12.5, Ui.Muted));
                }
                else
                {
                    foreach (string part in ActionCatalog.ChordParts(chord))
                    {
                        keysHost.Children.Add(Ui.Chip(part, 11));
                    }
                }
            }

            void StopListening()
            {
                if (!listening)
                {
                    return;
                }

                listening = false;
                hook?.Dispose();
                hook = null;
                SetWindowShortcutsEnabled(true);
                ShowKeys();
            }

            void Got(string? recorded)
            {
                if (recorded is not null)
                {
                    chord = recorded;
                }

                StopListening();
            }

            recorder.Click += (_, _) =>
            {
                if (listening)
                {
                    return;
                }

                listening = true;
                SetWindowShortcutsEnabled(false); // Ctrl+Tab and friends would act on the window instead of being recorded
                hook = new RecorderKeyHook(_hwnd, DispatcherQueue, r =>
                {
                    if (listening && !r.Unsupported)
                    {
                        Got(r.Chord);
                    }
                });
                ShowKeys();
            };
            recorder.PreviewKeyDown += (_, e) =>
            {
                if (!listening)
                {
                    return;
                }

                e.Handled = true;
                ushort vk = (ushort)e.Key;
                if (ChordRecorder.IsModifier(vk))
                {
                    return;
                }

                bool ctrl = Down(Windows.System.VirtualKey.Control), alt = Down(Windows.System.VirtualKey.Menu);
                bool shift = Down(Windows.System.VirtualKey.Shift);
                bool win = Down(Windows.System.VirtualKey.LeftWindows) || Down(Windows.System.VirtualKey.RightWindows);
                if (!ctrl && !alt && !shift && !win && e.Key == Windows.System.VirtualKey.Escape)
                {
                    StopListening();
                    return;
                }

                Got(ChordRecorder.Compose(win, ctrl, alt, shift, vk));
            };
            recorder.LostFocus += (_, _) => StopListening();
            recorder.Unloaded += (_, _) => StopListening();
            ShowKeys();
            panel.Children.Add(recorder);

            var scope = new ComboBox { MinWidth = 160 };
            scope.Items.Add("All apps");
            if (app is not null)
            {
                scope.Items.Add((appName ?? app) + " only");
            }

            scope.SelectedIndex = entry?.Shortcut.App is not null && app is not null ? 1 : 0;
            AutomationProperties.SetName(scope, "Show it for");
            var problem = Ui.Text(string.Empty, 11.5, Ui.Brush("WarnBrush"));
            problem.TextWrapping = TextWrapping.Wrap;

            var save = new Button { Content = "Save", Style = Ui.Res<Style>("AccentButtonStyle") };
            var cancel = new Button { Content = "Cancel" };
            save.Click += (_, _) =>
            {
                if (MyShortcutOps.Problem(nameBox.Text, chord) is { } why)
                {
                    problem.Text = why;
                    return;
                }

                string? forApp = scope.SelectedIndex == 1 ? app : null;
                _config.Update(c => entry is null
                    ? MyShortcutOps.Add(c, nameBox.Text, chord!, forApp)
                    : MyShortcutOps.Update(c, entry.Index, nameBox.Text, chord!, forApp));
                editing = int.MinValue;
                Build();
            };
            cancel.Click += (_, _) => { editing = int.MinValue; Build(); };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(scope);
            actions.Children.Add(save);
            actions.Children.Add(cancel);
            panel.Children.Add(actions);
            panel.Children.Add(problem);
            panel.Loaded += (_, _) => nameBox.Focus(FocusState.Programmatic);
            return panel;
        }

        void Build()
        {
            list.Children.Clear();
            first = null;
            AddMine();
            int hiddenCount = 0;
            foreach (ShortcutGroup g in groups)
            {
                var rows = new List<FrameworkElement>();
                foreach (Shortcut s in g.Items)
                {
                    string key = ShortcutLibrary.HideKey(g.Id, s.Chord);
                    bool isHidden = hidden.Contains(key);
                    hiddenCount += isHidden ? 1 : 0;
                    if (!isHidden || showHidden)
                    {
                        rows.Add(Row(s, key, isHidden));
                    }
                }

                if (rows.Count == 0)
                {
                    continue;
                }

                var head = Ui.Text(g.Title.ToUpperInvariant(), 11, Ui.Muted);
                head.Margin = new Thickness(2, 10, 0, 4);
                list.Children.Add(head);
                foreach (FrameworkElement r in rows)
                {
                    list.Children.Add(r);
                }
            }

            if (groups.Count > 0 && hiddenCount > 0 && !showHidden && groups.All(g => g.Items.All(s => hidden.Contains(ShortcutLibrary.HideKey(g.Id, s.Chord)))))
            {
                list.Children.Add(Ui.Text("Every suggestion is hidden.", 12, Ui.Muted));
            }

            toggleHidden.Visibility = hiddenCount > 0 || showHidden ? Visibility.Visible : Visibility.Collapsed;
            toggleHidden.Content = showHidden ? "Done" : $"Show hidden ({hiddenCount})";
        }

        toggleHidden.Click += (_, _) =>
        {
            showHidden = !showHidden;
            Build();
        };

        var note = Ui.Text("Hide the ones you never use. Settings, General turns this tab off.", 11, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(scroller);
        panel.Children.Add(toggleHidden);
        panel.Children.Add(note);
        Build();
        return (panel, first);
    }
}
