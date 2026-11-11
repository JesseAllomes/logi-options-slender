using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Slender.Settings.Logic;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Slender.Settings.Views;

/// <summary>
/// The battery glyph: a rounded body with a thin outline, a terminal cap and an inner fill exactly as wide as the percent, drawn
/// as vector shapes from <see cref="BatteryGlyphGeometry"/> (so it is crisp at any DPI) with the label beside it. All decisions
/// come from <see cref="BatteryGlyphModel"/>. Colours are the theme's brushes; in high contrast it uses the system colours.
/// </summary>
internal static class BatteryGlyph
{
    /// <summary>The glyph with its label. <paramref name="textBrush"/> is the label colour when the state does not set its own.</summary>
    public static FrameworkElement Create(BatteryGlyphModel model, BatteryGlyphSize size, Brush? textBrush = null, bool compact = false)
    {
        BatteryGlyphGeometry g = BatteryGlyphGeometry.For(size, model.FillFraction);
        bool highContrast = new AccessibilitySettings().HighContrast;
        Brush ink = highContrast ? SystemBrush(UIColorType.Foreground) : Ui.Ink;
        Brush muted = highContrast ? ink : Ui.Muted;
        Brush ground = highContrast ? SystemBrush(UIColorType.Background) : Ui.Brush("PaperBrush");

        Brush outline = model.Kind == BatteryGlyphKind.Error && !highContrast ? Ui.Warn : muted;
        Brush fill = highContrast ? ink : model.Kind switch
        {
            BatteryGlyphKind.Low => Ui.Brush("DangerBrush"),
            BatteryGlyphKind.Charging or BatteryGlyphKind.Full => Ui.Live,
            _ => ink,
        };
        Brush label = highContrast ? ink : model.Kind switch
        {
            BatteryGlyphKind.Low => Ui.Brush("DangerBrush"),
            BatteryGlyphKind.Error => Ui.Warn,
            _ => textBrush ?? ink,
        };

        var canvas = new Canvas { Width = g.Width, Height = g.Height, VerticalAlignment = VerticalAlignment.Center };
        canvas.Children.Add(Place(new Rectangle
        {
            Width = g.Body.Width,
            Height = g.Body.Height,
            RadiusX = g.Body.Radius,
            RadiusY = g.Body.Radius,
            Stroke = outline,
            StrokeThickness = g.BodyStroke,
        }, g.Body));
        canvas.Children.Add(Place(new Rectangle { Width = g.Cap.Width, Height = g.Cap.Height, RadiusX = g.Cap.Radius, RadiusY = g.Cap.Radius, Fill = outline }, g.Cap));
        if (g.Fill.Width > 0)
        {
            canvas.Children.Add(Place(new Rectangle { Width = g.Fill.Width, Height = g.Fill.Height, RadiusX = g.Fill.Radius, RadiusY = g.Fill.Radius, Fill = fill }, g.Fill));
        }

        if (model.ShowBolt)
        {
            var points = new PointCollection();
            foreach ((double x, double y) in g.Bolt) points.Add(new Point(x, y));
            canvas.Children.Add(new Polygon { Points = points, Stroke = ground, StrokeThickness = g.BoltEdge * 2, StrokeLineJoin = PenLineJoin.Round, Fill = ground });
            canvas.Children.Add(new Polygon { Points = points, Fill = ink });
        }

        var text = Ui.Text(compact ? model.CompactLabel : model.Label, g.FontSize, label);
        text.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = g.TextGap, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(canvas);
        row.Children.Add(text);
        row.Opacity = model.Dimmed ? 0.5 : 1;
        AutomationProperties.SetName(row, model.AccessibleText);
        AutomationProperties.SetAccessibilityView(canvas, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(text, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return row;
    }

    private static Rectangle Place(Rectangle shape, GlyphRect rect)
    {
        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        return shape;
    }

    private static Brush SystemBrush(UIColorType type)
    {
        Windows.UI.Color c = new UISettings().GetColorValue(type);
        return new SolidColorBrush(c);
    }
}
