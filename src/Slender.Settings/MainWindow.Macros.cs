using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Engine.Macros;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    /// <summary>
    /// The picker's "Macro" section (More tab): choose one of the saved macros, edit it, or make a new one. "New macro" opens the
    /// editor and binds the control to the macro once it is saved.
    /// </summary>
    private FrameworkElement MacroSection(ActionSpec.Macro? current, Action<ActionSpec?> commit)
    {
        var inner = new StackPanel { Spacing = 8 };
        IReadOnlyList<MacroDef> macros = _config.Config.Macros;

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = macros.Count == 0 ? "No macros yet" : "Choose a macro" };
        foreach (MacroDef m in macros)
        {
            combo.Items.Add(m.Name);
        }

        int at = current is null ? -1 : macros.ToList().FindIndex(m => m.Id == current.Id);
        combo.SelectedIndex = at;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, "Macro");
        var summary = Ui.Text(string.Empty, 12, Ui.Muted);
        summary.TextWrapping = TextWrapping.Wrap;

        var use = new Button { Content = "Use this macro", Style = Ui.Res<Style>("PrimaryPillStyle"), IsEnabled = false };
        var edit = new Button { Content = "Edit...", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = at >= 0 };
        var create = new Button { Content = "New macro...", Style = Ui.Res<Style>("QuietButtonStyle") };

        MacroDef? Chosen() => combo.SelectedIndex >= 0 && combo.SelectedIndex < macros.Count ? macros[combo.SelectedIndex] : null;
        void Refresh()
        {
            MacroDef? m = Chosen();
            summary.Text = m is null ? string.Empty : MacroOps.Summary(m);
            edit.IsEnabled = m is not null;
            use.IsEnabled = m is not null && current?.Id != m.Id;
        }

        combo.SelectionChanged += (_, _) => Refresh();
        use.Click += (_, _) =>
        {
            if (Chosen() is { } m)
            {
                commit(new ActionSpec.Macro(m.Id));
            }
        };
        edit.Click += (_, _) =>
        {
            if (Chosen() is { } m)
            {
                MacroEditorWindow.Show(_config, m, isNew: false);
            }
        };
        create.Click += (_, _) =>
        {
            MacroDef fresh = MacroOps.NewMacro(_config.Config);
            MacroEditorWindow.Show(_config, fresh, isNew: true, saved: m => commit(new ActionSpec.Macro(m.Id)));
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(create);
        row.Children.Add(edit);
        row.Children.Add(use);
        inner.Children.Add(combo);
        inner.Children.Add(summary);
        inner.Children.Add(row);
        Refresh();

        return new Expander
        {
            Header = "Macro",
            IsExpanded = current is not null,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = inner,
        };
    }
}
