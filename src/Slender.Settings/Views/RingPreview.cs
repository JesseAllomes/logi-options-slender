using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Slender.Engine;
using Slender.Engine.Ring;
using Slender.Settings.Logic;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Slender.Settings.Views;

/// <summary>
/// The Actions Ring drawn with XAML shapes from <see cref="RingLayout"/>, over a neutral sheet. Styles follow the round-1
/// prototypes (Bezel, Compass, Keypad). It doubles as the slot editor: click or Enter on a slot, right-click or Menu for the
/// slot menu, Delete to clear, Esc or Backspace to leave a folder, arrows to move. The tray overlay is drawn separately.
/// </summary>
internal sealed class RingPreview : UserControl
{
    public const double SheetWidth = 520;
    public const double SheetHeight = 470;

    private readonly Canvas _canvas = new() { Width = SheetWidth, Height = SheetHeight };
    private readonly Canvas _ring = new() { Width = SheetWidth, Height = SheetHeight };
    private RingAppearance _appearance = new();
    private IReadOnlyList<RingSlot> _slots = [];
    private bool _inFolder;
    private bool _muted;

    public RingPreview()
    {
        Width = SheetWidth;
        Height = SheetHeight;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        CornerRadius = new CornerRadius(10);
        AutomationProperties.SetName(this, "Actions Ring slots. Arrow keys move between slots, Enter edits the slot, Delete clears it, Escape leaves a folder.");

        var sheet = new Border { Width = SheetWidth, Height = SheetHeight, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Hex("#E9ECEE")) };
        var lines = new Canvas { Width = SheetWidth, Height = SheetHeight, IsHitTestVisible = false };
        var rule = new SolidColorBrush(Hex("#C9CFD3"));
        for (double y = 26; y < SheetHeight; y += 26)
        {
            lines.Children.Add(new Line { X1 = 0, X2 = SheetWidth, Y1 = y, Y2 = y, Stroke = rule, StrokeThickness = 1 });
        }

        for (double x = 96; x < SheetWidth; x += 96)
        {
            lines.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = SheetHeight, Stroke = rule, StrokeThickness = 1 });
        }

        _canvas.Children.Add(lines);
        _canvas.Children.Add(_ring);
        var root = new Grid { Width = SheetWidth, Height = SheetHeight };
        root.Children.Add(sheet);
        root.Children.Add(_canvas);
        Content = new Border { Child = root, CornerRadius = new CornerRadius(10), Background = Ui.Brush("PaperBrush"), BorderBrush = Ui.Rule, BorderThickness = new Thickness(1) };

        GotFocus += (_, _) =>
        {
            if (Selected is null && !_muted)
            {
                Selected = 0;
                Draw();
            }
        };
        KeyDown += OnKeyDown;
    }

    /// <summary>The selected slot index, or null.</summary>
    public int? Selected { get; set; }

    /// <summary>Maps a "file:" icon path to an image file to show (an exe's extracted icon, or the image itself); null when none.</summary>
    public Func<string, Task<string?>>? FileIconPath { get; set; }

    /// <summary>A slot was clicked or Enter was pressed on it: slot index and the point (in this control) to anchor the picker at.</summary>
    public event Action<int, Point>? SlotInvoked;

    /// <summary>Right-click or the Menu key on a slot.</summary>
    public event Action<int, Point>? SlotMenu;

    public event Action<int>? SlotDelete;

    /// <summary>The centre was clicked inside a folder, or Esc or Backspace was pressed.</summary>
    public event Action? BackRequested;

    public Point CentreOf(int index)
    {
        (double x, double y) = RingLayout.SlotCenter(_appearance.Style, _appearance.Size, index);
        return new Point(SheetWidth / 2 + x, SheetHeight / 2 + y);
    }

    /// <summary>Redraws. Muted draws the ring faded and not editable (an app that follows All apps).</summary>
    public void Render(RingAppearance appearance, IReadOnlyList<RingSlot> slots, bool inFolder, bool muted)
    {
        _appearance = appearance;
        _slots = slots;
        _inFolder = inFolder;
        _muted = muted;
        IsTabStop = !muted;
        Draw();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_muted)
        {
            return;
        }

        (int dx, int dy) = e.Key switch
        {
            VirtualKey.Left => (-1, 0),
            VirtualKey.Right => (1, 0),
            VirtualKey.Up => (0, -1),
            VirtualKey.Down => (0, 1),
            _ => (0, 0),
        };
        if (dx != 0 || dy != 0)
        {
            e.Handled = true;
            Selected = RingOps.Navigate(_appearance.Style, _appearance.Size, Selected, dx, dy);
            Draw();
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
            case VirtualKey.Space:
                if (Selected is { } s)
                {
                    e.Handled = true;
                    SlotInvoked?.Invoke(s, CentreOf(s));
                }

                break;
            case VirtualKey.Delete:
                if (Selected is { } d)
                {
                    e.Handled = true;
                    SlotDelete?.Invoke(d);
                }

                break;
            case VirtualKey.Escape:
            case VirtualKey.Back:
                if (_inFolder)
                {
                    e.Handled = true;
                    BackRequested?.Invoke();
                }

                break;
            case VirtualKey.Application:
                if (Selected is { } m)
                {
                    e.Handled = true;
                    SlotMenu?.Invoke(m, CentreOf(m));
                }

                break;
        }
    }

    // ---- drawing ----

    private static Color Hex(string hex)
    {
        string h = hex.TrimStart('#');
        return Color.FromArgb(255, Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }

    private static SolidColorBrush Brush(Color c, byte alpha = 255) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    private static readonly Brush Clear = new SolidColorBrush(Colors.Transparent);

    private void Draw()
    {
        _ring.Children.Clear();
        _ring.Opacity = _muted ? 0.45 : _appearance.OpacityPercent / 100.0;
        double cx = SheetWidth / 2, cy = SheetHeight / 2;
        RingMetrics m = RingLayout.Metrics(_appearance.Style, _appearance.Size);
        Color accent = Hex(RingOps.NormaliseAccent(_appearance.Accent) ?? "#D9925F");
        var views = _slots.Select(RingOps.Describe).ToList();
        while (views.Count < RingLayout.SlotCount)
        {
            views.Add(RingOps.Describe(RingSlot.Empty));
        }

        switch (_appearance.Style)
        {
            case RingStyle.Compass:
                DrawCompass(cx, cy, m, accent, views);
                break;
            case RingStyle.Keypad:
                DrawKeypad(cx, cy, m, accent, views);
                break;
            default:
                DrawBezel(cx, cy, m, accent, views);
                break;
        }
    }

    private static Point Pt(double cx, double cy, double r, double angle) =>
        new(cx + r * Math.Cos(angle * Math.PI / 180), cy + r * Math.Sin(angle * Math.PI / 180));

    private static Path Wedge(double cx, double cy, double r1, double r2, double a1, double a2)
    {
        var figure = new PathFigure { StartPoint = Pt(cx, cy, r2, a1), IsClosed = true };
        figure.Segments.Add(new ArcSegment { Point = Pt(cx, cy, r2, a2), Size = new Size(r2, r2), SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = Pt(cx, cy, r1, a2) });
        figure.Segments.Add(new ArcSegment { Point = Pt(cx, cy, r1, a1), Size = new Size(r1, r1), SweepDirection = SweepDirection.Counterclockwise });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Path { Data = geometry };
    }

    private static Path Arc(double cx, double cy, double r, double a1, double a2)
    {
        var figure = new PathFigure { StartPoint = Pt(cx, cy, r, a1) };
        figure.Segments.Add(new ArcSegment { Point = Pt(cx, cy, r, a2), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Path { Data = geometry };
    }

    private T Add<T>(T element, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _ring.Children.Add(element);
        return element;
    }

    /// <summary>A fresh collection each time: WinUI refuses one DoubleCollection shared by several shapes.</summary>
    private static DoubleCollection Dash() => [4, 4];

    private void Wire(Shape hit, int index)
    {
        hit.Fill = Clear;
        hit.Tag = index;
        hit.Tapped += (_, e) =>
        {
            if (_muted)
            {
                return;
            }

            e.Handled = true;
            Selected = index;
            Draw();
            Focus(FocusState.Pointer);
            SlotInvoked?.Invoke(index, CentreOf(index));
        };
        hit.RightTapped += (_, e) =>
        {
            if (_muted)
            {
                return;
            }

            e.Handled = true;
            Selected = index;
            Draw();
            Focus(FocusState.Pointer);
            SlotMenu?.Invoke(index, CentreOf(index));
        };
    }

    /// <summary>The slot's glyph or file icon centred on (x, y).</summary>
    private void Icon(RingSlotView v, double x, double y, double size, Brush foreground)
    {
        if (v.IsEmpty)
        {
            Add(new TextBlock { Text = "+", FontSize = size, Foreground = Ui.Muted, IsHitTestVisible = false, Width = size * 2, TextAlignment = TextAlignment.Center }, x - size, y - size * 0.72);
            return;
        }

        string? ch = RingGlyphs.ToChar(v.Glyph);
        var glyph = new FontIcon { Glyph = ch ?? "", FontSize = size, Foreground = foreground, IsHitTestVisible = false };
        glyph.Width = size * 1.4;
        glyph.Height = size * 1.4;
        Add(glyph, x - size * 0.7, y - size * 0.7);
        if (v.FilePath is { } path && FileIconPath is { } resolve)
        {
            var image = new Image { Width = size * 1.2, Height = size * 1.2, IsHitTestVisible = false, Stretch = Stretch.Uniform };
            Add(image, x - size * 0.6, y - size * 0.6);
            _ = ShowFileIcon(image, glyph, path, resolve, (int)Math.Ceiling(size * 1.2 * 2));
        }
    }

    private static async Task ShowFileIcon(Image image, UIElement glyph, string path, Func<string, Task<string?>> resolve, int decode)
    {
        try
        {
            if (await resolve(path) is { } file && File.Exists(file))
            {
                image.Source = new BitmapImage(new Uri(file)) { DecodePixelWidth = decode };
                glyph.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            // keep the glyph
        }
    }

    private TextBlock Label(string text, double size, Brush foreground, double left, double top, double width, TextAlignment align, bool mono = false)
    {
        TextBlock t = Ui.Text(text, size, foreground, mono ? Ui.Mono : null, mono ? null : Microsoft.UI.Text.FontWeights.SemiBold);
        t.Width = width;
        t.TextAlignment = align;
        t.IsHitTestVisible = false;
        return Add(t, left, top);
    }

    private bool ShowLabel(int i) => _appearance.Labels == RingLabels.Always || Selected == i;

    // ---- 1 Bezel ----

    private void DrawBezel(double cx, double cy, RingMetrics m, Color accent, List<RingSlotView> views)
    {
        double k = m.Scale;
        Brush ink = Ui.Ink, muted = Ui.Muted;
        Add(new Ellipse { Width = 284 * k, Height = 284 * k, Fill = Brush(Hex("#1B1F23"), 140), IsHitTestVisible = false }, cx - 142 * k, cy - 142 * k);
        for (int t = 0; t < 96; t++)
        {
            double a = t * 3.75;
            Point p1 = Pt(cx, cy, 133 * k, a), p2 = Pt(cx, cy, 139 * k, a);
            _ring.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Ui.Rule, StrokeThickness = 1.2, IsHitTestVisible = false });
        }

        for (int i = 0; i < RingLayout.SlotCount; i++)
        {
            try
            {
                RingSlotView v = views[i];
                double a = RingLayout.AngleOf(i);
                bool on = Selected == i;
                Path w = Wedge(cx, cy, m.InnerRadius, m.OuterRadius, a - 21.5, a + 21.5);
                w.Fill = on ? Ui.Paper3 : Ui.Brush("Paper2Brush");
                w.Stroke = v.IsEmpty ? Ui.Muted : Ui.Rule;
                w.StrokeThickness = 1;
                if (v.IsEmpty)
                {
                    w.StrokeDashArray = Dash();
                }

                w.IsHitTestVisible = false;
                _ring.Children.Add(w);
                if (on)
                {
                    Path soft = Wedge(cx, cy, m.InnerRadius, m.OuterRadius, a - 21.5, a + 21.5);
                    soft.Fill = Brush(accent, 41);
                    soft.IsHitTestVisible = false;
                    _ring.Children.Add(soft);
                    Path rim = Arc(cx, cy, 131 * k, a - 21, a + 21);
                    rim.Stroke = Brush(accent);
                    rim.StrokeThickness = 4;
                    rim.StrokeStartLineCap = PenLineCap.Round;
                    rim.StrokeEndLineCap = PenLineCap.Round;
                    rim.IsHitTestVisible = false;
                    _ring.Children.Add(rim);
                }

                (double sx, double sy) = RingLayout.SlotCenter(_appearance.Style, _appearance.Size, i);
                double x = cx + sx, y = cy + sy;
                bool label = ShowLabel(i) && !v.IsEmpty && _appearance.Labels == RingLabels.Always;
                Icon(v, x, label ? y - 6 * k : y, 21 * k, on ? Brush(accent) : ink);
                if (label)
                {
                    Label(v.Title, 9.5 * k, on ? ink : muted, x - 33 * k, y + 8 * k, 66 * k, TextAlignment.Center);
                }

                Path hit = Wedge(cx, cy, m.InnerRadius, m.OuterRadius, a - 22.5, a + 22.5);
                Wire(hit, i);
                _ring.Children.Add(hit);
                Automate(hit, i, v);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Services.CrashLog.Write(e); // skip this slot, keep the rest of the ring
            }
        }

        var hub = new Ellipse { Width = 104 * k, Height = 104 * k, Fill = Ui.Brush("PaperBrush"), Stroke = Ui.Rule, StrokeThickness = 1 };
        Add(hub, cx - 52 * k, cy - 52 * k);
        if (Selected is { } s)
        {
            RingSlotView v = views[s];
            Label(v.Title, 15 * k, v.IsEmpty ? muted : ink, cx - 48 * k, cy - 16 * k, 96 * k, TextAlignment.Center);
            Label(v.Sub, 10.5 * k, muted, cx - 48 * k, cy + 6 * k, 96 * k, TextAlignment.Center, mono: true);
        }
        else
        {
            Label(_inFolder ? "Back" : "Esc", 12 * k, muted, cx - 48 * k, cy - 9 * k, 96 * k, TextAlignment.Center);
        }

        HubHit(cx, cy, 52 * k);
    }

    // ---- 2 Compass ----

    private void DrawCompass(double cx, double cy, RingMetrics m, Color accent, List<RingSlotView> views)
    {
        double k = m.Scale;
        Add(new Ellipse { Width = 200 * k, Height = 200 * k, Stroke = Brush(Hex("#1B1F23"), 90), StrokeThickness = 1, IsHitTestVisible = false }, cx - 100 * k, cy - 100 * k);
        if (Selected is { } sel)
        {
            Point end = Pt(cx, cy, 74 * k, RingLayout.AngleOf(sel));
            _ring.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = end.X, Y2 = end.Y, Stroke = Brush(accent), StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false });
        }

        Add(new Ellipse { Width = 18, Height = 18, Fill = Ui.Brush("PaperBrush"), Stroke = Brush(accent), StrokeThickness = 2, IsHitTestVisible = false }, cx - 9, cy - 9);
        Add(new Ellipse { Width = 6, Height = 6, Fill = Brush(accent), IsHitTestVisible = false }, cx - 3, cy - 3);

        for (int i = 0; i < RingLayout.SlotCount; i++)
        {
            try
            {
                RingSlotView v = views[i];
                bool on = Selected == i;
                (double sx, double sy) = RingLayout.SlotCenter(_appearance.Style, _appearance.Size, i);
                double x = cx + sx, y = cy + sy;
                var bubble = new Ellipse
                {
                    Width = 2 * m.BubbleRadius,
                    Height = 2 * m.BubbleRadius,
                    Fill = on ? Brush(accent) : Ui.Brush("PaperBrush"),
                    Stroke = v.IsEmpty ? Ui.Muted : on ? Brush(accent) : Ui.Rule,
                    StrokeThickness = 1.2,
                    IsHitTestVisible = false,
                };
                if (v.IsEmpty)
                {
                    bubble.StrokeDashArray = Dash();
                }

                Add(bubble, x - m.BubbleRadius, y - m.BubbleRadius);
                Icon(v, x, y, 19 * k, on ? Ui.Brush("PaperBrush") : Ui.Ink);
                var hit = new Ellipse { Width = 2 * (m.BubbleRadius + 4 * k), Height = 2 * (m.BubbleRadius + 4 * k) };
                Wire(hit, i);
                Add(hit, x - m.BubbleRadius - 4 * k, y - m.BubbleRadius - 4 * k);
                Automate(hit, i, v);

                if (ShowLabel(i) && !v.IsEmpty || on)
                {
                    double a = RingLayout.AngleOf(i);
                    double cos = Math.Cos(a * Math.PI / 180), sin = Math.Sin(a * Math.PI / 180);
                    Point lp = Pt(cx, cy, m.LabelRadius, a);
                    bool right = cos > 0.3, left = cos < -0.3;
                    double dx = right ? -14 * k : left ? 14 * k : 0;
                    double ty = sin < -0.9 ? lp.Y - 18 * k : sin > 0.9 ? lp.Y - 2 * k : lp.Y - 14 * k;
                    var stack = new StackPanel { Spacing = 0, IsHitTestVisible = false };
                    stack.Children.Add(Ui.Text(v.Title, (on ? 14 : 12) * k, v.IsEmpty ? Ui.Muted : Ui.Ink, null, Microsoft.UI.Text.FontWeights.SemiBold));
                    if (on && v.Sub.Length > 0)
                    {
                        stack.Children.Add(Ui.Text(v.Sub, 10 * k, Ui.Muted, Ui.Mono));
                    }

                    var pill = new Border { Background = Brush(Hex("#1B1F23"), 216), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 3), Child = stack, IsHitTestVisible = false };
                    double anchorX = lp.X + dx;
                    pill.SizeChanged += (_, e) => Canvas.SetLeft(pill, right ? anchorX : left ? anchorX - e.NewSize.Width : anchorX - e.NewSize.Width / 2);
                    Add(pill, anchorX, ty);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Services.CrashLog.Write(e); // skip this slot, keep the rest of the ring
            }
        }

        HubHit(cx, cy, 14);
    }

    // ---- 3 Keypad ----

    private void DrawKeypad(double cx, double cy, RingMetrics m, Color accent, List<RingSlotView> views)
    {
        double k = m.Scale;
        double kw = m.KeyWidth, kh = m.KeyHeight, gap = m.KeyGap;
        double ox = cx - (kw * 1.5 + gap), oy = cy - (kh * 1.5 + gap);
        Add(new Rectangle
        {
            Width = kw * 3 + gap * 2 + 20,
            Height = kh * 3 + gap * 2 + 20,
            RadiusX = 14,
            RadiusY = 14,
            Fill = Brush(Hex("#1B1F23"), 209),
            Stroke = Ui.Rule,
            IsHitTestVisible = false,
        }, ox - 10, oy - 10);

        for (int i = 0; i < RingLayout.SlotCount; i++)
        {
            try
            {
                RingSlotView v = views[i];
                bool on = Selected == i;
                (int col, int row) = RingLayout.KeypadCell(i);
                double x = ox + col * (kw + gap), y = oy + row * (kh + gap) + (on ? 2 : 0);
                Add(new Rectangle { Width = kw, Height = kh, RadiusX = 8, RadiusY = 8, Fill = Brush(Hex("#15191C")), IsHitTestVisible = false }, x, y + 3);
                var cap = new Rectangle
                {
                    Width = kw,
                    Height = kh,
                    RadiusX = 8,
                    RadiusY = 8,
                    Fill = on ? Ui.Paper3 : Ui.Brush("Paper2Brush"),
                    Stroke = on ? Brush(accent) : v.IsEmpty ? Ui.Muted : Ui.Rule,
                    StrokeThickness = on ? 1.6 : 1,
                    IsHitTestVisible = false,
                };
                if (v.IsEmpty && !on)
                {
                    cap.StrokeDashArray = Dash();
                }

                Add(cap, x, y);
                if (on)
                {
                    Add(new Rectangle { Width = kw, Height = kh, RadiusX = 8, RadiusY = 8, Fill = Brush(accent, 41), IsHitTestVisible = false }, x, y);
                }

                bool label = ShowLabel(i);
                if (v.IsEmpty)
                {
                    Label(label ? "Empty" : "+", 13.5 * k, Ui.Muted, x + 12 * k, y + (label ? 30 : 22) * k, kw - 20 * k, TextAlignment.Left);
                }
                else if (label)
                {
                    Icon(v, x + 12 * k + 8 * k, y + 16 * k, 16 * k, on ? Brush(accent) : Ui.Ink);
                    Label(v.Title, 13.5 * k, Ui.Ink, x + 12 * k, y + 30 * k, kw - 20 * k, TextAlignment.Left);
                    if (v.Sub.Length > 0)
                    {
                        Label(v.Sub, 10 * k, Ui.Muted, x + 12 * k, y + 49 * k, kw - 20 * k, TextAlignment.Left, mono: true);
                    }
                }
                else
                {
                    Icon(v, x + kw / 2, y + kh / 2, 24 * k, on ? Brush(accent) : Ui.Ink);
                }

                var hit = new Rectangle { Width = kw, Height = kh, RadiusX = 8, RadiusY = 8 };
                Wire(hit, i);
                Add(hit, x, y);
                Automate(hit, i, v);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Services.CrashLog.Write(e); // skip this slot, keep the rest of the ring
            }
        }

        double mx = ox + kw + gap, my = oy + kh + gap;
        Add(new Rectangle { Width = kw, Height = kh, RadiusX = 8, RadiusY = 8, Stroke = Ui.Rule, StrokeDashArray = [2, 4], IsHitTestVisible = false }, mx, my);
        _ring.Children.Add(new Line { X1 = cx - 8, X2 = cx + 8, Y1 = cy, Y2 = cy, Stroke = Brush(accent), StrokeThickness = 2, IsHitTestVisible = false });
        _ring.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = cy - 8, Y2 = cy + 8, Stroke = Brush(accent), StrokeThickness = 2, IsHitTestVisible = false });
        Label(_inFolder ? "Back" : "Esc", 10, Ui.Muted, mx, my + kh - 18, kw, TextAlignment.Center, mono: true);
        HubHit(cx, cy, 36 * k);
    }

    /// <summary>The centre: inside a folder a click goes back.</summary>
    private void HubHit(double cx, double cy, double radius)
    {
        var hit = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = Clear };
        hit.Tapped += (_, e) =>
        {
            if (_inFolder && !_muted)
            {
                e.Handled = true;
                BackRequested?.Invoke();
            }
        };
        if (_inFolder)
        {
            AutomationProperties.SetName(hit, "Back");
        }

        Add(hit, cx - radius, cy - radius);
    }

    private static void Automate(UIElement hit, int index, RingSlotView v) =>
        AutomationProperties.SetName(hit, $"Slot {index + 1}: {v.Title}" + (v.Sub.Length > 0 ? $", {v.Sub}" : string.Empty));
}
