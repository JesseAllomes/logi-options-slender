using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// The thumb wheel card: clicking the thumb wheel on the device stage opens this instead of the plain picker. One place, for the
/// app chosen in the strip, for everything the wheel does: what scrolling sideways does (opens the normal picker), the second
/// action on/off, what a notch right and a notch left do in that second role, and which button flips between the two.
/// </summary>
public sealed partial class MainWindow
{
    private const double ThumbCardWidth = 316;

    private Flyout? _thumbCard;
    private StackPanel? _thumbCardBody;
    private ControlInfo? _thumbCardInfo;

    private void ShowThumbCard(FrameworkElement anchor, ControlInfo info)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = Ui.Res<Style>("PickerFlyoutPresenterStyle"),
        };
        var root = new StackPanel { Spacing = 6, Width = ThumbCardWidth };
        var header = Ui.Text(string.Empty, 12, Ui.Muted);
        root.Children.Add(header);
        var body = new StackPanel();
        root.Children.Add(body);
        flyout.Content = root;

        _thumbCard = flyout;
        _thumbCardBody = body;
        _thumbCardInfo = info;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_thumbCard, flyout))
            {
                _thumbCard = null;
                _thumbCardBody = null;
                _thumbCardInfo = null;
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

        // What the wheel does normally: the stage label's text, and the same picker the label used to open.
        var scroll = new Button { Content = PlainActionText(info), Style = Ui.Res<Style>("QuietButtonStyle"), MaxWidth = 170 };
        AutomationProperties.SetName(scroll, $"Scroll sideways in {name}: {PlainActionText(info)}");
        scroll.Click += (_, _) => ShowPicker(scroll, info);
        CardRow("Scroll sideways", null, scroll, body);

        ThumbWheelView view = ThumbWheelOps.View(_config.Config, app);
        CardRow("Second action", "Flip the wheel to a second job with a button of your choice.",
            PageToggle("Second action in " + name, view.On, on => _config.Update(x => ThumbWheelOps.SetEnabled(x, app, on))), body);
        if (!view.On || view.Second is not { } second)
        {
            return;
        }

        bool own = !view.Inherited;
        CardRow("Scroll right", null, NotchButton("Scroll right", second.Forward, own, name,
            a => _config.Update(x => ThumbWheelOps.SetForward(x, app, a))), body);
        CardRow("Scroll left", null, NotchButton("Scroll left", second.Back, own, name,
            a => _config.Update(x => ThumbWheelOps.SetBack(x, app, a))), body);

        (ushort? switchId, bool switchInherited) = ThumbWheelOps.SwitchControl(_config.Config, app);
        CardRow("Switch with", switchInherited ? "Same as All apps" : null, SwitchCombo(switchId, name, app), body);

        if (ThumbWheelOps.InheritedText(view) is { } inherited)
        {
            var customise = new Button { Content = "Customise", Style = Ui.Res<Style>("QuietButtonStyle") };
            AutomationProperties.SetName(customise, "Customise the second action in " + name);
            customise.Click += (_, _) => _config.Update(x => ThumbWheelOps.Customise(x, app));
            CardRow(inherited, null, customise, body);
        }
    }

    /// <summary>"Switch with": None or any mouse control except the wheel itself; picking one moves "Switch thumb wheel mode" there.</summary>
    private ComboBox SwitchCombo(ushort? current, string appName, string? app)
    {
        IReadOnlyList<ControlInfo> candidates = ThumbWheelOps.SwitchCandidates;
        var combo = new ComboBox { MinWidth = 150 };
        combo.Items.Add("None");
        foreach (ControlInfo c in candidates)
        {
            combo.Items.Add(c.Name);
        }

        int index = current is { } id ? candidates.ToList().FindIndex(c => c.Id == id) : -1;
        combo.SelectedIndex = index + 1;
        AutomationProperties.SetName(combo, "Switch the thumb wheel in " + appName + " with");
        combo.SelectionChanged += (_, _) =>
        {
            int picked = combo.SelectedIndex - 1;
            ushort? target = picked >= 0 && picked < candidates.Count ? candidates[picked].Id : null;
            if (target != current)
            {
                _config.Update(x => ThumbWheelOps.SetSwitchControl(x, app, target));
            }
        };
        return combo;
    }

    /// <summary>A tight row for the card: title (and muted help) on the left, the control on the right, no rule.</summary>
    private static void CardRow(string title, string? help, FrameworkElement control, Panel into)
    {
        var grid = new Grid { Padding = new Thickness(0, 7, 0, 7), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(title, 13));
        if (help is not null)
        {
            var h = Ui.Text(help, 11.5, Ui.Muted);
            h.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(h);
        }

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        into.Children.Add(grid);
    }
}
