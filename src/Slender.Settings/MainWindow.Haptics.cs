using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>The Haptics page's per-event buzzes, My patterns builder and the named alerts (docs/buzz.md).</summary>
public sealed partial class MainWindow
{
    private readonly List<Action> _hapticRefresh = [];
    private readonly List<Action> _alertRefresh = [];
    private bool _hapticRefreshing;
    private TextBlock? _hapticTestNote;
    private StackPanel? _alertList;

    private const string PatternNote = "Pattern names come from the Solaar project and are not verified on this mouse.";
    private const string PatternTip = "The buzz shape. * = your pattern.";
    private const string LevelTip = "How strong each buzz is. Strong is the strongest the mouse can do.";
    private const string CyclesTip = "How many times the buzz plays";
    private const string FixedCyclesTip = "This pattern sets its own length";
    internal const string HapticFeelTip = "The mouse's own strength between Slender's buzzes. Slender's buzzes use each event's Light / Normal / Strong.";

    private HapticSettings Haptics => _config.Config.Haptics;

    private void BuildHapticEvents(SlenderConfig c)
    {
        _hapticRefresh.Clear();
        AddSection("EVENTS");
        _hapticTestNote = Ui.Text(PatternNote + " Test plays through the running Slender tray. * = your pattern.", 12, Ui.Muted);
        _hapticTestNote.TextWrapping = TextWrapping.Wrap;
        _hapticTestNote.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(_hapticTestNote);

        foreach (HapticEvent e in HapticOps.EventOrder)
        {
            HapticEvent ev = e;
            HapticCue Get() => HapticCues.Configured(Haptics, ev);
            void Edit(HapticCue q) => _config.Update(x => x with { Haptics = HapticOps.WithCue(x.Haptics, ev, q) });

            var grid = CueGrid();
            string label = HapticCues.EventLabel(ev);
            var name = Ui.Text(label, 14);
            name.VerticalAlignment = VerticalAlignment.Center;
            var toggle = PageToggle(label, Get().Enabled, on => { if (!_hapticRefreshing) Edit(Get() with { Enabled = on }); });
            _hapticRefresh.Add(() => toggle.IsOn = Get().Enabled);

            Place(grid, name, 0);
            Place(grid, toggle, 1);
            AddCueControls(grid, 2, label, Get, Edit, _hapticRefresh);
            AddRule(grid);
        }

        BuildMyPatterns();
        BuildNamedAlerts();
    }

    /// <summary>Name, then five columns: on, Pattern, Strength, Cycles, Test (alert rows have Pattern first and Delete last).</summary>
    private static Grid CueGrid()
    {
        var grid = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    /// <summary>The Pattern, Strength, Cycles and Test controls of one cue, from <paramref name="column"/> on. Every cue row uses these.</summary>
    private void AddCueControls(Grid grid, int column, string label, Func<HapticCue> get, Action<HapticCue> set, List<Action> sink)
    {
        Place(grid, CuePatternBox(label, get, set, sink), column);
        Place(grid, CueLevelBox(label, get, set, sink), column + 1);
        Place(grid, CueCyclesBox(label, get, set, sink), column + 2);
        Place(grid, TestButton(label, () => HapticCues.ForTest(Haptics, get())), column + 3);
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

    /// <summary>Fills the box with <paramref name="items"/> (only touching the list when it changed) and selects one.</summary>
    private static void FillBox(ComboBox box, List<string> items, int selected)
    {
        bool same = box.Items.Count == items.Count;
        for (int i = 0; same && i < items.Count; i++) same = (box.Items[i] as string) == items[i];
        if (!same)
        {
            box.Items.Clear();
            foreach (string item in items) box.Items.Add(item);
        }

        box.SelectedIndex = selected;
    }

    private ComboBox CuePatternBox(string name, Func<HapticCue> get, Action<HapticCue> set, List<Action> sink)
    {
        var box = new ComboBox { Width = 170 };
        AutomationProperties.SetName(box, $"{name}: pattern");
        ToolTipService.SetToolTip(box, PatternTip);
        void Fill() => FillBox(box, HapticOps.PatternChoices(Haptics), HapticOps.PatternChoiceIndex(Haptics, get()));
        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (_hapticRefreshing || box.SelectedIndex < 0) return;
            var next = HapticOps.WithPatternChoice(Haptics, get(), box.SelectedIndex);
            if (next == get()) return;
            set(next);
            RefreshHapticRows(); // a saved pattern fixes the cycles, so the Cycles box changes
        };
        sink.Add(Fill);
        return box;
    }

    /// <summary>Light / Normal / Strong = real strength 33 / 66 / 100. A strength set by hand shows as "Custom (N)" until a level is picked.</summary>
    private ComboBox CueLevelBox(string name, Func<HapticCue> get, Action<HapticCue> set, List<Action> sink)
    {
        var box = new ComboBox { Width = 120 };
        AutomationProperties.SetName(box, $"{name}: strength");
        ToolTipService.SetToolTip(box, LevelTip);
        void Fill()
        {
            int index = HapticOps.LevelIndex(get());
            FillBox(box, HapticOps.LevelChoices(get()), index < 0 ? HapticCues.LevelNames.Count : index);
        }

        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (_hapticRefreshing || box.SelectedIndex is < 0 or > 2) return;
            if (HapticCues.StrengthOf(box.SelectedIndex) != get().Strength) set(HapticOps.WithLevel(get(), box.SelectedIndex));
        };
        sink.Add(Fill);
        return box;
    }

    /// <summary>1x / 2x / 3x = how many times the buzz plays. Disabled and "--" when the row uses a saved pattern, which has its own length.</summary>
    private ComboBox CueCyclesBox(string name, Func<HapticCue> get, Action<HapticCue> set, List<Action> sink)
    {
        var box = new ComboBox { Width = 80 };
        AutomationProperties.SetName(box, $"{name}: cycles");
        void Fill()
        {
            bool fixedLength = HapticOps.UsesPattern(Haptics, get());
            box.IsEnabled = !fixedLength;
            ToolTipService.SetToolTip(box, fixedLength ? FixedCyclesTip : CyclesTip);
            FillBox(box, fixedLength ? ["--"] : HapticOps.CyclesChoicesFor(get()), fixedLength ? 0 : HapticOps.CyclesIndex(get()));
        }

        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (_hapticRefreshing || !box.IsEnabled || box.SelectedIndex is < 0 or >= HapticOps.CyclesChoices) return;
            if (box.SelectedIndex != HapticOps.CyclesIndex(get())) set(HapticOps.WithCycles(get(), box.SelectedIndex));
        };
        sink.Add(Fill);
        return box;
    }

    /// <summary>A Test button; <paramref name="cue"/> returns the finished cue (pattern expanded, strength filled).</summary>
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
                note.Text = sent ? PatternNote + " Test plays through the running Slender tray. * = your pattern." : "Slender is not running, so there is nothing to buzz. Start it from the tray first.";
                note.Foreground = sent ? Ui.Muted : Ui.Warn;
            }
        };
        return test;
    }

    /// <summary>Re-reads every cue row after an edit elsewhere on the page (a saved pattern changes the Pattern lists and the Cycles boxes).</summary>
    private void RefreshHapticRows()
    {
        _hapticRefreshing = true;
        try
        {
            foreach (Action refresh in _hapticRefresh.ToArray()) refresh();
            foreach (Action refresh in _alertRefresh.ToArray()) refresh();
        }
        finally { _hapticRefreshing = false; }
    }

    /// <summary>Every list that shows saved patterns, after one was saved or deleted. Notification app rows are rebuilt; the rest are refreshed in place.</summary>
    private void PatternsChanged()
    {
        RefreshHapticRows();
        FillNotifyApps();
    }

    private void BuildMyPatterns()
    {
        AddSection("MY PATTERNS");
        var help = Ui.Text("Build your own buzz, save it, then pick it from any Pattern list above (marked *). Saving under an existing name replaces it.", 12, Ui.Muted);
        help.TextWrapping = TextWrapping.Wrap;
        help.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(help);

        var nameBox = new TextBox { PlaceholderText = "Name, for example ringer", MaxLength = HapticCues.MaxPatternNameLength, Width = 220 };
        AutomationProperties.SetName(nameBox, "Pattern name");
        ToolTipService.SetToolTip(nameBox, "Letters, digits, space, - or _");

        var buzz = new ComboBox { Width = 170 };
        foreach (string n in HapticCues.PatternNames) buzz.Items.Add(n);
        buzz.SelectedIndex = 1;
        AutomationProperties.SetName(buzz, "Pattern: buzz");
        ToolTipService.SetToolTip(buzz, "The built-in buzz this pattern repeats");

        var cycles = new ComboBox { Width = 80 };
        for (int i = 1; i <= HapticCue.PulsesMax; i++) cycles.Items.Add($"{i}x");
        cycles.SelectedIndex = 0;
        AutomationProperties.SetName(cycles, "Pattern: cycles");
        ToolTipService.SetToolTip(cycles, CyclesTip);

        Slider gap = PageSlider("Pattern: gap", HapticCue.SpacingMin, HapticCue.SpacingMax, 10, 150, _ => { }, v => $"{(int)v} ms", out TextBlock gapText);
        ToolTipService.SetToolTip(gap, "Time between buzzes");
        gapText.Width = 70;

        var repeat = new ComboBox { Width = 80 };
        for (int i = 1; i <= HapticCue.RepeatsMax; i++) repeat.Items.Add(i == 1 ? "Once" : $"{i}x");
        repeat.SelectedIndex = 0;
        AutomationProperties.SetName(repeat, "Pattern: repeat");
        ToolTipService.SetToolTip(repeat, "Play the whole group again, e.g. for a phone ringer");

        Slider pause = PageSlider("Pattern: pause between repeats", HapticCue.RepeatGapMin, HapticCue.RepeatGapMax, 100, 1000, _ => { }, v => $"{v / 1000:0.0} s", out TextBlock pauseText);
        ToolTipService.SetToolTip(pause, "Quiet time between repeats");
        pauseText.Width = 70;
        pause.IsEnabled = false;
        repeat.SelectionChanged += (_, _) => pause.IsEnabled = repeat.SelectedIndex > 0;

        HapticPattern Current() => new HapticPattern(Math.Max(buzz.SelectedIndex, 0), Math.Max(cycles.SelectedIndex, 0) + 1, (int)gap.Value,
            Math.Max(repeat.SelectedIndex, 0) + 1, (int)pause.Value).Clamped();

        FrameworkElement Field(string label, string tip, FrameworkElement control)
        {
            var text = Ui.Text(label, 12, Ui.Muted);
            ToolTipService.SetToolTip(text, tip);
            text.VerticalAlignment = VerticalAlignment.Center;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(text);
            line.Children.Add(control);
            control.VerticalAlignment = VerticalAlignment.Center;
            return line;
        }

        FrameworkElement Line(params FrameworkElement[] items)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            foreach (var item in items) line.Children.Add(item);
            return line;
        }

        var list = new StackPanel();
        var error = Ui.Text(string.Empty, 12, Ui.Warn);
        var save = new Button { Content = "Save pattern", Style = Ui.Res<Style>("QuietButtonStyle") };
        save.Click += (_, _) =>
        {
            string? problem = HapticOps.PatternNameProblem(Haptics, nameBox.Text);
            error.Text = problem ?? string.Empty;
            if (problem is not null) return;
            string name = nameBox.Text.Trim();
            HapticPattern pattern = Current();
            _config.Update(x => x with { Haptics = HapticOps.WithPattern(x.Haptics, name, pattern) });
            FillPatterns();
            PatternsChanged();
        };
        var test = TestButton("unsaved pattern", () => HapticCues.ForTest(Haptics, Current().ToCue()));

        var builder = new StackPanel { Spacing = 8, Padding = new Thickness(0, 4, 0, 4) };
        builder.Children.Add(Line(Field("Name", "What this pattern is called in the Pattern lists", nameBox)));
        builder.Children.Add(Line(Field("Buzz", "The built-in buzz this pattern repeats", buzz), Field("Cycles", CyclesTip, cycles), Field("Gap", "Time between buzzes", WithReadout(gap, gapText))));
        builder.Children.Add(Line(Field("Repeat", "Play the whole group again, e.g. for a phone ringer", repeat), Field("Pause", "Quiet time between repeats", WithReadout(pause, pauseText))));
        builder.Children.Add(Line(test, save));
        builder.Children.Add(error);
        PagePanel.Children.Add(builder);

        PagePanel.Children.Add(list);

        void Load(string name, HapticPattern p)
        {
            nameBox.Text = name;
            buzz.SelectedIndex = p.Pattern;
            cycles.SelectedIndex = p.Pulses - 1;
            gap.Value = p.SpacingMs;
            repeat.SelectedIndex = p.Repeats - 1;
            pause.Value = p.RepeatGapMs;
            error.Text = string.Empty;
        }

        void FillPatterns()
        {
            list.Children.Clear();
            foreach (string saved in HapticOps.PatternNamesSorted(Haptics))
            {
                string name = saved;
                HapticPattern Get() => Haptics.Patterns.TryGetValue(name, out var p) ? p : new HapticPattern();
                var grid = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 10 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var title = Ui.Text(name, 13, Ui.Ink, Ui.Mono);
                var summary = Ui.Text(Get().Summary, 12, Ui.Muted);
                var rowTest = TestButton($"pattern {name}", () => HapticCues.ForTest(Haptics, Get().ToCue()));
                var edit = new Button { Content = "Edit", Style = Ui.Res<Style>("QuietButtonStyle") };
                AutomationProperties.SetName(edit, $"Edit pattern {name}");
                edit.Click += (_, _) => Load(name, Get());
                var delete = new Button { Content = "Delete", Style = Ui.Res<Style>("QuietButtonStyle") };
                AutomationProperties.SetName(delete, $"Delete pattern {name}");
                ToolTipService.SetToolTip(delete, "Cues that use it keep playing the same buzz");
                delete.Click += (_, _) =>
                {
                    _config.Update(x => x with { Haptics = HapticOps.WithoutPattern(x.Haptics, name) });
                    FillPatterns();
                    PatternsChanged();
                };
                Place(grid, title, 0);
                Place(grid, summary, 1);
                Place(grid, rowTest, 2);
                Place(grid, edit, 3);
                Place(grid, delete, 4);
                list.Children.Add(grid);
                list.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
            }
        }

        FillPatterns();
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
        _alertRefresh.Clear();
        foreach (string alert in _config.Config.Haptics.Alerts.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = alert;
            HapticCue Get() => _config.Config.Haptics.Alerts.TryGetValue(name, out var c) ? c.Clamped() : new HapticCue();
            void Set(HapticCue q) => _config.Update(x => x with { Haptics = HapticOps.WithAlertCue(x.Haptics, name, q) });

            var grid = CueGrid();
            var title = Ui.Text(name, 13, Ui.Ink, Ui.Mono);
            var delete = new Button { Content = Ui.Glyph("", 12), Style = Ui.Res<Style>("QuietButtonStyle") };
            AutomationProperties.SetName(delete, $"Delete alert {name}");
            delete.Click += (_, _) =>
            {
                _config.Update(x => x with { Haptics = HapticOps.WithoutAlert(x.Haptics, name) });
                FillAlerts();
            };
            Place(grid, title, 0);
            AddCueControls(grid, 1, $"Alert {name}", Get, Set, _alertRefresh);
            Place(grid, delete, 5);
            list.Children.Add(grid);
            list.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.6 });
        }
    }
}
