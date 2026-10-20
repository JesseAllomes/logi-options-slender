using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Engine.Macros;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;
using Windows.Graphics;

namespace Slender.Settings;

/// <summary>
/// The macro editor: a name, how it runs, the steps (add, edit, delete, reorder, adjustable pauses) and a recorder. The recorder
/// listens to the keys and mouse on its own pad inside this window, only while Record is on and this window is in front; it
/// reads input events and never sends any. Nothing is saved until Save.
/// </summary>
public sealed class MacroEditorWindow : Window
{
    private static readonly Dictionary<string, MacroEditorWindow> Open = new();
    private static readonly int[] PausePresets = [100, 250, 500, 1000, 5000];

    private readonly ConfigService _config;
    private readonly Action<MacroDef>? _saved;
    private readonly bool _isNew;
    private readonly string _id;
    private MacroDef _macro;
    private readonly ListView _list = new();
    private readonly StackPanel _edit = new() { Spacing = 8 };
    private readonly TextBlock _summary;
    private readonly TextBlock _recordStatus;
    private readonly Button _record;
    private readonly Border _pad;
    private readonly TextBlock _padText;
    private readonly CheckBox _recordMoves;
    private readonly MacroRecorderModel _recorder = new();
    private readonly Button _deleteMacro;
    private readonly TextBlock _deleteNote = Ui.Text(string.Empty, 12, Ui.Muted);
    private bool _recording;
    private bool _building;
    private bool _confirmDelete;
    private int _insertAt = -1;

    /// <summary>Opens the editor for a macro (one window per macro). <paramref name="saved"/> runs after a Save with the saved macro.</summary>
    public static void Show(ConfigService config, MacroDef macro, bool isNew, Action<MacroDef>? saved = null)
    {
        if (Open.TryGetValue(macro.Id, out MacroEditorWindow? existing))
        {
            existing.Activate();
            return;
        }

        var window = new MacroEditorWindow(config, macro, isNew, saved);
        Open[macro.Id] = window;
        window.Closed += (_, _) => Open.Remove(macro.Id);
        window.Activate();
    }

    private MacroEditorWindow(ConfigService config, MacroDef macro, bool isNew, Action<MacroDef>? saved)
    {
        _config = config;
        _macro = macro;
        _isNew = isNew;
        _saved = saved;
        _id = macro.Id;
        Title = "Macro";
        SystemBackdrop = new MicaBackdrop();

        var root = new StackPanel { Spacing = 12, Padding = new Thickness(18) };
        root.Children.Add(Ui.Text(isNew ? "New macro" : "Edit macro", 18, null, Ui.Heading));

        // Name, how it runs, safety cap.
        var name = new TextBox { Text = macro.Name, MaxLength = MacroDef.NameMax, Header = "Name" };
        name.TextChanged += (_, _) => _macro = _macro with { Name = name.Text };
        root.Children.Add(name);

        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Header = "When the button is pressed" };
        foreach (MacroRunMode m in Enum.GetValues<MacroRunMode>())
        {
            mode.Items.Add(MacroOps.ModeName(m));
        }

        mode.SelectedIndex = (int)macro.Mode;
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex >= 0)
            {
                _macro = _macro with { Mode = (MacroRunMode)mode.SelectedIndex };
                Paint();
            }
        };
        root.Children.Add(mode);

        var cap = Number("Stops by itself after (seconds)", macro.CapSeconds, MacroDef.CapMinSeconds, MacroDef.CapMaxSeconds, v => _macro = _macro with { CapSeconds = v });
        root.Children.Add(cap);
        var safety = Ui.Text("Press the button again or Esc to stop a running macro. Whatever it holds down is let go.", 12, Ui.Muted);
        safety.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(safety);

        // Steps.
        root.Children.Add(Ui.Rule1());
        _summary = Ui.Text(string.Empty, 12, Ui.Muted);
        root.Children.Add(Ui.Text("Steps", 13.5, null, Ui.Heading));
        root.Children.Add(_summary);

        _list.SelectionMode = ListViewSelectionMode.Single;
        _list.CanReorderItems = true;
        _list.CanDragItems = true;
        _list.AllowDrop = true;
        _list.MaxHeight = 250;
        _list.MinHeight = 90;
        _list.SelectionChanged += (_, _) => { if (!_building) { ShowEdit(); } };
        _list.DragItemsCompleted += (_, _) => TakeOrderFromList();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_list, "Macro steps. Drag to reorder.");
        root.Children.Add(_list);
        root.Children.Add(Toolbar());
        root.Children.Add(_edit);

        // Recorder.
        root.Children.Add(Ui.Rule1());
        root.Children.Add(Ui.Text("Record", 13.5, null, Ui.Heading));
        var how = Ui.Text("Press Record, then use the keys and mouse on the pad. Your real gaps become pauses. Nothing is sent while recording, and it stops if you leave this window.", 12, Ui.Muted);
        how.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(how);
        _recordMoves = new CheckBox { Content = "Record pointer movement", IsChecked = true };
        root.Children.Add(_recordMoves);
        _record = new Button { Content = "Record", Style = Ui.Res<Style>("QuietButtonStyle") };
        _record.Click += (_, _) => { if (_recording) { StopRecording(); } else { StartRecording(); } };
        _recordStatus = Ui.Text(string.Empty, 12, Ui.Muted);
        _recordStatus.TextWrapping = TextWrapping.Wrap;
        var recordRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        recordRow.Children.Add(_record);
        recordRow.Children.Add(_recordStatus);
        root.Children.Add(recordRow);

        _padText = Ui.Text("Press Record to start", 13, Ui.Muted);
        _padText.HorizontalAlignment = HorizontalAlignment.Center;
        _padText.VerticalAlignment = VerticalAlignment.Center;
        _pad = new Border
        {
            Height = 96,
            CornerRadius = new CornerRadius(6),
            BorderBrush = Ui.Rule,
            BorderThickness = new Thickness(1),
            Background = Ui.Paper3,
            IsTabStop = true,
            Child = _padText,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_pad, "Recording pad. Keys and mouse are recorded here while recording.");
        _pad.KeyDown += (_, e) => OnPadKey(e, true);
        _pad.KeyUp += (_, e) => OnPadKey(e, false);
        _pad.PointerPressed += (_, e) => OnPadButton(e, true);
        _pad.PointerReleased += (_, e) => OnPadButton(e, false);
        _pad.PointerMoved += (_, e) => OnPadMove(e);
        _pad.PointerWheelChanged += (_, e) => OnPadWheel(e);
        root.Children.Add(_pad);

        // Footer.
        root.Children.Add(Ui.Rule1());
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryPillStyle") };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Style = Ui.Res<Style>("QuietButtonStyle") };
        cancel.Click += (_, _) => Close();
        _deleteMacro = new Button { Content = "Delete macro", Style = Ui.Res<Style>("QuietButtonStyle"), Visibility = isNew ? Visibility.Collapsed : Visibility.Visible };
        _deleteMacro.Click += (_, _) => DeleteMacro();
        _deleteNote.TextWrapping = TextWrapping.Wrap;
        _deleteNote.Visibility = Visibility.Collapsed;
        root.Children.Add(_deleteNote);
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_deleteMacro);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(cancel);
        right.Children.Add(save);
        Grid.SetColumn(right, 1);
        footer.Children.Add(right);
        root.Children.Add(footer);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(520 * scale), (int)(860 * scale)));
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Slender.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
        }

        // Recording is only for as long as this window is the one in front.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && _recording)
            {
                StopRecording("Stopped: you left this window.");
            }
        };
        Closed += (_, _) => { if (_recording) { _recorder.Stop(Environment.TickCount64); _recording = false; } };
        Paint();
    }

    // ---- building blocks ----

    private static NumberBox Number(string header, int value, int min, int max, Action<int> changed)
    {
        var box = new NumberBox
        {
            Header = header,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 1,
            LargeChange = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        box.ValueChanged += (_, e) =>
        {
            if (!double.IsNaN(e.NewValue))
            {
                changed((int)Math.Clamp(Math.Round(e.NewValue), min, max));
            }
        };
        return box;
    }

    private FrameworkElement Toolbar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var add = new Button { Content = "Add step", Style = Ui.Res<Style>("QuietButtonStyle") };
        var menu = new MenuFlyout();
        foreach ((MacroStepKind kind, string title) in MacroOps.AddChoices)
        {
            var item = new MenuFlyoutItem { Text = title };
            MacroStepKind k = kind;
            item.Click += (_, _) => AddStep(MacroOps.Default(k));
            menu.Items.Add(item);
        }

        add.Flyout = menu;
        row.Children.Add(add);
        row.Children.Add(Tool("Up", "Move the step up", () => MoveSelected(-1)));
        row.Children.Add(Tool("Down", "Move the step down", () => MoveSelected(1)));
        row.Children.Add(Tool("Copy", "Duplicate the step", () =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0)
            {
                _macro = MacroOps.Duplicate(_macro, i);
                Paint(i + 1);
            }
        }));
        row.Children.Add(Tool("Delete", "Delete the step", () =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0)
            {
                _macro = MacroOps.Delete(_macro, i);
                Paint(Math.Min(i, _macro.Steps.Count - 1));
            }
        }));
        return row;
    }

    private static Button Tool(string text, string tip, Action click)
    {
        var b = new Button { Content = text, Style = Ui.Res<Style>("QuietButtonStyle") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tip);
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    // ---- the step list ----

    private void AddStep(MacroStep step)
    {
        int at = _list.SelectedIndex >= 0 ? _list.SelectedIndex + 1 : _macro.Steps.Count;
        _macro = MacroOps.Insert(_macro, at, step);
        Paint(Math.Min(at, _macro.Steps.Count - 1));
    }

    private void MoveSelected(int by)
    {
        int i = _list.SelectedIndex;
        if (i < 0)
        {
            return;
        }

        int to = i + by;
        MacroDef moved = MacroOps.Move(_macro, i, to);
        if (!ReferenceEquals(moved, _macro))
        {
            _macro = moved;
            Paint(to);
        }
    }

    /// <summary>After a drag, the list's order is the new order of the steps.</summary>
    private void TakeOrderFromList()
    {
        var steps = _list.Items.OfType<ListViewItem>().Select(i => i.Tag).OfType<MacroStep>().ToList();
        if (steps.Count == _macro.Steps.Count)
        {
            _macro = _macro with { Steps = steps };
            Paint(_list.SelectedIndex);
        }
    }

    private ListViewItem Row(MacroStep step, int index)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        line.Children.Add(Ui.Text((index + 1).ToString(), 11, Ui.Muted, Ui.Mono));
        line.Children.Add(Ui.Text(MacroOps.Describe(step), 13, step.Kind == MacroStepKind.Pause ? Ui.Mark : null));
        return new ListViewItem { Content = line, Tag = step, MinHeight = 34, CornerRadius = new CornerRadius(6) };
    }

    /// <summary>Redraws the list from the working macro and selects a row (-1 = none).</summary>
    private void Paint(int select = -1)
    {
        _building = true;
        _list.Items.Clear();
        for (int i = 0; i < _macro.Steps.Count; i++)
        {
            _list.Items.Add(Row(_macro.Steps[i], i));
        }

        _list.SelectedIndex = select >= 0 && select < _macro.Steps.Count ? select : -1;
        _building = false;
        _summary.Text = _macro.Steps.Count == 0 ? "No steps yet. Add one, or record." : MacroOps.Summary(_macro);
        ShowEdit();
    }

    private void Changed(int index, MacroStep step)
    {
        _macro = MacroOps.Replace(_macro, index, step);
        if (index < _list.Items.Count)
        {
            _building = true; // swapping the row must not rebuild the edit panel the user is typing in
            _list.Items[index] = Row(step, index);
            _list.SelectedIndex = index;
            _building = false;
        }

        _summary.Text = MacroOps.Summary(_macro);
    }

    // ---- editing the selected step ----

    private void ShowEdit()
    {
        _edit.Children.Clear();
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _macro.Steps.Count)
        {
            return;
        }

        MacroStep s = _macro.Steps[i];
        switch (s.Kind)
        {
            case MacroStepKind.Pause:
                _edit.Children.Add(PauseEditor(i, s));
                break;
            case MacroStepKind.KeyDown:
            case MacroStepKind.KeyUp:
                _edit.Children.Add(CheckedText("Key (for example Ctrl, Shift, A, F5)", s.Text, t => ChordParser.TryParseKey(t, out _), t => Changed(i, s with { Text = t.Trim() })));
                break;
            case MacroStepKind.Tap:
                _edit.Children.Add(CheckedText("Shortcut (for example Ctrl+C, or Enter, Ctrl+W for a sequence)", s.Text, t => ChordParser.TryParseSequence(t, out _), t => Changed(i, s with { Text = t.Trim() })));
                break;
            case MacroStepKind.Type:
                _edit.Children.Add(TypeEditor(i, s));
                break;
            case MacroStepKind.MouseDown:
            case MacroStepKind.MouseUp:
            case MacroStepKind.Click:
                _edit.Children.Add(ButtonPicker(i, s));
                break;
            case MacroStepKind.Move:
                _edit.Children.Add(MoveEditor(i, s));
                break;
            case MacroStepKind.Wheel:
                _edit.Children.Add(WheelEditor(i, s));
                break;
            case MacroStepKind.Launch:
                _edit.Children.Add(LaunchEditor(i, s));
                break;
        }
    }

    private FrameworkElement PauseEditor(int i, MacroStep s)
    {
        var panel = new StackPanel { Spacing = 8 };
        NumberBox box = Number($"Pause length (milliseconds, {MacroStep.PauseMinMs} to {MacroStep.PauseMaxMs:N0})", s.Ms, MacroStep.PauseMinMs, MacroStep.PauseMaxMs,
            v => Changed(i, MacroStep.Pause(v)));
        box.SmallChange = 50;
        box.LargeChange = 500;
        panel.Children.Add(box);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (int ms in PausePresets)
        {
            int value = ms;
            var b = new Button { Content = MacroOps.PauseText(value), Style = Ui.Res<Style>("QuietButtonStyle") };
            b.Click += (_, _) =>
            {
                box.Value = value; // raises ValueChanged, which updates the step
            };
            presets.Children.Add(b);
        }

        panel.Children.Add(presets);
        return panel;
    }

    private static FrameworkElement CheckedText(string header, string? value, Func<string, bool> valid, Action<string> apply)
    {
        var panel = new StackPanel { Spacing = 4 };
        var box = new TextBox { Header = header, Text = value ?? string.Empty };
        var warn = Ui.Text(string.Empty, 12, Ui.Warn);
        box.TextChanged += (_, _) =>
        {
            if (valid(box.Text))
            {
                warn.Text = string.Empty;
                apply(box.Text);
            }
            else
            {
                warn.Text = "Slender cannot send that. The step keeps its last good value.";
            }
        };
        panel.Children.Add(box);
        panel.Children.Add(warn);
        return panel;
    }

    private FrameworkElement TypeEditor(int i, MacroStep s)
    {
        var box = new TextBox
        {
            Header = "Text to type",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxHeight = 140,
            MaxLength = MacroStep.TextMax,
            Text = s.Text ?? string.Empty,
        };
        box.TextChanged += (_, _) =>
        {
            if (box.Text.Length > 0)
            {
                Changed(i, MacroStep.Type(box.Text.Replace("\r\n", "\n").Replace('\r', '\n')));
            }
        };
        return box;
    }

    private FrameworkElement ButtonPicker(int i, MacroStep s)
    {
        var combo = new ComboBox { Header = "Mouse button", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 160 };
        foreach (string b in MacroStep.Buttons)
        {
            combo.Items.Add(b);
        }

        combo.SelectedItem = s.Text;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string b)
            {
                Changed(i, s with { Text = b });
            }
        };
        return combo;
    }

    private FrameworkElement MoveEditor(int i, MacroStep s)
    {
        var panel = new StackPanel { Spacing = 8 };
        int x = s.X, y = s.Y;
        bool absolute = s.Absolute;
        void Apply() => Changed(i, new MacroStep(MacroStepKind.Move, X: x, Y: y, Absolute: absolute));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(Number(absolute ? "Left (pixels)" : "Right (pixels, negative = left)", x, -MacroStep.MoveLimit, MacroStep.MoveLimit, v => { x = v; Apply(); }));
        row.Children.Add(Number(absolute ? "Top (pixels)" : "Down (pixels, negative = up)", y, -MacroStep.MoveLimit, MacroStep.MoveLimit, v => { y = v; Apply(); }));
        panel.Children.Add(row);
        var abs = new CheckBox { Content = "To a fixed spot on the desktop (otherwise from where the pointer is)", IsChecked = absolute };
        abs.Click += (_, _) =>
        {
            absolute = abs.IsChecked == true;
            Apply();
            ShowEdit(); // the field names change
        };
        panel.Children.Add(abs);
        return panel;
    }

    private FrameworkElement WheelEditor(int i, MacroStep s)
    {
        int vertical = s.Y, sideways = s.X;
        void Apply() => Changed(i, MacroStep.Wheel(vertical, sideways));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(Number("Up (notches, negative = down)", vertical, -MacroStep.WheelLimit, MacroStep.WheelLimit, v => { vertical = v; Apply(); }));
        row.Children.Add(Number("Right (notches, negative = left)", sideways, -MacroStep.WheelLimit, MacroStep.WheelLimit, v => { sideways = v; Apply(); }));
        return row;
    }

    private FrameworkElement LaunchEditor(int i, MacroStep s)
    {
        var panel = new StackPanel { Spacing = 8 };
        var target = new TextBox { Header = "App, file, folder or URL", Text = s.Text ?? string.Empty };
        var args = new TextBox { Header = "Arguments (apps only)", Text = s.Arguments ?? string.Empty };
        void Apply()
        {
            if (target.Text.Trim().Length > 0)
            {
                Changed(i, MacroStep.Launch(target.Text.Trim().Trim('"'), args.Text.Length > 0 ? args.Text : null));
            }
        }

        target.TextChanged += (_, _) => Apply();
        args.TextChanged += (_, _) => Apply();
        panel.Children.Add(target);
        panel.Children.Add(args);
        return panel;
    }

    // ---- recorder (reads events on the pad; sends nothing) ----

    private void StartRecording()
    {
        _insertAt = _list.SelectedIndex >= 0 ? _list.SelectedIndex + 1 : _macro.Steps.Count;
        _recording = true;
        _recorder.Start();
        _record.Content = "Stop";
        _recordStatus.Text = "Recording. Do it on the pad.";
        _padText.Text = "Recording: use keys and mouse here";
        _pad.BorderBrush = Ui.Mark;
        _pad.Focus(FocusState.Programmatic);
    }

    private void StopRecording(string? note = null)
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        IReadOnlyList<MacroStep> steps = _recorder.Stop(Environment.TickCount64);
        _record.Content = "Record";
        _padText.Text = "Press Record to start";
        _pad.BorderBrush = Ui.Rule;
        MacroDef before = _macro;
        _macro = MacroOps.InsertRange(_macro, _insertAt < 0 ? _macro.Steps.Count : _insertAt, steps);
        int added = _macro.Steps.Count - before.Steps.Count;
        string text = added == 0 ? "Nothing recorded." : added == 1 ? "Recorded 1 step." : $"Recorded {added} steps.";
        if (added < steps.Count)
        {
            text += " The macro is full, so the rest was dropped.";
        }

        if (_recorder.Skipped > 0)
        {
            text += $" {_recorder.Skipped} key(s) Slender cannot send were left out.";
        }

        _recordStatus.Text = note is null ? text : note + " " + text;
        Paint(added > 0 ? Math.Min(_insertAt + added - 1, _macro.Steps.Count - 1) : -1);
    }

    private void OnPadKey(KeyRoutedEventArgs e, bool down)
    {
        if (!_recording)
        {
            return;
        }

        e.Handled = true; // keys belong to the recording, not to Settings
        _recorder.Key(Environment.TickCount64, (ushort)e.Key, down);
        CheckFull();
    }

    private static (int X, int Y) PadPoint(PointerRoutedEventArgs e, UIElement pad)
    {
        double scale = pad.XamlRoot?.RasterizationScale ?? 1.0; // device pixels, so the recorded moves match what a replay sends
        Windows.Foundation.Point p = e.GetCurrentPoint(pad).Position;
        return ((int)Math.Round(p.X * scale), (int)Math.Round(p.Y * scale));
    }

    private void OnPadButton(PointerRoutedEventArgs e, bool down)
    {
        if (!_recording)
        {
            return;
        }

        PointerPointProperties props = e.GetCurrentPoint(_pad).Properties;
        string? button = props.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => "Left",
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => "Right",
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => "Middle",
            PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => "Back",
            PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => "Forward",
            _ => null,
        };
        if (button is null)
        {
            return;
        }

        if (down)
        {
            _pad.CapturePointer(e.Pointer); // the release is seen even if the pointer leaves the pad
            _pad.Focus(FocusState.Programmatic);
        }
        else
        {
            _pad.ReleasePointerCapture(e.Pointer);
        }

        (int x, int y) = PadPoint(e, _pad);
        _recorder.Button(Environment.TickCount64, button, down, x, y);
        e.Handled = true;
        CheckFull();
    }

    private void OnPadMove(PointerRoutedEventArgs e)
    {
        if (!_recording || _recordMoves.IsChecked != true)
        {
            return;
        }

        (int x, int y) = PadPoint(e, _pad);
        _recorder.Pointer(Environment.TickCount64, x, y);
    }

    private void OnPadWheel(PointerRoutedEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        PointerPointProperties props = e.GetCurrentPoint(_pad).Properties;
        int notches = props.MouseWheelDelta / 120;
        if (notches == 0)
        {
            notches = Math.Sign(props.MouseWheelDelta);
        }

        if (props.IsHorizontalMouseWheel)
        {
            _recorder.Wheel(Environment.TickCount64, 0, notches);
        }
        else
        {
            _recorder.Wheel(Environment.TickCount64, notches, 0);
        }

        e.Handled = true;
        CheckFull();
    }

    private void CheckFull()
    {
        if (_recording && _recorder.Full)
        {
            StopRecording("Stopped: that is as many steps as a macro can hold.");
        }
    }

    // ---- save / delete ----

    private void Save()
    {
        if (_recording)
        {
            StopRecording();
        }

        MacroDef? clean = _macro.Normalized();
        if (clean is null)
        {
            return;
        }

        _config.Update(c => MacroOps.Upsert(c, clean));
        _saved?.Invoke(clean);
        Close();
    }

    private void DeleteMacro()
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            _deleteNote.Text = MacroReferences.UsageText(MacroReferences.Count(_config.Config, _id));
            _deleteNote.Visibility = Visibility.Visible;
            _deleteMacro.Content = "Click again to delete";
            return;
        }

        _config.Update(c => MacroReferences.Delete(c, _id)); // also turns off every control that used it
        Close();
    }
}
