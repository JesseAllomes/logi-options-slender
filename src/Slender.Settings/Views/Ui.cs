using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Slender.Settings.Logic;
using Windows.UI;
using Windows.UI.Text;

namespace Slender.Settings.Views;

/// <summary>Small factories so the views read as layout rather than property soup.</summary>
internal static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.Resources[key];

    /// <summary>The mouse wheel moves the slider one step per notch while the pointer is over it (the page does not scroll then).</summary>
    public static Slider WheelSteps(Slider slider)
    {
        int carry = 0;
        slider.PointerWheelChanged += (_, e) =>
        {
            var point = e.GetCurrentPoint(slider);
            if (!slider.IsEnabled || point.Properties.IsHorizontalMouseWheel) return;
            double step = slider.StepFrequency > 0 ? slider.StepFrequency : slider.SmallChange;
            slider.Value = SliderWheel.Apply(slider.Value, slider.Minimum, slider.Maximum, step, point.Properties.MouseWheelDelta, ref carry);
            e.Handled = true;
        };
        return slider;
    }

    public static Brush Brush(string key) => Res<Brush>(key);

    public static Brush Ink => Brush("InkBrush");
    public static Brush Muted => Brush("MutedBrush");
    public static Brush Mark => Brush("MarkBrush");
    public static Brush MarkSoft => Brush("MarkSoftBrush");
    public static Brush Paper3 => Brush("Paper3Brush");
    public static Brush Rule => Brush("RuleBrush");
    public static Brush Warn => Brush("WarnBrush");
    public static Brush Live => Brush("LiveBrush");

    public static FontFamily Display => Res<FontFamily>("DisplayFont");
    public static FontFamily Heading => Res<FontFamily>("HeadingFont");
    public static FontFamily Mono => Res<FontFamily>("MonoFont");

    public static TextBlock Text(string text, double size = 13, Brush? foreground = null, FontFamily? family = null, FontWeight? weight = null)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = foreground ?? Ink,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        if (family is not null)
        {
            t.FontFamily = family;
        }

        if (weight is { } w)
        {
            t.FontWeight = w;
        }

        return t;
    }

    /// <summary>One key cap in JetBrains Mono.</summary>
    public static Border Chip(string text, double size = 11)
    {
        return new Border
        {
            Background = Paper3,
            BorderBrush = Rule,
            BorderThickness = new Thickness(1, 1, 1, 2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, size, Ink, Mono),
        };
    }

    public static StackPanel Chips(IEnumerable<string> parts, double size = 11)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (string part in parts)
        {
            p.Children.Add(Chip(part, size));
        }

        return p;
    }

    private const int BitmapCacheLimit = 32;
    private const int SizesPerPicture = 3;
    private static readonly Dictionary<(string Path, bool ByWidth), List<(int Size, BitmapImage Image)>> s_bitmaps = [];

    /// <summary>
    /// A decoded-at-display-size image from a file (decode sizes in physical pixels). The last few sizes of each file and axis are
    /// kept (a picture shows as a rail tile, a popover tile and on its page), so a page rebuild reuses the decode instead of
    /// decoding again, which showed as a blank flash; older sizes drop out, so a window resize never piles up copies. UI thread only.
    /// </summary>
    public static BitmapImage Bitmap(string path, int decodePixelWidth = 0, int decodePixelHeight = 0)
    {
        var key = (path, decodePixelWidth > 0);
        int size = decodePixelWidth > 0 ? decodePixelWidth : decodePixelHeight;
        if (!s_bitmaps.TryGetValue(key, out var sizes))
        {
            if (s_bitmaps.Count >= BitmapCacheLimit)
            {
                s_bitmaps.Clear();
            }

            s_bitmaps[key] = sizes = [];
        }

        int at = sizes.FindIndex(s => s.Size == size);
        if (at >= 0)
        {
            var hit = sizes[at];
            sizes.RemoveAt(at);
            sizes.Insert(0, hit);
            return hit.Image;
        }

        var bmp = new BitmapImage();
        if (decodePixelWidth > 0)
        {
            bmp.DecodePixelWidth = decodePixelWidth;
        }
        else if (decodePixelHeight > 0)
        {
            bmp.DecodePixelHeight = decodePixelHeight;
        }

        bmp.UriSource = new Uri(path);
        sizes.Insert(0, (size, bmp));
        if (sizes.Count > SizesPerPicture)
        {
            sizes.RemoveAt(sizes.Count - 1);
        }

        return bmp;
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static Border Rule1(double height = 1) => new() { Height = height, Background = Rule };

    /// <summary>Rounded tile that stands in for "All apps".</summary>
    public static Border AllAppsGlyph(double size)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = Paper3,
            Child = new TextBlock
            {
                Text = "All",
                FontFamily = Heading,
                FontSize = size * 0.34,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    public static FontIcon Glyph(string glyph, double size = 14, Brush? foreground = null) =>
        new() { Glyph = glyph, FontSize = size, Foreground = foreground ?? Muted };

    public static string DeviceNoun(DeviceKind kind) => kind == DeviceKind.Mouse ? "button" : "key";
}
