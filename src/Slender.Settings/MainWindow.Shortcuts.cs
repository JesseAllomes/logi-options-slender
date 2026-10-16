using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// The picker's Shortcuts tab: suggested shortcuts for the app (ShortcutLibrary), then common and Windows ones. Click one to
/// use it. Each row can be hidden; "Show hidden" brings them back. Hidden rows and the tab's on/off live in settings-ui.json.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>True when the chord is a suggestion the Shortcuts tab shows for this app (tab on, row not hidden).</summary>
    private bool ShortcutShown(string? app, string chord) =>
        !_ui.HideShortcutSuggestions
        && ShortcutLibrary.For(app).Any(g => g.Items.Any(s =>
            string.Equals(s.Chord, chord, StringComparison.OrdinalIgnoreCase)
            && !_ui.HiddenShortcuts.Contains(ShortcutLibrary.HideKey(g.Id, s.Chord), StringComparer.OrdinalIgnoreCase)));

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

        void Build()
        {
            list.Children.Clear();
            first = null;
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
                head.Margin = new Thickness(2, list.Children.Count == 0 ? 0 : 10, 0, 4);
                list.Children.Add(head);
                foreach (FrameworkElement r in rows)
                {
                    list.Children.Add(r);
                }
            }

            if (list.Children.Count == 0)
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
