using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Slender.Settings.Views;

/// <summary>
/// The Galaxy Buds drawn as plain vector shapes (no picture ships): one earbud, the case, or a pair. Theme brushes only, so it follows
/// light, dark and high contrast. <c>scale</c> 1 is the page hero; the rail uses a smaller one.
/// </summary>
internal static class BudsArt
{
    public const double BudWidth = 34, BudHeight = 54, CaseWidth = 64, CaseHeight = 46;

    /// <summary>One earbud, body on top and the stem below it, on the outside edge so a left and right bud mirror each other.</summary>
    public static Canvas Bud(bool left, double scale = 1)
    {
        double s = scale;
        var canvas = new Canvas { Width = BudWidth * s, Height = BudHeight * s };
        double stemX = left ? 4 : 21;
        canvas.Children.Add(At(new Rectangle { Width = 9 * s, Height = 30 * s, RadiusX = 4.5 * s, RadiusY = 4.5 * s, Fill = Ui.Brush("PaperBrush"), Stroke = Ui.Ink, StrokeThickness = 1.5 }, stemX * s, 22 * s));
        canvas.Children.Add(At(new Ellipse { Width = 32 * s, Height = 30 * s, Fill = Ui.Brush("PaperBrush"), Stroke = Ui.Ink, StrokeThickness = 1.5 }, 1 * s, 1 * s));
        canvas.Children.Add(At(new Ellipse { Width = 11 * s, Height = 11 * s, Stroke = Ui.Muted, StrokeThickness = 1.2 }, (left ? 12 : 11) * s, 10 * s));
        return canvas;
    }

    /// <summary>The charging case with its lid line and a small light (green while it charges).</summary>
    public static Canvas Case(bool lit, double scale = 1)
    {
        double s = scale;
        var canvas = new Canvas { Width = CaseWidth * s, Height = CaseHeight * s };
        canvas.Children.Add(At(new Rectangle { Width = (CaseWidth - 3) * s, Height = (CaseHeight - 3) * s, RadiusX = 16 * s, RadiusY = 16 * s, Fill = Ui.Brush("PaperBrush"), Stroke = Ui.Ink, StrokeThickness = 1.5 }, 1.5 * s, 1.5 * s));
        canvas.Children.Add(At(new Rectangle { Width = (CaseWidth - 20) * s, Height = 1.5, Fill = Ui.Muted, Opacity = 0.7 }, 10 * s, 15 * s));
        canvas.Children.Add(At(new Ellipse { Width = 5 * s, Height = 5 * s, Fill = lit ? Ui.Live : Ui.Muted }, (CaseWidth / 2 - 2.5) * s, 28 * s));
        return canvas;
    }

    /// <summary>Both buds side by side, for the rail tile and the popover.</summary>
    public static Canvas Pair(double scale)
    {
        double gap = 4 * scale, w = BudWidth * scale;
        var canvas = new Canvas { Width = w * 2 + gap, Height = BudHeight * scale };
        canvas.Children.Add(Bud(true, scale));
        Canvas right = Bud(false, scale);
        Canvas.SetLeft(right, w + gap);
        canvas.Children.Add(right);
        return canvas;
    }

    private static T At<T>(T shape, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        return shape;
    }
}
