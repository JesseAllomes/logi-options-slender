using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.System;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    /// <summary>The picker's Excel tab: the built-in Excel actions, then a box for the name of a VBA macro to run through the add-in.</summary>
    private (FrameworkElement Content, FrameworkElement? Focus) ExcelTab(ActionSpec? own, Action<ActionSpec?> commit)
    {
        (FrameworkElement list, FrameworkElement? focus) = OptionList(ActionCatalog.ExcelOptions, own, commit);
        string current = (own as ActionSpec.ExcelMacro)?.Name ?? string.Empty;

        var box = new TextBox { PlaceholderText = "Excel macro, e.g. Book1.xlsm!MyMacro or MyMacro", Text = current, MaxLength = Slender.Engine.ExcelAddin.ExcelAddinProtocol.MaxMacroNameChars + 1 };
        AutomationProperties.SetName(box, "Name of the Excel macro to run");
        var error = Ui.Text(string.Empty, 12, Ui.Muted);
        error.TextWrapping = TextWrapping.Wrap;
        error.Visibility = Visibility.Collapsed;
        var note = Ui.Text(ExcelMacroInput.Note, 12, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryPillStyle"), IsEnabled = false };

        box.TextChanged += (_, _) =>
        {
            (ActionSpec.ExcelMacro? action, string? problem) = ExcelMacroInput.Check(box.Text);
            error.Text = problem ?? string.Empty;
            error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
            save.IsEnabled = action is not null && !action.Equals(own);
        };
        void Save()
        {
            if (ExcelMacroInput.Check(box.Text).Action is { } action)
            {
                commit(action);
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

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(save);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(list);
        panel.Children.Add(box);
        panel.Children.Add(error);
        panel.Children.Add(note);
        panel.Children.Add(row);
        return (panel, focus);
    }
}
