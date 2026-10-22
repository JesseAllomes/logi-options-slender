using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.Foundation;
using Windows.System;

namespace Slender.Settings;

public sealed partial class MainWindow
{
    private const double LabelHeight = 46;
    private const double LeftLabelWidth = 230;
    private const double RightLabelWidth = 250;

    private sealed class StageItem
    {
        public required ControlInfo Info { get; init; }
        public Point Position { get; set; }
        public Button? Hotspot { get; set; }
        public Ellipse? Halo { get; set; }
        public Ellipse? Dot { get; set; }
        public Ellipse? Core { get; set; }
        public Button? Label { get; set; }
        public Polyline? Leader { get; set; }
        public bool LabelOnLeft { get; set; }
        public double LabelWidth { get; set; }
    }

    private readonly Dictionary<ushort, StageItem> _items = [];
    private Image? _render;
    private bool _layoutQueued;

    // Keyboard stage: one floating label above the render and a list of changed keys below it.
    private Button? _kbLabel;
    private Polyline? _kbLeader;
    private StackPanel? _kbList;
    private Rect _kbRender;

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready || _page != Page.Device || _layoutQueued)
        {
            return;
        }

        _layoutQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _layoutQueued = false;
            if (_page == Page.Device)
            {
                LayoutStage();
            }
        });
    }

    private ushort SelectedControl
    {
        get
        {
            if (_selected.TryGetValue(_kind, out ushort id) && _items.ContainsKey(id))
            {
                return id;
            }

            ushort first = _items.Keys.FirstOrDefault();
            return first;
        }
    }

    private void LayoutStage()
    {
        StageCanvas.Children.Clear();
        _items.Clear();
        _render = null;
        _kbLabel = null;
        _kbLeader = null;
        _kbList = null;

        double w = StageLayer.ActualWidth;
        double h = StageLayer.ActualHeight;
        if (w < 80 || h < 80)
        {
            return;
        }

        CachedDevice? device = _devices.GetValueOrDefault(_kind);
        if (device is null)
        {
            LayoutControlList(w, h);
        }
        else if (_kind == DeviceKind.Mouse)
        {
            LayoutMouse(device, w, h);
        }
        else
        {
            LayoutKeyboard(device, w, h);
        }

        RefreshStageLabels();
        UpdateStageDim();
    }

    // ---- mouse: render with hotspots, leader lines and labels either side ----

    private void LayoutMouse(CachedDevice device, double w, double h)
    {
        DeviceLayout layout = device.Layout;
        double aspect = (double)device.ImageWidth / device.ImageHeight;
        double imgH = h - 28;
        double imgW = imgH * aspect;
        if (imgW > w * 0.4)
        {
            imgW = w * 0.4;
            imgH = imgW / aspect;
        }

        double left = (w - imgW) / 2 + 12;
        double top = Math.Max(10, (h - imgH) / 2);
        _render = new Image
        {
            Width = imgW,
            Height = imgH,
            Stretch = Stretch.Uniform,
            Source = Ui.Bitmap(device.ImagePath, decodePixelHeight: (int)Math.Ceiling(imgH * _scale)),
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(_render, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Canvas.SetLeft(_render, left);
        Canvas.SetTop(_render, top);
        StageCanvas.Children.Add(_render);

        var right = new List<(Hotspot Spot, Point Pos)>();
        var leftSide = new List<(Hotspot Spot, Point Pos)>();
        foreach (Hotspot spot in layout.Hotspots)
        {
            (double fx, double fy) = HotspotMapper.ToImageFraction(DeviceKind.Mouse, spot, layout, device.ImageWidth, device.ImageHeight);
            var pos = new Point(left + fx * imgW, top + fy * imgH);
            (spot.LabelSide < 0 ? leftSide : right).Add((spot, pos));
        }

        double rightEdge = left + imgW + 40;
        double rightWidth = Math.Min(RightLabelWidth, w - rightEdge - 18);
        double leftEdge = left - 40;
        double leftWidth = Math.Min(LeftLabelWidth, leftEdge - 18);

        PlaceLabelColumn(right, labelOnLeft: false, edge: rightEdge, width: rightWidth, minY: 72, maxY: h - 24);
        PlaceLabelColumn(leftSide, labelOnLeft: true, edge: leftEdge, width: leftWidth, minY: 110, maxY: h - 24);

        // Leader lines go below the hotspots and labels; hotspots on top.
        foreach (StageItem item in _items.Values)
        {
            StageCanvas.Children.Insert(1, item.Leader!);
        }

        foreach (StageItem item in _items.Values)
        {
            StageCanvas.Children.Add(item.Label!);
        }

        foreach (StageItem item in _items.Values)
        {
            StageCanvas.Children.Add(item.Hotspot!);
        }

        // Gesture swipe rows: one strip of labels along the bottom (no hotspots on the render). Click is the gesture button's own label.
        double stripWidth = Math.Min(w - 36, 4 * 190 + 3 * 8);
        double cell = (stripWidth - 3 * 8) / 4;
        double stripLeft = (w - stripWidth) / 2;
        // Thumb wheel touch and tap live in the thumb wheel card (user 2026-10-19).
        IReadOnlyList<ControlInfo> swipes = ControlCatalog.SwipeControls;
        for (int i = 0; i < swipes.Count; i++)
        {
            var swipeItem = new StageItem { Info = swipes[i], LabelWidth = cell };
            swipeItem.Position = new Point(stripLeft + i * (cell + 8) + cell / 2, h - LabelHeight / 2 - 6);
            swipeItem.Label =MakeLabelButton(swipeItem, cell);
            Canvas.SetLeft(swipeItem.Label, stripLeft + i * (cell + 8));
            Canvas.SetTop(swipeItem.Label, h - LabelHeight - 6);
            StageCanvas.Children.Add(swipeItem.Label);
            _items[swipes[i].Id] = swipeItem;
        }
    }

    private void PlaceLabelColumn(List<(Hotspot Spot, Point Pos)> column, bool labelOnLeft, double edge, double width, double minY, double maxY)
    {
        if (column.Count == 0 || width < 120)
        {
            return;
        }

        column.Sort((a, b) => a.Pos.Y.CompareTo(b.Pos.Y));
        double[] ys = LabelLayout.SpreadRows(column.Select(c => c.Pos.Y).ToArray(), LabelHeight + 14, minY, maxY);
        for (int i = 0; i < column.Count; i++)
        {
            (Hotspot spot, Point pos) = column[i];
            double ly = ys[i];
            var info = new ControlInfo(spot.ControlId, spot.Name, ControlCatalog.Find(_kind, spot.ControlId)?.NativeText ?? spot.Name);
            var item = new StageItem { Info = info, Position = pos, LabelOnLeft = labelOnLeft, LabelWidth = width };

            item.Leader = new Polyline
            {
                Stroke = Ui.Brush("LeaderBrush"),
                StrokeThickness = 1,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                Points = labelOnLeft
                    ? [pos, new Point(edge + 16, ly), new Point(edge, ly)]
                    : [pos, new Point(edge - 16, ly), new Point(edge, ly)],
            };

            item.Label = MakeLabelButton(item, width);
            Canvas.SetLeft(item.Label, labelOnLeft ? edge - 6 - width : edge + 6);
            Canvas.SetTop(item.Label, ly - LabelHeight / 2);
            MakeHotspot(item, spot.ControlId);
            _items[spot.ControlId] = item;
        }
    }

    private void MakeHotspot(StageItem item, ushort id)
    {
        item.Halo = new Ellipse { Width = 32, Height = 32, Fill = Ui.MarkSoft, Visibility = Visibility.Collapsed };
        item.Dot = new Ellipse { Width = 18, Height = 18, Fill = Ui.Brush("HotspotFillBrush"), Stroke = Ui.Mark, StrokeThickness = 2 };
        item.Core = new Ellipse { Width = 6, Height = 6, Fill = Ui.Mark, Visibility = Visibility.Collapsed };
        var grid = new Grid { Width = 34, Height = 34 };
        grid.Children.Add(item.Halo);
        grid.Children.Add(item.Dot);
        grid.Children.Add(item.Core);
        foreach (FrameworkElement e in grid.Children)
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            e.VerticalAlignment = VerticalAlignment.Center;
        }

        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Content = grid,
        };
        AutomationProperties.SetName(button, item.Info.Name);
        ToolTipService.SetToolTip(button, item.Info.Tip);
        Canvas.SetLeft(button, item.Position.X - 17);
        Canvas.SetTop(button, item.Position.Y - 17);
        button.Click += (_, _) => ActivateControl(id);
        button.GotFocus += (_, _) => SelectControl(id);
        button.PointerEntered += (_, _) => item.Dot!.Fill = Ui.MarkSoft;
        button.PointerExited += (_, _) => RefreshItemVisual(item, id);
        item.Hotspot = button;
    }

    private Button MakeLabelButton(StageItem item, double width)
    {
        ushort id = item.Info.Id;
        var button = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = width,
            Height = LabelHeight,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 4, 9, 4),
            HorizontalContentAlignment = item.LabelOnLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => ActivateControl(id);
        button.GotFocus += (_, _) => SelectControl(id);
        ToolTipService.SetToolTip(button, item.Info.Tip);
        return button;
    }

    // ---- keyboard: render with top-row hotspots, one floating label and the changed-keys list ----

    private void LayoutKeyboard(CachedDevice device, double w, double h)
    {
        DeviceLayout layout = device.Layout;
        double aspect = (double)device.ImageWidth / device.ImageHeight;
        const double ViewShare = 0.72; // like Options+, show the keyboard and only the top of the wrist rest
        double listHeight = 150;
        double top = 96;
        double imgW = Math.Min(Math.Min(w - 48, 880), (h - top - 24 - listHeight) / (ViewShare / aspect));
        imgW = Math.Max(imgW, 360);
        double imgH = imgW / aspect;
        double viewH = imgH * ViewShare;
        double left = (w - imgW) / 2;
        _kbRender = new Rect(left, top, imgW, viewH);

        var frame = new Grid { Width = imgW, Height = viewH, Clip = new RectangleGeometry { Rect = new Rect(0, 0, imgW, viewH) }, IsHitTestVisible = false };
        _render = new Image
        {
            Width = imgW,
            Height = imgH,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top,
            Source = Ui.Bitmap(device.ImagePath, decodePixelWidth: (int)Math.Ceiling(imgW * _scale)),
        };
        AutomationProperties.SetAccessibilityView(frame, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        frame.Children.Add(_render);
        // The picture is cut off like Options+ does; fade the cut into the window ground instead of a hard edge.
        frame.Children.Add(new Rectangle
        {
            Height = viewH * 0.4,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0x1C, 0x1F, 0x22), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x1C, 0x1F, 0x22), Offset = 1 },
                },
            },
        });
        Canvas.SetLeft(frame, left);
        Canvas.SetTop(frame, top);
        StageCanvas.Children.Add(frame);

        foreach (Hotspot spot in layout.Hotspots)
        {
            (double fx, double fy) = HotspotMapper.ToImageFraction(DeviceKind.Keyboard, spot, layout, device.ImageWidth, device.ImageHeight);
            var pos = new Point(left + fx * imgW, top + fy * imgH);
            var info = new ControlInfo(spot.ControlId, spot.Name, ControlCatalog.Find(_kind, spot.ControlId)?.NativeText ?? spot.Name);
            var item = new StageItem { Info = info, Position = pos };
            MakeHotspot(item, spot.ControlId);
            _items[spot.ControlId] = item;
        }

        _kbLeader = new Polyline { Stroke = Ui.Mark, StrokeThickness = 1.5, IsHitTestVisible = false };
        StageCanvas.Children.Add(_kbLeader);

        _kbLabel = new Button
        {
            Style = Ui.Res<Style>("PlainButtonStyle"),
            Width = 250,
            Height = LabelHeight,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 4, 9, 4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _kbLabel.Click += (_, _) => ActivateControl(SelectedControl);
        StageCanvas.Children.Add(_kbLabel);

        _kbList = new StackPanel { Spacing = 6, Width = Math.Max(imgW, 420) };
        Canvas.SetLeft(_kbList, left);
        Canvas.SetTop(_kbList, top + viewH + 18);
        StageCanvas.Children.Add(_kbList);

        foreach (StageItem item in _items.Values)
        {
            StageCanvas.Children.Add(item.Hotspot!);
        }
    }

    private void RefreshKeyboardLabel()
    {
        if (_kbLabel is null || _kbLeader is null || !_items.TryGetValue(SelectedControl, out StageItem? item))
        {
            return;
        }

        _kbLabel.Content = LabelContent(item, leftAligned: false);
        bool flip = item.Position.X > _kbRender.X + _kbRender.Width * 0.7;
        double y = _kbRender.Y - 34;
        // The app heading sits top left; keep the label clear of it.
        double labelLeft = flip ? item.Position.X - 36 - _kbLabel.Width : Math.Max(item.Position.X + 36, 470);
        double elbow = flip ? labelLeft + _kbLabel.Width + 6 : labelLeft - 6;
        _kbLeader.Points =
        [
            new Point(item.Position.X, item.Position.Y),
            new Point(item.Position.X, y),
            new Point(elbow, y),
        ];
        Canvas.SetLeft(_kbLabel, labelLeft);
        Canvas.SetTop(_kbLabel, y - LabelHeight / 2);
        _kbLabel.Background = Ui.MarkSoft;
        _kbLabel.BorderBrush = Ui.Mark;
        AutomationProperties.SetName(_kbLabel, item.Info.Name + ": " + PlainActionText(item.Info));
        ToolTipService.SetToolTip(_kbLabel, item.Info.Tip);
    }

    private void RefreshKeyboardList()
    {
        if (_kbList is null)
        {
            return;
        }

        _kbList.Children.Clear();
        string appName = _app is null ? "All apps" : ProfileOps.AppName(ProfileOps.Find(_config.Config, _app)!);
        _kbList.Children.Add(Ui.Text(_app is null ? "Keys you changed" : $"Changed in {appName}", 12, Ui.Muted, Ui.Heading));

        var shown = ControlCatalog.KeyboardControls
            .Where(c => _items.ContainsKey(c.Id) && ProfileOps.Own(_config.Config, _app, DeviceKind.Keyboard, c.Id) is not null)
            .ToList();
        if (shown.Count == 0)
        {
            _kbList.Children.Add(Ui.Text(_app is null
                ? "Select a key on the keyboard to change what it does."
                : $"Nothing changed. Select a key to change what it does in {appName}.", 13, Ui.Muted));
            AddSpareKeysRow();
            return;
        }

        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0;
        for (int i = 0; i < shown.Count && i < 6; i++)
        {
            if (i % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row = i / 2;
            }

            ControlInfo info = shown[i];
            bool isSelected = info.Id == SelectedControl;
            var button = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 4, 9, 4),
                Height = LabelHeight,
                Background = isSelected ? Ui.MarkSoft : Ui.Brush("RailTransparent"),
                BorderBrush = isSelected ? Ui.Mark : Ui.Brush("RuleBrush"),
                Content = LabelContent(_items[info.Id], leftAligned: false),
            };
            ushort id = info.Id;
            button.Click += (_, _) => ActivateControl(id);
            Grid.SetColumn(button, i % 2);
            Grid.SetRow(button, row);
            grid.Children.Add(button);
        }

        _kbList.Children.Add(grid);
        AddSpareKeysRow();
    }

    // ---- no Options+ assets: a plain, designed list of controls ----

    private void LayoutControlList(double w, double h)
    {
        ControlInfo[] controls = [.. ControlCatalog.For(_kind)];
        bool twoColumns = controls.Length > 8;
        double colWidth = 300;
        double listWidth = twoColumns ? colWidth * 2 + 12 : 380;
        double silhouetteW = _kind == DeviceKind.Mouse ? 150 : 0;
        double total = listWidth + (silhouetteW > 0 ? silhouetteW + 56 : 0);
        double left = Math.Max(32, (w - total) / 2);
        double top = 124;

        if (_kind == DeviceKind.Mouse)
        {
            var silhouette = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                    "M50,4 C80,4 96,40 94,84 C92,130 80,156 50,156 C26,156 10,134 8,100 C6,60 22,4 50,4 Z"),
                Fill = Ui.Paper3,
                Stroke = Ui.Rule,
                StrokeThickness = 1,
                Stretch = Stretch.Uniform,
                Width = silhouetteW,
                Height = silhouetteW * 1.6,
                Opacity = 0.9,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(silhouette, left);
            Canvas.SetTop(silhouette, top);
            StageCanvas.Children.Add(silhouette);
            _render = null;
            left += silhouetteW + 56;
        }

        StageCanvas.Children.Add(MuteNote(left, top - 34));
        for (int i = 0; i < controls.Length; i++)
        {
            int column = twoColumns ? i / 8 : 0;
            int row = twoColumns ? i % 8 : i;
            var item = new StageItem { Info = controls[i], LabelWidth = colWidth };
            item.Label = MakeLabelButton(item, twoColumns ? colWidth : listWidth);
            Canvas.SetLeft(item.Label, left + column * (colWidth + 12));
            Canvas.SetTop(item.Label, top + row * (LabelHeight + 8));
            StageCanvas.Children.Add(item.Label);
            _items[controls[i].Id] = item;
        }
    }

    private static FrameworkElement MuteNote(double left, double top)
    {
        var note = Ui.Text("Device pictures are not available here, so controls are listed.", 12, Ui.Muted);
        Canvas.SetLeft(note, left);
        Canvas.SetTop(note, top);
        return note;
    }

    // ---- shared: label text, selection visuals ----

    private string PlainActionText(ControlInfo info)
    {
        (ActionSpec? action, bool _) = CurrentProfile.Effective(_kind, info.Id);
        return action is null ? info.NativeText : ActionCatalog.Plain(action);
    }

    private FrameworkElement LabelContent(StageItem item, bool leftAligned)
    {
        ControlInfo info = item.Info;
        (ActionSpec? action, bool inherited) = CurrentProfile.Effective(_kind, info.Id);
        HorizontalAlignment align = leftAligned ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var panel = new StackPanel { Spacing = 1, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(info.Name, 11.5, Ui.Muted);
        name.HorizontalAlignment = align;
        panel.Children.Add(name);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = align };
        if (action is null)
        {
            row.Children.Add(Ui.Text(_app is null ? info.NativeText : info.NativeText + (ProfileOps.IsPassThrough(_config.Config, _app, _kind, info.Id) ? string.Empty : " · same as All apps"), 13, _app is null ? Ui.Ink : Ui.Muted));
        }
        else
        {
            ActionLabel label = ActionCatalog.Describe(action);
            if (inherited)
            {
                string text = label.Text.Length > 0 ? label.Text : string.Join('+', label.Chips);
                row.Children.Add(Ui.Text(text + " · same as All apps", 13, Ui.Muted));
            }
            else
            {
                if (label.Text.Length > 0)
                {
                    row.Children.Add(Ui.Text(label.Text, 13));
                }

                if (label.Chips.Count > 0)
                {
                    row.Children.Add(Ui.Chips(label.Chips));
                }
            }
        }

        panel.Children.Add(row);
        return panel;
    }

    private void RefreshStageLabels()
    {
        if (_page != Page.Device)
        {
            return;
        }

        foreach ((ushort id, StageItem item) in _items)
        {
            if (item.Label is not null && _kbLabel is null)
            {
                item.Label.Content = LabelContent(item, item.LabelOnLeft);
                AutomationProperties.SetName(item.Label, item.Info.Name + ": " + PlainActionText(item.Info));
                ToolTipService.SetToolTip(item.Label, item.Info.Tip);
            }

            RefreshItemVisual(item, id);
        }

        if (_kind == DeviceKind.Keyboard)
        {
            RefreshKeyboardLabel();
            RefreshKeyboardList();
        }
    }

    private void RefreshItemVisual(StageItem item, ushort id)
    {
        bool selected = id == SelectedControl;
        bool changed = ProfileOps.Own(_config.Config, _app, _kind, id) is not null;

        if (item.Dot is not null)
        {
            item.Dot.Fill = selected ? Ui.Mark : Ui.Brush("HotspotFillBrush");
            item.Halo!.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            item.Core!.Visibility = changed && !selected ? Visibility.Visible : Visibility.Collapsed;
        }

        if (item.Label is not null && _kbLabel is null)
        {
            item.Label.Background = selected ? Ui.MarkSoft : Ui.Brush("RailTransparent");
            item.Label.BorderBrush = selected ? Ui.Mark : Ui.Brush("RailTransparent");
        }

        if (item.Leader is not null)
        {
            item.Leader.Stroke = selected ? Ui.Mark : Ui.Brush("LeaderBrush");
            item.Leader.StrokeThickness = selected ? 1.5 : 1;
        }
    }

    private void SelectControl(ushort id)
    {
        if (!_items.ContainsKey(id) || (_selected.TryGetValue(_kind, out ushort cur) && cur == id))
        {
            return;
        }

        _selected[_kind] = id;
        foreach ((ushort key, StageItem item) in _items)
        {
            RefreshItemVisual(item, key);
        }

        if (_kind == DeviceKind.Keyboard)
        {
            RefreshKeyboardLabel();
            RefreshKeyboardList();
        }
    }

    private void UpdateStageDim()
    {
        DeviceState? state = _status.Device(ControlCatalog.DeviceId(_kind));
        string id = ControlCatalog.DeviceId(_kind);
        bool offline = _status.Status is not null && (DevicePresenceView.Dimmed(_status.Status, id) || !DevicePresenceView.Visible(_status.Status, id));
        if (_render is not null)
        {
            _render.Opacity = offline ? 0.5 : 1;
        }
    }

    private bool FocusSelectedHotspot(bool keyboard = true)
    {
        if (_page != Page.Device || !_items.TryGetValue(SelectedControl, out StageItem? item))
        {
            return false;
        }

        UIElement? target = item.Hotspot ?? item.Label;
        return target is Control c && c.Focus(keyboard ? FocusState.Keyboard : FocusState.Programmatic);
    }

    // ---- arrow keys move between hotspots ----

    private void OnStageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        (double dx, double dy) = e.Key switch
        {
            VirtualKey.Left => (-1.0, 0.0),
            VirtualKey.Right => (1.0, 0.0),
            VirtualKey.Up => (0.0, -1.0),
            VirtualKey.Down => (0.0, 1.0),
            _ => (0.0, 0.0),
        };
        if (dx == 0 && dy == 0 || _items.Count < 2)
        {
            return;
        }

        if (e.OriginalSource is not FrameworkElement source || FindItem(source) is not { } from)
        {
            return;
        }

        StageItem? best = null;
        double bestScore = double.MaxValue;
        foreach (StageItem candidate in _items.Values)
        {
            if (ReferenceEquals(candidate, from))
            {
                continue;
            }

            double vx = candidate.Position.X - from.Position.X;
            double vy = candidate.Position.Y - from.Position.Y;
            double along = vx * dx + vy * dy;
            if (along <= 1)
            {
                continue;
            }

            double across = Math.Abs(vx * dy - vy * dx);
            double score = along + 2 * across;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is null)
        {
            return;
        }

        e.Handled = true;
        (best.Hotspot ?? best.Label)?.Focus(FocusState.Keyboard);
    }

    private StageItem? FindItem(FrameworkElement source)
    {
        foreach (StageItem item in _items.Values)
        {
            if ((item.Hotspot is not null && IsInside(source, item.Hotspot)) || (item.Label is not null && IsInside(source, item.Label)))
            {
                return item;
            }
        }

        return null;
    }

    private void ActivateControl(ushort id)
    {
        SelectControl(id);
        if (!_items.TryGetValue(id, out StageItem? item))
        {
            return;
        }

        FrameworkElement? anchor = _kind == DeviceKind.Keyboard && _kbLabel is not null ? _kbLabel : item.Label ?? item.Hotspot;
        if (anchor is null)
        {
            return;
        }

        if (_kind == DeviceKind.Mouse && id == DeviceIds.ThumbWheelPseudoId)
        {
            ShowThumbCard(anchor, item.Info); // the wheel's card: scroll action, second action, switch button
        }
        else
        {
            ShowPicker(anchor, item.Info);
        }
    }

    // ---- heading ----

    private void UpdateHeading()
    {
        Profile? profile = ProfileOps.Find(_config.Config, _app);
        string name = _app is null ? "All apps" : profile is null ? _app : ProfileOps.AppName(profile);
        HeadingTitle.Text = name;
        int changed = ProfileOps.ChangedCount(_config.Config, _app, _kind);
        HeadingSub.Text = _app is null
            ? (_kind == DeviceKind.Mouse ? "Default for every app" : "Default for every app")
            : ProfileOps.Subtitle(changed, _kind, isAllApps: false);

        if (HeadingIconHost.Tag as string != (_app ?? "*"))
        {
            HeadingIconHost.Tag = _app ?? "*";
            HeadingIconHost.Children.Clear();
            HeadingIconHost.Children.Add(AppIcon(profile is { AppProcess: not null } ? profile : null, 46));
        }
    }
}
