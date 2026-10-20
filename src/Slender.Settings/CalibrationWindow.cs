using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;
using Windows.Graphics;

namespace Slender.Settings;

/// <summary>
/// Touch calibration (user 2026-10-20): a small always-on-top window you can drag anywhere, opened from Settings. Two timed
/// steps, each with its own length: take hold of the mouse over and over, then rest, tap and double tap with the mouse still.
/// While it is open the tray is in preview mode (nothing runs) and sends each touch; this window samples the pointer and turns
/// both into TouchSamples. The result proposes the two grab thresholds and shows what they would have done; Save writes them.
/// </summary>
public sealed class CalibrationWindow : Window
{
    private static readonly int[] Lengths = [30, 60, 90, 120];

    private readonly ConfigService _config;
    private readonly ThumbPreviewClient _client;
    private readonly DispatcherQueueTimer _tick;
    private readonly TouchSampler[] _samplers;
    private readonly ComboBox[] _length = new ComboBox[2];
    private readonly TextBlock[] _count = new TextBlock[2];
    private readonly Border[] _stepBox = new Border[2];
    private readonly TextBlock _timer;
    private readonly TextBlock _status;
    private readonly Button _go;
    private readonly StackPanel _results;
    private int _step;          // 0 or 1: the step to run next or running
    private bool _running;
    private long _endsAt;
    private bool _live;

    public CalibrationWindow(ConfigService config)
    {
        _config = config;
        int rest = config.Config.Pointer.TouchRestMs;
        _samplers = [new TouchSampler(rest), new TouchSampler(rest)];
        Title = "Touch calibration";
        SystemBackdrop = new MicaBackdrop();

        var root = new StackPanel { Spacing = 12, Padding = new Thickness(18) };
        root.Children.Add(Ui.Text("Touch calibration", 18, null, Ui.Heading));
        var intro = Ui.Text("Nothing runs while this window is open. Drag it anywhere, set how long each step lasts, then start.", 12.5, Ui.Muted);
        intro.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(intro);
        root.Children.Add(Step(0, "Take hold of the mouse", "Let go of it completely, then take hold of it again, the way you normally do. Over and over."));
        root.Children.Add(Step(1, "Rest, tap and double tap", "Keep the mouse still. Rest your thumb on the wheel, tap it, double tap it, a few of each."));

        _timer = Ui.Text("1:00", 34, null, Ui.Display);
        _timer.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(_timer);
        _status = Ui.Text("Connecting to Slender…", 12, Ui.Muted);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(_status);

        _go = new Button { Style = Ui.Res<Style>("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 160 };
        _go.Click += (_, _) => Go();
        root.Children.Add(_go);

        _results = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        root.Children.Add(_results);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(400 * scale), (int)(600 * scale)));
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Slender.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        _client = new ThumbPreviewClient(a => DispatcherQueue.TryEnqueue(() => a()));
        _client.ConnectedChanged += live =>
        {
            _live = live;
            if (!live && _running)
            {
                _running = false;
            }

            Paint();
        };
        _client.Touch += OnTouch;
        _client.Start();

        _tick = DispatcherQueue.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(15);
        _tick.Tick += (_, _) => Tick();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _client.Dispose();
        };
        Paint();
    }

    private FrameworkElement Step(int index, string title, string hint)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var number = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = Ui.MarkSoft,
            Child = new TextBlock { Text = (index + 1).ToString(), FontFamily = Ui.Heading, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Mark },
        };
        grid.Children.Add(number);
        var name = Ui.Text(title, 13.5);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var length = new ComboBox { MinWidth = 86 };
        foreach (int s in Lengths)
        {
            length.Items.Add($"{s} s");
        }

        length.SelectedIndex = 1;
        length.SelectionChanged += (_, _) => Paint();
        AutomationProperties.SetName(length, $"How long step {index + 1} lasts");
        _length[index] = length;
        Grid.SetColumn(length, 2);
        grid.Children.Add(length);

        var help = Ui.Text(hint, 12, Ui.Muted);
        help.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(help, 1);
        Grid.SetColumn(help, 1);
        grid.Children.Add(help);
        _count[index] = Ui.Text(string.Empty, 12, Ui.Muted);
        _count[index].VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(_count[index], 1);
        Grid.SetColumn(_count[index], 2);
        grid.Children.Add(_count[index]);

        var box = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Ui.Rule, Child = grid };
        _stepBox[index] = box;
        return box;
    }

    private int LengthSeconds(int step) => Lengths[Math.Max(0, _length[step].SelectedIndex)];

    private void Go()
    {
        if (_running)
        {
            _running = false; // Stop: what was recorded so far counts
            _tick.Stop();
            Finish();
            return;
        }

        if (_step > 1)
        {
            // Run again from the start.
            int rest = _config.Config.Pointer.TouchRestMs;
            _samplers[0] = new TouchSampler(rest);
            _samplers[1] = new TouchSampler(rest);
            _step = 0;
            _results.Visibility = Visibility.Collapsed;
            Paint();
            return;
        }

        _running = true;
        _endsAt = Environment.TickCount64 + LengthSeconds(_step) * 1000L;
        _tick.Start();
        Paint();
    }

    private void Tick()
    {
        if (!_running)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _samplers[_step].Pointer(now, pt.X, pt.Y);
        }

        if (now >= _endsAt)
        {
            _running = false;
            _tick.Stop();
            Finish();
            return;
        }

        PaintTimer(_endsAt - now);
    }

    private void OnTouch(bool down)
    {
        if (!_running)
        {
            return;
        }

        if (down)
        {
            _samplers[_step].TouchDown(Environment.TickCount64);
        }
        else if (_samplers[_step].Lift() is not null)
        {
            PaintCounts();
        }
    }

    /// <summary>A step ended (time up or Stop): on to step 2, or the result.</summary>
    private void Finish()
    {
        _samplers[_step].Lift();
        _step++;
        if (_step > 1)
        {
            ShowResult();
        }

        Paint();
    }

    private void ShowResult()
    {
        _results.Children.Clear();
        PointerSettings p = _config.Config.Pointer;
        CalibrationResult? r = TouchCalibration.Compute(_samplers[0].Samples, _samplers[1].Samples, p.GrabBeforePx, p.GrabMovePx);
        if (r is null)
        {
            var few = Ui.Text($"Too few touches in step 2 ({_samplers[1].Samples.Count}; at least {TouchCalibration.MinTouches}). Run it again and rest, tap and double tap a few more times.", 12.5, Ui.Brush("WarnBrush"));
            few.TextWrapping = TextWrapping.Wrap;
            _results.Children.Add(few);
        }
        else
        {
            _results.Children.Add(Ui.Text("Result", 11.5, Ui.Muted));
            _results.Children.Add(Line("Pointer movement before a touch", $"{p.GrabBeforePx} → {r.GrabBeforePx} px"));
            _results.Children.Add(Line("Pointer movement during a touch", $"{p.GrabMovePx} → {r.GrabMovePx} px"));
            _results.Children.Add(Line("Grabs ignored", $"{r.GrabsIgnored} of {r.Grabs} (now {r.GrabsIgnoredBefore})"));
            _results.Children.Add(Line("Your touches kept", $"{r.TouchesKept} of {r.Touches} (now {r.TouchesKeptBefore})"));
            var save = new Button { Content = "Save", Style = Ui.Res<Style>("AccentButtonStyle") };
            save.Click += (_, _) =>
            {
                _config.Update(c => c with { Pointer = c.Pointer with { GrabBeforePx = r.GrabBeforePx, GrabMovePx = r.GrabMovePx } });
                _status.Text = "Saved. Slender uses the new values now.";
                save.IsEnabled = false;
            };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            actions.Children.Add(save);
            _results.Children.Add(actions);
        }

        var defaults = new PointerSettings();
        if (p.GrabBeforePx != defaults.GrabBeforePx || p.GrabMovePx != defaults.GrabMovePx)
        {
            var reset = new Button { Content = $"Reset to defaults ({defaults.GrabBeforePx} / {defaults.GrabMovePx} px)", Style = Ui.Res<Style>("QuietButtonStyle") };
            reset.Click += (_, _) =>
            {
                _config.Update(c => c with { Pointer = c.Pointer with { GrabBeforePx = defaults.GrabBeforePx, GrabMovePx = defaults.GrabMovePx } });
                _status.Text = "Back to the defaults.";
                reset.IsEnabled = false;
            };
            _results.Children.Add(reset);
        }

        _results.Visibility = Visibility.Visible;
    }

    private static FrameworkElement Line(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Ui.Text(label, 12.5, Ui.Muted));
        var v = Ui.Text(value, 12.5);
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    private void Paint()
    {
        for (int i = 0; i < 2; i++)
        {
            bool current = i == _step && _step <= 1;
            _stepBox[i].BorderBrush = current ? Ui.Mark : Ui.Rule;
            _stepBox[i].Background = current && _running ? Ui.MarkSoft : Ui.Brush("RailTransparent");
            _length[i].IsEnabled = !_running;
        }

        PaintCounts();
        if (!_running)
        {
            PaintTimer(_step <= 1 ? LengthSeconds(_step) * 1000L : 0);
        }

        _go.IsEnabled = _live || _running || _step > 1;
        _go.Content = _running ? "Stop" : _step switch
        {
            0 => "Start step 1",
            1 => "Start step 2",
            _ => "Run again",
        };
        if (!_live && !_running && _step <= 1)
        {
            _status.Text = "Slender isn't running, so touches can't be measured.";
        }
        else if (_running)
        {
            _status.Text = _step == 0 ? "Step 1: take hold of the mouse, over and over." : "Step 2: rest, tap and double tap, mouse still.";
        }
        else if (_step == 1)
        {
            _status.Text = "Step 1 done. Start step 2 when ready.";
        }
        else if (_step == 0)
        {
            _status.Text = "Ready.";
        }
    }

    private void PaintCounts()
    {
        _count[0].Text = $"{_samplers[0].Samples.Count} grabs";
        _count[1].Text = $"{_samplers[1].Samples.Count} touches";
    }

    private void PaintTimer(long ms)
    {
        long s = (ms + 999) / 1000;
        _timer.Text = $"{s / 60}:{s % 60:00}";
    }
}
