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
            block.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
            list.Children.Add(block);
        }
    }

    /// <summary>Name, on/off, then the same Pattern, Strength, Cycles and Test controls as an event row.</summary>
    private Grid CueRow(string title, string? sub, Func<HapticCue> get, Action<HapticCue> set, bool enabled, Action<bool> toggled)
    {
        var grid = CueGrid();
        grid.Padding = new Thickness(0, 8, 0, 4);
        var name = new StackPanel();
        name.Children.Add(Ui.Text(title, 14));
        if (sub is not null) name.Children.Add(Ui.Text(sub, 11, Ui.Muted));
        Place(grid, name, 0);
        Place(grid, PageToggle(title, enabled, toggled), 1);
        AddCueControls(grid, 2, title, get, set, _hapticRefresh);
        return grid;
    }
}
