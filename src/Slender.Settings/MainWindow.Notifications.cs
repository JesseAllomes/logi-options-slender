using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Slender.Sync;

namespace Slender.Settings;

/// <summary>The Haptics page's Notifications menu: buzz the mouse when Windows shows a notification, with a cue per app (m45).</summary>
public sealed partial class MainWindow
{
    private StackPanel? _notifyList;
    private int _notifyRefreshBase;

    private void BuildNotificationHaptics()
    {
        AddSection("NOTIFICATIONS");
        AddRow("Buzz on notifications", "The mouse taps when Windows shows a notification. Off by default; while off Slender does nothing extra.",
            PageToggle("Buzz on notifications", _config.Config.Haptics.Notify.Enabled, on => _config.Update(x => x with { Haptics = NotifyOps.WithEnabled(x.Haptics, on) })));

        var help = Ui.Text("Apps appear below the first time they show a notification while this is on. Do Not Disturb and Focus hide notifications, so nothing buzzes then. "
            + "The tray reads the app name from the popup; if a name cannot be read, that notification uses the default cue.", 12, Ui.Muted);
        help.TextWrapping = TextWrapping.Wrap;
        help.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(help);

        // The default cue for apps that have not been given their own.
        HapticCue Def() => _config.Config.Haptics.Notify.Default.Clamped();
        void SetDef(HapticCue q) => _config.Update(x => x with { Haptics = NotifyOps.WithDefault(x.Haptics, q) });
        AddRule(CueRow("Default for new apps", null, Def, SetDef, Def().Enabled, on => SetDef(Def() with { Enabled = on })));

        _notifyRefreshBase = _hapticRefresh.Count;
        _notifyList = new StackPanel();
        PagePanel.Children.Add(_notifyList);
        FillNotifyApps();

        var refresh = new Button { Content = "Refresh app list", Style = Ui.Res<Style>("QuietButtonStyle"), Margin = new Thickness(0, 8, 0, 0) };
        refresh.Click += (_, _) => FillNotifyApps();
        PagePanel.Children.Add(refresh);
    }

    private void FillNotifyApps()
    {
        if (_notifyList is not { } list) return;
        list.Children.Clear();
        if (_hapticRefresh.Count > _notifyRefreshBase) _hapticRefresh.RemoveRange(_notifyRefreshBase, _hapticRefresh.Count - _notifyRefreshBase); // rows rebuilt below
        var rows = NotifyOps.Rows(SeenAppsStore.Read(SyncPaths.StateDirectory), _config.Config.Haptics.Notify);
        if (rows.Count == 0)
        {
            var none = Ui.Text("No apps yet. Turn the switch on and the next notification adds its app here.", 12, Ui.Muted);
            none.Margin = new Thickness(0, 8, 0, 0);
            list.Children.Add(none);
            return;
        }

        foreach (var row in rows)
        {
            string app = row.Name;
            HapticCue Get() => _config.Config.Haptics.Notify.CueFor(app);
            void Set(HapticCue q) => _config.Update(x => x with { Haptics = NotifyOps.WithAppCue(x.Haptics, app, q) });
            var block = new StackPanel();
            block.Children.Add(CueRow(app, NotifyOps.SeenText(row, DateTimeOffset.UtcNow), Get, Set, Get().Enabled, on => Set(Get() with { Enabled = on })));
            block.Children.Add(NotifyAdvanced(app, Get, Set));
            block.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
            list.Children.Add(block);
        }
    }

    /// <summary>Name, on/off, pattern, strength, test: the same controls as an event row.</summary>
    private Grid CueRow(string title, string? sub, Func<HapticCue> get, Action<HapticCue> set, bool enabled, Action<bool> toggled)
    {
        var grid = new Grid { Padding = new Thickness(0, 8, 0, 4), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel();
        name.Children.Add(Ui.Text(title, 14));
        if (sub is not null) name.Children.Add(Ui.Text(sub, 11, Ui.Muted));
        Place(grid, name, 0);
        Place(grid, PageToggle(title, enabled, toggled), 1);
        Place(grid, CuePatternBox(title, get, set), 2);
        Place(grid, CueIntensityBox(title, get, set), 3);
        Place(grid, TestButton(title, () => get() with { Enabled = true }), 4);
        return grid;
    }

    /// <summary>Pulses, gap and exact strength for one app; collapsed by default like the Advanced section above.</summary>
    private Expander NotifyAdvanced(string app, Func<HapticCue> get, Action<HapticCue> set)
    {
        var pulses = new ComboBox { Width = 70 };
        for (int i = 1; i <= HapticCue.PulsesMax; i++) pulses.Items.Add(i.ToString());
        pulses.SelectedIndex = get().Pulses - 1;
        AutomationProperties.SetName(pulses, $"{app}: pulses");
        pulses.SelectionChanged += (_, _) =>
        {
            if (pulses.SelectedIndex >= 0 && pulses.SelectedIndex + 1 != get().Pulses) set(get() with { Pulses = pulses.SelectedIndex + 1 });
        };
        Slider gap = PageSlider($"{app}: gap", HapticCue.SpacingMin, HapticCue.SpacingMax, 10, get().SpacingMs,
            v => { if ((int)v != get().SpacingMs) set(get() with { SpacingMs = (int)v }); }, v => $"{(int)v} ms", out TextBlock gapText);
        gapText.Width = 70;
        Slider exact = PageSlider($"{app}: exact strength", 0, 100, 5, get().Strength ?? _config.Config.Haptics.EffectiveStrength,
            v => { if (get().Strength is { } s && (int)v != s) set(get() with { Strength = (int)v }); }, v => $"{(int)v}", out TextBlock exactText);
        exact.IsEnabled = get().Strength is not null;
        var exactToggle = PageToggle($"{app}: exact strength", get().Strength is not null, on =>
        {
            exact.IsEnabled = on;
            set(get() with { Strength = on ? (int)exact.Value : null });
        });

        var line1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        line1.Children.Add(Ui.Text("Pulses", 12, Ui.Muted));
        line1.Children.Add(pulses);
        line1.Children.Add(Ui.Text("Gap", 12, Ui.Muted));
        line1.Children.Add(WithReadout(gap, gapText));
        var line2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        line2.Children.Add(Ui.Text("Exact strength", 12, Ui.Muted));
        line2.Children.Add(exactToggle);
        line2.Children.Add(WithReadout(exact, exactText));
        foreach (var line in new[] { line1, line2 })
            foreach (UIElement child in line.Children) if (child is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;

        var inner = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        inner.Children.Add(line1);
        inner.Children.Add(line2);
        return new Expander { Header = "Advanced", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = inner };
    }
}
