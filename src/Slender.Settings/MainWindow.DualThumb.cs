using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// "Dual thumb wheel" on the Point &amp; scroll page, for the app chosen in the strip: an on/off toggle and, when on, what one notch
/// right and one notch left do in the second role. The role is switched with "Switch thumb wheel mode" on any control; the
/// thumb wheel card on the device stage (MainWindow.ThumbCard.cs) edits the same settings.
/// </summary>
public sealed partial class MainWindow
{
    private StackPanel? _dualHost;

    private void BuildDualThumbSection()
    {
        AddSection("DUAL THUMB WHEEL · " + (_app is null ? "ALL APPS" : AppDisplayName(_app).ToUpperInvariant()));
        _dualHost = new StackPanel();
        PagePanel.Children.Add(_dualHost);
        RefreshDualThumb();
    }

    private string AppDisplayName(string app) => ProfileOps.Find(_config.Config, app) is { } p ? ProfileOps.AppName(p) : app;

    /// <summary>Rebuilds only this section, so a config change never rebuilds (and resets) the sliders above it.</summary>
    private void RefreshDualThumb()
    {
        if (_dualHost is null || _page != Page.Pointer)
        {
            return;
        }

        _dualHost.Children.Clear();
        string? app = _app;
        string name = app is null ? "All apps" : AppDisplayName(app);
        ThumbWheelView view = ThumbWheelOps.View(_config.Config, app);

        AddRow("Dual thumb wheel", "Give the thumb wheel a second job. Put \"Switch thumb wheel mode\" on a button, such as the top one behind the scroll wheel, to flip between the two.",
            PageToggle("Dual thumb wheel in " + name, view.On, on => _config.Update(x => ThumbWheelOps.SetEnabled(x, app, on))), _dualHost);
        if (!view.On || view.Second is not { } second)
        {
            return;
        }

        if (ThumbWheelOps.InheritedText(view) is { } inherited)
        {
            var customise = new Button { Content = "Customise", Style = Ui.Res<Style>("QuietButtonStyle") };
            AutomationProperties.SetName(customise, "Customise the dual thumb wheel in " + name);
            customise.Click += (_, _) => _config.Update(x => ThumbWheelOps.Customise(x, app));
            AddRow(inherited, null, customise, _dualHost);
        }

        bool own = !view.Inherited;
        AddRow("Scroll right", null, NotchButton("Scroll right", second.Forward, own, name,
            a => _config.Update(x => ThumbWheelOps.SetForward(x, app, a))), _dualHost);
        AddRow("Scroll left", null, NotchButton("Scroll left", second.Back, own, name,
            a => _config.Update(x => ThumbWheelOps.SetBack(x, app, a))), _dualHost);

        if (app is not null)
        {
            var off = new Button { Content = "Turn off for " + name, Style = Ui.Res<Style>("QuietButtonStyle") };
            off.Click += (_, _) => _config.Update(x => ThumbWheelOps.TurnOffFor(x, app));
            AddRow("Not wanted in " + name + "?", null, off, _dualHost);
        }
    }

    /// <summary>A button showing what the notch does; it opens the action picker. Muted and inert while the role is inherited.</summary>
    private Button NotchButton(string what, ActionSpec action, bool enabled, string appName, Action<ActionSpec> set)
    {
        var button = new Button
        {
            Content = ThumbWheelOps.Text(action),
            Style = Ui.Res<Style>("QuietButtonStyle"),
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.6,
        };
        AutomationProperties.SetName(button, $"{what} in {appName}: {ThumbWheelOps.Text(action)}");
        ActionSpec? own = action is ActionSpec.None ? null : action;
        string initial = own switch
        {
            ActionSpec.MouseButton or ActionSpec.ToggleThumbWheelMode => "mouse",
            ActionSpec.Excel => "excel",
            ActionSpec.ActionsRing => "ring",
            ActionSpec.Launch or ActionSpec.Disabled or ActionSpec.Brightness or ActionSpec.Text => "more",
            ActionSpec.Keys => "keys",
            _ => "default",
        };
        button.Click += (_, _) => ShowPickerCore(button, new PickerSpec($"{what} in {appName}", MouseTabs, initial, own, "Do nothing", false,
            picked => set(picked ?? new ActionSpec.None())));
        return button;
    }
}
