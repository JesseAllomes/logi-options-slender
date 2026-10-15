using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    private const double CompareNameColumn = 176;
    private const double CompareAppColumn = 156;

    private ContentDialog? _compare;
    private ScrollViewer? _compareScroll;

    private async void OnCompareClicked(object sender, RoutedEventArgs e) => await ShowCompareAsync();

    private async Task ShowCompareAsync()
    {
        if (_compare is not null)
        {
            return;
        }

        _compareScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            MaxHeight = Math.Max(300, Root.ActualHeight - 220),
        };
        _compare = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"Compare apps · {ControlCatalog.DeviceName(_kind)}",
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark,
            Content = _compareScroll,
        };
        _compare.Resources["ContentDialogMaxWidth"] = Math.Max(760.0, Root.ActualWidth - 100);
        _compare.Resources["ContentDialogMinWidth"] = Math.Min(900.0, Math.Max(760.0, Root.ActualWidth - 100));
        _compare.Resources["ContentDialogMaxHeight"] = Math.Max(420.0, Root.ActualHeight - 60);
        BuildCompareGrid();
        try
        {
            await _compare.ShowAsync();
        }
        finally
        {
            _compare = null;
            _compareScroll = null;
        }
    }

    private void RefreshCompare()
    {
        if (_compare is not null)
        {
            BuildCompareGrid();
        }
    }

    /// <summary>Full controls x apps grid. Every cell opens the same picker as the stage.</summary>
    private void BuildCompareGrid()
    {
        if (_compareScroll is null)
        {
            return;
        }

        List<Profile> columns = [.. _config.Config.Profiles.Where(p => p.AppProcess is null), .. _config.Config.Profiles.Where(p => p.AppProcess is not null)];
        if (columns.Count == 0 || columns[0].AppProcess is not null)
        {
            columns.Insert(0, new Profile(null, new Dictionary<ushort, ActionSpec>()) { DisplayName = "All apps" });
        }

        IReadOnlyList<ControlInfo> controls = ControlCatalog.For(_kind);
        var grid = new Grid { RowSpacing = 0, ColumnSpacing = 0 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CompareNameColumn) });
        foreach (Profile _ in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = CompareAppColumn });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        foreach (ControlInfo _ in controls)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        }

        for (int c = 0; c < columns.Count; c++)
        {
            Profile p = columns[c];
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            head.Children.Add(AppIcon(p.AppProcess is null ? null : p, 26));
            head.Children.Add(Ui.Text(p.AppProcess is null ? "All apps" : ProfileOps.AppName(p), 13, Ui.Ink, null, Microsoft.UI.Text.FontWeights.SemiBold));
            Grid.SetColumn(head, c + 1);
            grid.Children.Add(head);
        }

        for (int r = 0; r < controls.Count; r++)
        {
            ControlInfo info = controls[r];
            var name = Ui.Text(info.Name, 13);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetRow(name, r + 1);
            grid.Children.Add(name);

            for (int c = 0; c < columns.Count; c++)
            {
                string? process = columns[c].AppProcess;
                Button cell = CompareCell(info, process, columns[c]);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c + 1);
                grid.Children.Add(cell);
            }

            var rule = new Border { Height = 1, Background = Ui.Rule, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.7 };
            Grid.SetRow(rule, r + 1);
            Grid.SetColumnSpan(rule, columns.Count + 1);
            grid.Children.Add(rule);
        }

        var headRule = new Border { Height = 1, Background = Ui.Rule, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(headRule, columns.Count + 1);
        grid.Children.Add(headRule);

        _compareScroll.Content = grid;
    }

    private Button CompareCell(ControlInfo info, string? process, Profile profile)
    {
        (ActionSpec? action, bool inherited) = ProfileOps.Effective(_config.Config, process, _kind, info.Id);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        string plain;
        if (action is null)
        {
            plain = info.NativeText;
            row.Children.Add(Ui.Text(plain, 13, process is null ? Ui.Ink : Ui.Muted));
        }
        else
        {
            ActionLabel label = ActionCatalog.Describe(action);
            plain = ActionCatalog.Plain(action);
            if (inherited)
            {
                row.Children.Add(Ui.Text("←", 12, Ui.Muted));
                row.Children.Add(Ui.Text(label.Text.Length > 0 ? label.Text : string.Join('+', label.Chips), 13, Ui.Muted));
            }
            else
            {
                if (label.Text.Length > 0)
                {
                    row.Children.Add(Ui.Text(label.Text, 13));
                }

                if (label.Chips.Count > 0)
                {
                    row.Children.Add(Ui.Chips(label.Chips, 10.5));
                }
            }
        }

        bool changed = action is not null && !inherited;
        var cell = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            Background = changed ? Ui.MarkSoft : Ui.Brush("RailTransparent"),
            Content = row,
        };
        string appName = process is null ? "All apps" : ProfileOps.AppName(profile);
        AutomationProperties.SetName(cell, $"{info.Name} in {appName}: {plain}{(inherited ? ", same as All apps" : string.Empty)}");
        cell.Click += (_, _) => ShowPicker(cell, info, process, useCurrentApp: false);
        return cell;
    }
}
