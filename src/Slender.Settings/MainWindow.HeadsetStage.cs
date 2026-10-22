using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;
using Windows.Foundation;

namespace Slender.Settings;

/// <summary>
/// The Headset page's picture (m55): the WH64 or its base with callouts, in the same visual language as the mouse page (copper dot,
/// leader line, label). The pictures are the user's own, copied from their Yealink USB Connect install into the same cache the
/// mouse and keyboard pictures use; the callout positions are ours (YealinkPictures). Without USB Connect a plain drawing is shown.
/// </summary>
public sealed partial class MainWindow
{
    private const double HsStageWidth = 660, HsStageHeight = 440, HsPicture = 400, HsLabelWidth = 150, HsLabelHeight = 44;

    private readonly Dictionary<YealinkView, CachedDevice?> _yealinkViews = [];
    private readonly HashSet<YealinkView> _yealinkLoading = [];

    /// <summary>A base setting as the base stores it, by env key (the list the tray published), or null.</summary>
    private static string? HeadsetSettingRaw(HeadsetState? h, string key) => h?.Settings?.FirstOrDefault(s => s.Key == key)?.Raw;

    private void AddHeadsetPicture(HeadsetState? h, YealinkSettings y, YealinkView view)
    {
        PagePanel.Children.Add(new Border { Height = 12 });
        if (!_yealinkViews.TryGetValue(view, out CachedDevice? device))
        {
            LoadYealinkView(view);
        }

        var stage = new Canvas { Width = HsStageWidth, Height = HsStageHeight, HorizontalAlignment = HorizontalAlignment.Left };
        if (device is not null)
        {
            LayoutHeadsetPicture(stage, device, view, h, y);
        }
        else
        {
            LayoutHeadsetFallback(stage, view, h, y);
        }

        PagePanel.Children.Add(stage);

        if (device is null && _yealinkViews.ContainsKey(view))
        {
            AddHeadsetNote("The pictures come from Yealink USB Connect on this PC. It isn't installed here, so a plain drawing is shown.");
        }
    }

    private static string? FindYealinkDrawable() =>
        YealinkPictures.FindInstall(InstalledPrograms.Entries(), Directory.Exists);

    /// <summary>
    /// Copies the picture off the UI thread the first time it is needed, then redraws the page. Every other view is copied
    /// too, so Slender keeps all of them and USB Connect can be uninstalled afterwards. A miss is remembered so it is not retried.
    /// </summary>
    private void LoadYealinkView(YealinkView view)
    {
        if (!_yealinkLoading.Add(view))
        {
            return;
        }

        _ = Task.Run(() =>
            {
                string? drawable = FindYealinkDrawable();
                var found = new Dictionary<YealinkView, CachedDevice?>();
                foreach (YealinkView each in new[] { view }.Concat(Enum.GetValues<YealinkView>()).Distinct())
                {
                    found[each] = _assets.EnsureFrom(YealinkPictures.DeviceId(each), dir => YealinkPictures.Copy(each, drawable, dir))
                        ?? _assets.EnsureFrom(YealinkPictures.LegacyDeviceId(each), _ => null);
                }

                return found;
            })
            .ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
            {
                _yealinkLoading.Remove(view);
                foreach (YealinkView each in Enum.GetValues<YealinkView>())
                {
                    _yealinkViews[each] = t.IsCompletedSuccessfully ? t.Result.GetValueOrDefault(each) : null;
                }

                BuildRail();
                if (_page is Page.Headset or Page.Base)
                {
                    RebuildHeadsetPage();
                }
            }), TaskScheduler.Default);
    }

    /// <summary>What a callout says, and the action when clicking it should open a picker (the two buttons Slender can run an action for).</summary>
    private (string Value, Func<YealinkSettings, ActionSpec>? Binding, Func<YealinkSettings, ActionSpec, YealinkSettings>? Set) HeadsetCallout(string slot, HeadsetState? h, YealinkSettings y) => slot switch
    {
        YealinkSlots.CallButton => (HeadsetActionText(y.Hook), s => s.Hook, (s, a) => s with { Hook = a }),
        YealinkSlots.TeamsButton => (HeadsetActionText(y.TeamsButton), s => s.TeamsButton, (s, a) => s with { TeamsButton = a }),
        YealinkSlots.Battery => (BatteryCallout(h), null, null),
        YealinkSlots.Busylight => (BusylightCallout(h), null, null),
        YealinkSlots.BoomMute => (y.MuteSync ? "Syncs Windows microphone" : "Headset only", null, null),
        YealinkSlots.ChargeLight => (h?.Charging switch { true => "Charging", false => h.Docked == true ? "Docked" : "Not charging", _ => "Charging light" }, null, null),
        YealinkSlots.MuteLight => ("Shows mute", null, null),
        YealinkSlots.CallLight => ("Shows calls", null, null),
        _ => ("Touch key", null, null),
    };

    private static string BusylightCallout(HeadsetState? h) =>
        (HeadsetSettingRaw(h, "busylight_built_in.manual_enable"), HeadsetSettingRaw(h, "busylight_built_in.sync_enable")) switch
        {
            ("1", _) => "On (busy)",
            (_, "1") => "Shows calls",
            ("0", "0") => "Off",
            _ => "Shows call status",
        };

    private static string BatteryCallout(HeadsetState? h)
    {
        if (h?.BatteryPercent is not { } p)
        {
            return "Not reported";
        }

        string state = h.Charging == true ? " · charging" : h.Docked == true ? " · docked" : string.Empty;
        return $"{p}%{state}";
    }

    private void LayoutHeadsetPicture(Canvas stage, CachedDevice device, YealinkView view, HeadsetState? h, YealinkSettings y)
    {
        double left = (HsStageWidth - HsPicture) / 2, top = 20;
        var image = new Image
        {
            Width = HsPicture,
            Height = HsPicture,
            Stretch = Stretch.Uniform,
            Source = Ui.Bitmap(device.ImagePath, decodePixelHeight: (int)Math.Ceiling(HsPicture * _scale)),
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        stage.Children.Add(image);

        var leftSide = new List<(Hotspot Spot, Point Pos)>();
        var rightSide = new List<(Hotspot Spot, Point Pos)>();
        foreach (Hotspot spot in device.Layout.Hotspots)
        {
            var pos = new Point(left + spot.X / 100 * HsPicture, top + spot.Y / 100 * HsPicture);
            (spot.LabelSide < 0 ? leftSide : rightSide).Add((spot, pos));
        }

        var leaders = new List<UIElement>();
        var dots = new List<UIElement>();
        var labels = new List<UIElement>();
        PlaceHeadsetColumn(leftSide, true, HsLabelWidth + 6, h, y, leaders, dots, labels);
        PlaceHeadsetColumn(rightSide, false, HsStageWidth - HsLabelWidth - 6, h, y, leaders, dots, labels);
        foreach (UIElement e in leaders.Concat(dots).Concat(labels))
        {
            stage.Children.Add(e);
        }
    }

    private void PlaceHeadsetColumn(List<(Hotspot Spot, Point Pos)> column, bool onLeft, double edge, HeadsetState? h, YealinkSettings y,
        List<UIElement> leaders, List<UIElement> dots, List<UIElement> labels)
    {
        if (column.Count == 0)
        {
            return;
        }

        column.Sort((a, b) => a.Pos.Y.CompareTo(b.Pos.Y));
        double[] ys = LabelLayout.SpreadRows(column.Select(c => c.Pos.Y).ToArray(), HsLabelHeight + 10, HsLabelHeight / 2 + 4, HsStageHeight - HsLabelHeight / 2 - 4);
        for (int i = 0; i < column.Count; i++)
        {
            (Hotspot spot, Point pos) = column[i];
            leaders.Add(new Polyline
            {
                Stroke = Ui.Brush("LeaderBrush"),
                StrokeThickness = 1,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                Points = onLeft ? [pos, new Point(edge + 16, ys[i]), new Point(edge, ys[i])] : [pos, new Point(edge - 16, ys[i]), new Point(edge, ys[i])],
            });

            var dot = new Ellipse { Width = 16, Height = 16, Fill = Ui.Brush("HotspotFillBrush"), Stroke = Ui.Mark, StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(dot, pos.X - 8);
            Canvas.SetTop(dot, pos.Y - 8);
            dots.Add(dot);

            var (value, binding, set) = HeadsetCallout(spot.Slot, h, y);
            var content = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(spot.Name, 11.5, Ui.Muted);
            var text = Ui.Text(value, 13, Ui.Ink);
            name.TextTrimming = text.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextAlignment = text.TextAlignment = onLeft ? TextAlignment.Right : TextAlignment.Left;
            content.Children.Add(name);
            content.Children.Add(text);

            FrameworkElement label;
            if (set is not null)
            {
                var button = new Button
                {
                    Style = Ui.Res<Style>("PlainButtonStyle"),
                    Width = HsLabelWidth,
                    Height = HsLabelHeight,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Ui.Brush("RailTransparent"),
                    Background = Ui.Brush("RailTransparent"),
                    Padding = new Thickness(9, 4, 9, 4),
                    HorizontalContentAlignment = onLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = content,
                };
                string title = spot.Name;
                ActionSpec current = binding!(y);
                button.Click += (_, _) => ShowHeadsetPicker(button, title, current, set);
                ToolTipService.SetToolTip(button, $"{spot.Name}: click to choose what it does");
                label = button;
            }
            else
            {
                label = new Border { Width = HsLabelWidth, Height = HsLabelHeight, Padding = new Thickness(9, 4, 9, 4), Child = content };
            }

            AutomationProperties.SetName(label, $"{spot.Name}: {value}");
            Canvas.SetLeft(label, onLeft ? edge - 6 - HsLabelWidth : edge + 6);
            Canvas.SetTop(label, ys[i] - HsLabelHeight / 2);
            labels.Add(label);
        }
    }

    /// <summary>No USB Connect pictures: a plain headset or base drawing in the middle, the callouts as two columns without leader lines.</summary>
    private void LayoutHeadsetFallback(Canvas stage, YealinkView view, HeadsetState? h, YealinkSettings y)
    {
        double cx = HsStageWidth / 2, cy = HsStageHeight / 2;
        Brush line = Ui.Muted;
        if (view == YealinkView.Base)
        {
            AddShape(stage, new Rectangle { Width = 170, Height = 44, RadiusX = 8, RadiusY = 8, Fill = Ui.Paper3, Stroke = line, StrokeThickness = 2 }, cx - 85, cy + 20);
            AddShape(stage, new Rectangle { Width = 56, Height = 120, RadiusX = 10, RadiusY = 10, Fill = Ui.Paper3, Stroke = line, StrokeThickness = 2 }, cx - 28, cy - 90);
        }
        else
        {
            var figure = new PathFigure { StartPoint = new Point(cx - 62, cy + 10) };
            figure.Segments.Add(new ArcSegment { Point = new Point(cx + 62, cy + 10), Size = new Size(62, 95), SweepDirection = SweepDirection.Clockwise });
            var arc = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = new PathGeometry { Figures = { figure } },
                Stroke = line,
                StrokeThickness = 8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            AddShape(stage, arc, 0, 0);
            AddShape(stage, new Rectangle { Width = 40, Height = 70, RadiusX = 14, RadiusY = 14, Fill = Ui.Paper3, Stroke = line, StrokeThickness = 2 }, cx - 82, cy - 5);
            AddShape(stage, new Rectangle { Width = 40, Height = 70, RadiusX = 14, RadiusY = 14, Fill = Ui.Paper3, Stroke = line, StrokeThickness = 2 }, cx + 42, cy - 5);
        }

        var spots = YealinkPictures.Hotspots(view);
        for (int side = -1; side <= 1; side += 2)
        {
            var column = spots.Where(s => s.LabelSide == side || (side > 0 && s.LabelSide == 0)).ToList();
            for (int i = 0; i < column.Count; i++)
            {
                var (value, _, _) = HeadsetCallout(column[i].Slot, h, y);
                var content = new StackPanel { Spacing = 1 };
                content.Children.Add(Ui.Text(column[i].Name, 11.5, Ui.Muted));
                content.Children.Add(Ui.Text(value, 13, Ui.Ink));
                var label = new Border { Width = HsLabelWidth, Height = HsLabelHeight, Padding = new Thickness(9, 4, 9, 4), Child = content };
                AutomationProperties.SetName(label, $"{column[i].Name}: {value}");
                Canvas.SetLeft(label, side < 0 ? 0 : HsStageWidth - HsLabelWidth);
                Canvas.SetTop(label, 20 + i * (HsLabelHeight + 12));
                stage.Children.Add(label);
            }
        }
    }

    private static void AddShape(Canvas stage, UIElement shape, double x, double y)
    {
        shape.IsHitTestVisible = false;
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        stage.Children.Add(shape);
    }
}
