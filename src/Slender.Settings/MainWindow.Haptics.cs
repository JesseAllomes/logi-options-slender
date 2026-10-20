using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>The Haptics page's per-event buzzes, the Advanced expander and the named alerts (docs/buzz.md).</summary>
public sealed partial class MainWindow
{
    private readonly List<Action> _hapticRefresh = [];
    private bool _hapticRefreshing;
    private TextBlock? _hapticTestNote;
    private StackPanel? _alertList;

    private const string PatternNote = "Pattern names come from the Solaar project and are not verified on this mouse.";

    private void BuildHapticEvents(SlenderConfig c)
    {
        _hapticRefresh.Clear();
        AddSection("EVENTS");
        _hapticTestNote = Ui.Text(PatternNote + " Test plays through the running Slender tray.", 12, Ui.Muted);
        _hapticTestNote.TextWrapping = TextWrapping.Wrap;
        _hapticTestNote.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(_hapticTestNote);

        foreach (HapticEvent e in HapticOps.EventOrder)
        {
            HapticEvent ev = e;
            HapticCue Get() => HapticCues.Resolve(_config.Config.Haptics, ev);
            void Edit(Func<HapticCue, HapticCue> f) => _config.Update(x => x with { Haptics = HapticOps.WithCue(x.Haptics, ev, f(HapticCues.Resolve(x.Haptics, ev))) });

            var grid = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string label = HapticCues.EventLabel(ev);
            var name = Ui.Text(label, 14);
            name.VerticalAlignment = VerticalAlignment.Center;
            var toggle = PageToggle(label, Get().Enabled, on => { if (!_hapticRefreshing) Edit(q => q with { Enabled = on }); });
            var pattern = CuePatternBox(label, Get, q => Edit(_ => q));
            var intensity = CueIntensityBox(label, Get, q => Edit(_ => q));
            var test = TestButton(label, () => HapticCues.Resolve(_config.Config.Haptics, ev));
            _hapticRefresh.Add(() => toggle.IsOn = Get().Enabled);

            Place(grid, name, 0);
            Place(grid, toggle, 1);
            Place(grid, pattern, 2);
            Place(grid, intensity, 3);
            Place(grid, test, 4);
            AddRule(grid);
        }

        BuildHapticAdvanced();
        BuildNamedAlerts();
    }

    private static void Place(Grid grid, FrameworkElement element, int column)
    {
        element.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private void AddRule(FrameworkElement row)
    {
        var wrap = new StackPanel();
        wrap.Children.Add(row);
        wrap.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
        PagePanel.Children.Add(wrap);
    }

    private ComboBox CuePatternBox(string name, Func<HapticCue> get, Action<HapticCue> set)
    {
        var box = new ComboBox { Width = 160 };
        foreach (string n in HapticCues.PatternNames) box.Items.Add(n);
        box.SelectedIndex = get().Pattern;
        AutomationProperties.SetName(box, $"{name}: pattern");
        box.SelectionChanged += (_, _) =>
        {
            if (!_hapticRefreshing && box.SelectedIndex >= 0 && box.SelectedIndex != get().Pattern) set(get() with { Pattern = box.SelectedIndex });
        };
        _hapticRefresh.Add(() => box.SelectedIndex = get().Pattern);
        return box;
    }

    /// <summary>Light / Normal / Strong = 1 / 2 / 3 pulses. A cue edited by hand to more pulses shows an extra "Custom" choice.</summary>
    private ComboBox CueIntensityBox(string name, Func<HapticCue> get, Action<HapticCue> set)
    {
        var box = new ComboBox { Width = 100 };
        AutomationProperties.SetName(box, $"{name}: strength");
        void Fill()
        {
            box.Items.Clear();
            foreach (string n in HapticOps.IntensityNames) box.Items.Add(n);
            int index = HapticOps.IntensityIndex(get());
            if (index < 0) box.Items.Add("Custom");
            box.SelectedIndex = index < 0 ? 3 : index;
        }

        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (_hapticRefreshing || box.SelectedIndex is < 0 or > 2) return;
            if (box.SelectedIndex != HapticOps.IntensityIndex(get())) set(HapticOps.WithIntensity(get(), box.SelectedIndex));
        };
        _hapticRefresh.Add(Fill);
        return box;
    }

    private Button TestButton(string name, Func<HapticCue> cue)
    {
        var test = new Button { Content = "Test", Style = Ui.Res<Style>("QuietButtonStyle") };
        AutomationProperties.SetName(test, $"Test {name}");
        test.Click += async (_, _) =>
        {
            string line = BuzzProtocol.TestLine(cue());
            bool sent = await Task.Run(() => BuzzProtocol.TrySend(line));
            if (_hapticTestNote is { } note)
            {
                note.Text = sent ? PatternNote + " Test plays through the running Slender tray." : "Slender is not running, so there is nothing to buzz. Start it from the tray first.";
                note.Foreground = sent ? Ui.Muted : Ui.Warn;
            }
        };
        return test;
    }

    /// <summary>Re-reads every row after an edit elsewhere on the page (pulses in Advanced change the strength choice).</summary>
    private void RefreshHapticRows()
    {
        _hapticRefreshing = true;
        try { foreach (Action refresh in _hapticRefresh.ToArray()) refresh(); }
        finally { _hapticRefreshing = false; }
    }

    /// <summary>
    /// Repeat the pulse group (e.g. a phone ringer: 6 taps, 1 s quiet, 6 taps): how many groups and the quiet between them.
    /// <paramref name="refresh"/> puts the controls back to the cue's values without writing.
    /// </summary>
    private StackPanel CueRepeatLine(string label, Func<HapticCue> get, Action<HapticCue> set, out Action refresh)
    {
        bool loading = false;
        var repeats = new ComboBox { Width = 70 };
        for (int i = 1; i <= HapticCue.RepeatsMax; i++) repeats.Items.Add(i == 1 ? "Once" : $"{i}x");
        repeats.SelectedIndex = get().Repeats - 1;
        AutomationProperties.SetName(repeats, $"{label}: repeat");
        Slider pause = PageSlider($"{label}: pause between repeats", HapticCue.RepeatGapMin, HapticCue.RepeatGapMax, 100, get().RepeatGapMs,
            v => { if (!loading && (int)v != get().RepeatGapMs) set(get() with { RepeatGapMs = (int)v }); }, v => $"{v / 1000:0.0} s", out TextBlock pauseText);
        pauseText.Width = 70;
        pause.IsEnabled = get().Repeats > 1;
        repeats.SelectionChanged += (_, _) =>
        {
            if (loading || repeats.SelectedIndex < 0 || repeats.SelectedIndex + 1 == get().Repeats) return;
            pause.IsEnabled = repeats.SelectedIndex > 0;
            set(get() with { Repeats = repeats.SelectedIndex + 1 });
        };
        refresh = () =>
        {
            loading = true;
            try
            {
                HapticCue q = get();
                repeats.SelectedIndex = q.Repeats - 1;
                pause.Value = q.RepeatGapMs;
                pause.IsEnabled = q.Repeats > 1;
            }
            finally { loading = false; }
        };

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        line.Children.Add(Ui.Text("Repeat", 12, Ui.Muted));
        line.Children.Add(repeats);
        line.Children.Add(Ui.Text("Pause", 12, Ui.Muted));
        line.Children.Add(WithReadout(pause, pauseText));
        foreach (UIElement child in line.Children) if (child is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
        return line;
    }

    private void BuildHapticAdvanced()
    {
        var inner = new StackPanel { Spacing = 14, Padding = new Thickness(4, 6, 4, 6) };
        foreach (HapticEvent e in HapticOps.EventOrder)
        {
            HapticEvent ev = e;
            HapticCue Get() => HapticCues.Resolve(_config.Config.Haptics, ev);
            void Edit(Func<HapticCue, HapticCue> f)
            {
                _config.Update(x => x with { Haptics = HapticOps.WithCue(x.Haptics, ev, f(HapticCues.Resolve(x.Haptics, ev))) });
                RefreshHapticRows();
            }

            string label = HapticCues.EventLabel(ev);
            var block = new StackPanel { Spacing = 6 };
            block.Children.Add(Ui.Text(label, 13));

            var pulses = new ComboBox { Width = 70 };
            for (int i = 1; i <= HapticCue.PulsesMax; i++) pulses.Items.Add(i.ToString());
            pulses.SelectedIndex = Get().Pulses - 1;
            AutomationProperties.SetName(pulses, $"{label}: pulses");
            pulses.SelectionChanged += (_, _) =>
            {
                if (!_hapticRefreshing && pulses.SelectedIndex >= 0 && pulses.SelectedIndex + 1 != Get().Pulses) Edit(q => q with { Pulses = pulses.SelectedIndex + 1 });
            };
            Slider spacing = PageSlider($"{label}: spacing", HapticCue.SpacingMin, HapticCue.SpacingMax, 10, Get().SpacingMs,
                v => { if (!_hapticRefreshing) Edit(q => q with { SpacingMs = (int)v }); }, v => $"{(int)v} ms", out TextBlock spacingText);
            spacingText.Width = 70;

            var line1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            line1.Children.Add(Ui.Text("Pulses", 12, Ui.Muted));
            line1.Children.Add(pulses);
            line1.Children.Add(Ui.Text("Gap", 12, Ui.Muted));
            line1.Children.Add(WithReadout(spacing, spacingText));
            foreach (UIElement child in line1.Children) if (child is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
            block.Children.Add(line1);
            block.Children.Add(CueRepeatLine(label, Get, q => Edit(_ => q), out Action refreshRepeat));

            var note = Ui.Text("Writes the mouse's stored strength for this buzz and puts it back.", 12, Ui.Muted);
            note.TextWrapping = TextWrapping.Wrap;
            Slider exact = PageSlider($"{label}: exact strength", 0, 100, 5, Get().Strength ?? _config.Config.Haptics.EffectiveStrength,
                v => { if (!_hapticRefreshing && Get().Strength is not null) Edit(q => q with { Strength = (int)v }); }, v => $"{(int)v}", out TextBlock exactText);
            var exactToggle = PageToggle($"{label}: exact strength", Get().Strength is not null, on =>
            {
                if (_hapticRefreshing) return;
                exact.IsEnabled = on;
                note.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                Edit(q => q with { Strength = on ? (int)exact.Value : null });
            });
            exact.IsEnabled = Get().Strength is not null;
            note.Visibility = Get().Strength is not null ? Visibility.Visible : Visibility.Collapsed;
            var line2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            line2.Children.Add(Ui.Text("Exact strength", 12, Ui.Muted));
            line2.Children.Add(exactToggle);
            line2.Children.Add(WithReadout(exact, exactText));
            foreach (UIElement child in line2.Children) if (child is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center;
            block.Children.Add(line2);
            block.Children.Add(note);

            _hapticRefresh.Add(() =>
            {
                HapticCue q = Get();
                pulses.SelectedIndex = q.Pulses - 1;
                spacing.Value = q.SpacingMs;
                refreshRepeat();
                exactToggle.IsOn = q.Strength is not null;
                exact.IsEnabled = q.Strength is not null;
                note.Visibility = q.Strength is not null ? Visibility.Visible : Visibility.Collapsed;
                if (q.Strength is { } s) exact.Value = s;
            });
            inner.Children.Add(block);
        }

        var expander = new Expander
        {
            Header = "Advanced",
            IsExpanded = _ui.HapticsAdvancedOpen,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = inner,
            Margin = new Thickness(0, 12, 0, 0),
        };
        expander.Expanding += (_, _) => { _ui.HapticsAdvancedOpen = true; SaveUiNow(); };
        expander.Collapsed += (_, _) => { _ui.HapticsAdvancedOpen = false; SaveUiNow(); };
        PagePanel.Children.Add(expander);
    }

    private void BuildNamedAlerts()
    {
        AddSection("NAMED ALERTS");
        var help = Ui.Text("Other programs buzz the mouse by name: a build script, a Claude Code hook, an Excel macro. An unknown name plays \"Alert (no name)\".", 12, Ui.Muted);
        help.TextWrapping = TextWrapping.Wrap;
        help.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(help);

        _alertList = new StackPanel();
        PagePanel.Children.Add(_alertList);
        FillAlerts();

        var add = new Grid { Padding = new Thickness(0, 10, 0, 4), ColumnSpacing = 10 };
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { PlaceholderText = "Name, for example build", MaxLength = 32 };
        AutomationProperties.SetName(box, "New alert name");
        var addButton = new Button { Content = "+ Add alert", Style = Ui.Res<Style>("QuietButtonStyle") };
        var error = Ui.Text(string.Empty, 12, Ui.Warn);
        void TryAdd()
        {
            string? problem = HapticOps.AlertNameProblem(_config.Config.Haptics, box.Text);
            error.Text = problem ?? string.Empty;
            if (problem is not null) return;
            string name = box.Text.Trim();
            _config.Update(x => x with { Haptics = HapticOps.WithNewAlert(x.Haptics, name) });
            box.Text = string.Empty;
            FillAlerts();
        }

        addButton.Click += (_, _) => TryAdd();
        box.KeyDown += (_, k) => { if (k.Key == Windows.System.VirtualKey.Enter) TryAdd(); };
        Place(add, box, 0);
        Place(add, addButton, 1);
        PagePanel.Children.Add(add);
        PagePanel.Children.Add(error);

        var command = Ui.Text("Run  " + HapticOps.Command("<name>") + "  from any script. With no name it plays \"Alert (no name)\".", 12, Ui.Muted, Ui.Mono);
        command.TextWrapping = TextWrapping.Wrap;
        command.Margin = new Thickness(0, 6, 0, 0);
        PagePanel.Children.Add(command);
    }

    private void FillAlerts()
    {
        if (_alertList is not { } list) return;
        list.Children.Clear();
        foreach (string alert in _config.Config.Haptics.Alerts.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = alert;
            HapticCue Get() => _config.Config.Haptics.Alerts.TryGetValue(name, out var c) ? c.Clamped() : new HapticCue();
            void Set(HapticCue q) => _config.Update(x => x with { Haptics = HapticOps.WithAlertCue(x.Haptics, name, q) });

            var grid = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Ui.Text(name, 13, Ui.Ink, Ui.Mono);
            var pattern = CuePatternBox($"Alert {name}", Get, Set);
            var intensity = CueIntensityBox($"Alert {name}", Get, Set);
            var test = TestButton($"alert {name}", () => Get() with { Enabled = true });
            var delete = new Button { Content = Ui.Glyph("", 12), Style = Ui.Res<Style>("QuietButtonStyle") };
            AutomationProperties.SetName(delete, $"Delete alert {name}");
            delete.Click += (_, _) =>
            {
                _config.Update(x => x with { Haptics = HapticOps.WithoutAlert(x.Haptics, name) });
                FillAlerts();
            };
            Place(grid, title, 0);
            Place(grid, pattern, 1);
            Place(grid, intensity, 2);
            Place(grid, test, 3);
            Place(grid, delete, 4);
            list.Children.Add(grid);
            list.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
        }
    }
}
