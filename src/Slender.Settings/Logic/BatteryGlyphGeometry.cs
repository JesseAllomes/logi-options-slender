namespace Slender.Settings.Logic;

public enum BatteryGlyphSize { Small, Large }

/// <summary>A rounded rectangle in glyph units (device-independent pixels).</summary>
public readonly record struct GlyphRect(double X, double Y, double Width, double Height, double Radius);

/// <summary>
/// The battery glyph's drawing, as numbers. The WinUI control and the SVG preview both read this, so what is previewed is what
/// is drawn. Origin is the glyph's top-left; the glyph is <see cref="Width"/> x <see cref="Height"/>.
/// </summary>
public sealed record BatteryGlyphGeometry(
    double Width,
    double Height,
    GlyphRect Body,
    double BodyStroke,
    GlyphRect Cap,
    GlyphRect Fill,
    IReadOnlyList<(double X, double Y)> Bolt,
    double BoltEdge,
    double FontSize,
    double TextGap)
{
    private sealed record Metrics(double BodyW, double BodyH, double Stroke, double Radius, double CapW, double CapH, double Inset,
        double BoltHeight, double BoltEdge, double FontSize, double TextGap);

    private static readonly Metrics Small = new(22, 11, 1, 3, 1.8, 4.4, 1, 9, 0.6, 11.5, 4);
    private static readonly Metrics Large = new(34, 17, 1.25, 4.4, 2.8, 7, 1.5, 12.2, 0.8, 14, 8);

    /// <summary>The lightning bolt in a 10 x 16 box.</summary>
    private static readonly (double X, double Y)[] BoltUnit = [(7, 0), (0, 9), (4, 9), (3, 16), (10, 6), (6, 6)];

    public static BatteryGlyphGeometry For(BatteryGlyphSize size, double fraction)
    {
        Metrics m = size == BatteryGlyphSize.Small ? Small : Large;
        double f = Math.Clamp(fraction, 0, 1);
        double s = m.Stroke;

        var body = new GlyphRect(s / 2, s / 2, m.BodyW - s, m.BodyH - s, m.Radius - s / 2);
        double capX = m.BodyW - s / 2;
        var cap = new GlyphRect(capX, (m.BodyH - m.CapH) / 2, m.CapW, m.CapH, Math.Min(m.CapW, m.CapH) / 2);

        double gap = s + m.Inset;
        double innerW = m.BodyW - 2 * gap, innerH = m.BodyH - 2 * gap;
        double fillW = innerW * f;
        double innerR = Math.Max(0.5, m.Radius - gap);
        var fill = new GlyphRect(gap, gap, fillW, innerH, Math.Min(innerR, Math.Min(fillW / 2, innerH / 2)));

        double scale = m.BoltHeight / 16;
        double bx = (m.BodyW - 10 * scale) / 2, by = (m.BodyH - 16 * scale) / 2;
        var bolt = BoltUnit.Select(p => (bx + p.X * scale, by + p.Y * scale)).ToArray();

        return new BatteryGlyphGeometry(capX + m.CapW, m.BodyH, body, s, cap, fill, bolt, m.BoltEdge, m.FontSize, m.TextGap);
    }

    /// <summary>Full width of the inner fill area, so tests can check the fill is exactly proportional.</summary>
    public static double InnerWidth(BatteryGlyphSize size) => For(size, 1).Fill.Width;
}
