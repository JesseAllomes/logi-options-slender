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
/// leader line, label). The pictures ship with Slender (BuiltInPictures); the callout positions are measured on them.
/// </summary>
public sealed partial class MainWindow
{
    private const double HsStageWidth = 660, HsStageHeight = 440, HsPicture = 400, HsLabelWidth = 150, HsLabelHeight = 44;

    /// <summary>A base setting as the base stores it, by env key (the list the tray published), or null.</summary>
    private static string? HeadsetSettingRaw(HeadsetState? h, string key) => h?.Settings?.FirstOrDefault(s => s.Key == key)?.Raw;

    private void AddHeadsetPicture(HeadsetState? h, YealinkSettings y, CachedDevice device)
    {
        PagePanel.Children.Add(new Border { Height = 12 });
        var stage = new Canvas { Width = HsStageWidth, Height = HsStageHeight, HorizontalAlignment = HorizontalAlignment.Left };
        LayoutHeadsetPicture(stage, device, h, y);
        PagePanel.Children.Add(stage);
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

    private void LayoutHeadsetPicture(Canvas stage, CachedDevice device, HeadsetState? h, YealinkSettings y)
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
}
