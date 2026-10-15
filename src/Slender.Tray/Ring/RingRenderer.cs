using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tray.Ring;

/// <summary>One slot as drawn: text, glyph and an optional uploaded icon bitmap (owned by the icon cache).</summary>
internal sealed record RingCell(string Name, string Shortcut, string Glyph, nint Icon, bool IsEmpty, bool IsFolder);

/// <summary>Everything the renderer needs for one frame. Selected null = pointer in the centre.</summary>
internal sealed record RingFrame(RingStyle Style, RingMetrics Metrics, RingColor Accent, RingLabels Labels, IReadOnlyList<RingCell> Cells, int? Selected, string? FolderName);

/// <summary>
/// Draws the three approved ring styles (docs/ring-prototypes-round1.html: Bezel, Compass, Keypad) onto a canvas. Slot
/// geometry comes from <see cref="RingLayout"/>; the canvas is 2 x Extent DIPs square with the ring centre in the middle.
/// SVG baselines in the prototypes become vertical text centres (baseline minus about 0.35 x font size).
/// </summary>
internal static class RingRenderer
{
    private static readonly RingColor Paper = RingColor.Rgb(0x1B1F23), Paper2 = RingColor.Rgb(0x22282D), Paper3 = RingColor.Rgb(0x2A3137),
        Rule = RingColor.Rgb(0x343C43), Ink = RingColor.Rgb(0xECEFF1), Muted = RingColor.Rgb(0x8F989F), KeyShadow = RingColor.Rgb(0x15191C);

    /// <summary>The cells for a slot list; file icons are loaded (and cached) at <paramref name="iconPx"/> pixels square.</summary>
    public static RingCell[] Cells(IReadOnlyList<RingSlot> slots, RingIconCache? icons, int iconPx)
    {
        var cells = new RingCell[slots.Count];
        for (var i = 0; i < cells.Length; i++)
        {
            var slot = slots[i];
            nint icon = 0;
            if (icons is not null && !slot.IsEmpty && RingSlotText.IconFile(slot) is { } file) icon = icons.Get(file, iconPx);
            cells[i] = new RingCell(RingSlotText.Name(slot), RingSlotText.Shortcut(slot), slot.IsEmpty ? string.Empty : RingSlotText.Glyph(slot), icon, slot.IsEmpty, slot.IsFolder);
        }
        return cells;
    }

    public static void Draw(RingCanvas c, RingFrame f)
    {
        c.Begin();
        c.Clear();
        switch (f.Style)
        {
            case RingStyle.Compass: Compass(c, f); break;
            case RingStyle.Keypad: Keypad(c, f); break;
            default: Bezel(c, f); break;
        }
        c.End();
    }

    // ---- 1 Bezel: segmented donut, knurled rim, hub names the slot ----

    private static void Bezel(RingCanvas c, RingFrame f)
    {
        var m = f.Metrics;
        double k = m.Scale, cx = m.Extent, cy = m.Extent;
        var soft = f.Accent.WithAlpha(.16f);
        c.FillCircle(cx, cy, 142 * k, Paper.WithAlpha(.55f));
        for (var t = 0; t < 96; t++)
        {
            var a = t * 3.75;
            var (x1, y1) = RingCanvas.Polar(cx, cy, 133 * k, a);
            var (x2, y2) = RingCanvas.Polar(cx, cy, 139 * k, a);
            c.Line(x1, y1, x2, y2, Rule, 1.2 * k);
        }
        for (var i = 0; i < f.Cells.Count; i++)
        {
            var cell = f.Cells[i];
            var a = RingLayout.AngleOf(i);
            var on = f.Selected == i;
            using (var wedge = c.Wedge(cx, cy, m.InnerRadius, m.OuterRadius, a - 21.5, a + 21.5))
            {
                c.Fill(wedge, on ? Paper3 : Paper2);
                c.Stroke(wedge, cell.IsEmpty ? Muted : Rule, 1, dashed: cell.IsEmpty);
                if (on) c.Fill(wedge, soft);
            }
            if (on)
            {
                using var rim = c.ArcPath(cx, cy, 131 * k, a - 21, a + 21);
                c.Stroke(rim, f.Accent, 4 * k);
            }
            var (x, y) = RingCanvas.Polar(cx, cy, m.SlotRadius, a);
            if (cell.IsEmpty)
            {
                c.Text("+", RingFont.Ui, 20 * k, x, y, Muted);
                continue;
            }
            var glyphColor = on ? f.Accent : Ink;
            var labelled = f.Labels == RingLabels.Always;
            Icon(c, cell, x, labelled ? y - 7 * k : y, 21 * k, glyphColor);
            if (labelled) c.Text(RingSlotText.Fit(cell.Name, 10), RingFont.Label, 9.5 * k, x, y + 14 * k, on ? Ink : Muted, maxWidth: 66 * k);
        }
        c.FillCircle(cx, cy, m.LabelRadius, Paper);
        c.StrokeCircle(cx, cy, m.LabelRadius, Rule, 1);
        if (f.Selected is { } s)
        {
            var cell = f.Cells[s];
            if (cell.IsEmpty)
            {
                c.Text("Empty", RingFont.Label, 15 * k, cx, cy - 3.5 * k, Muted, maxWidth: 100 * k);
                c.Text("set in Slender", RingFont.Mono, 9.5 * k, cx, cy + 15 * k, Muted, maxWidth: 100 * k);
            }
            else
            {
                c.Text(RingSlotText.Fit(cell.Name, 11), RingFont.Label, 15 * k, cx, cy - 3.5 * k, Ink, maxWidth: 100 * k);
                c.Text(RingSlotText.Fit(cell.Shortcut, 14), RingFont.Mono, 10.5 * k, cx, cy + 15 * k, Muted, maxWidth: 100 * k);
            }
        }
        else if (f.FolderName is { } folder)
        {
            c.Text(RingSlotText.Fit(folder, 11), RingFont.Label, 15 * k, cx, cy - 3.5 * k, Ink, maxWidth: 100 * k);
            c.Text("centre = back", RingFont.Mono, 9.5 * k, cx, cy + 15 * k, Muted, maxWidth: 100 * k);
        }
        else c.Text("Esc", RingFont.Ui, 12 * k, cx, cy, Muted);
    }

    // ---- 2 Compass: floating icon buttons and a needle from the cursor ----

    private static void Compass(RingCanvas c, RingFrame f)
    {
        var m = f.Metrics;
        double k = m.Scale, cx = m.Extent, cy = m.Extent;
        c.StrokeCircle(cx, cy, 100 * k, Paper.WithAlpha(.35f), 1);
        if (f.Selected is { } sel)
        {
            var (nx, ny) = RingCanvas.Polar(cx, cy, 74 * k, RingLayout.AngleOf(sel));
            c.Line(cx, cy, nx, ny, f.Accent, 2.5 * k);
        }
        c.FillCircle(cx, cy, 9 * k, Paper);
        c.StrokeCircle(cx, cy, 9 * k, f.Accent, 2 * k);
        c.FillCircle(cx, cy, 3 * k, f.Accent);
        for (var i = 0; i < f.Cells.Count; i++)
        {
            var cell = f.Cells[i];
            var on = f.Selected == i;
            var (x, y) = RingCanvas.Polar(cx, cy, m.SlotRadius, RingLayout.AngleOf(i));
            c.FillCircle(x, y, m.BubbleRadius, on ? f.Accent : Paper);
            c.StrokeCircle(x, y, m.BubbleRadius, cell.IsEmpty ? Muted : on ? f.Accent : Rule, 1.2 * k, dashed: cell.IsEmpty);
            if (!cell.IsEmpty) Icon(c, cell, x, y, 19 * k, on ? Paper : Ink);
        }
        if (f.Selected is { } s)
        {
            var cell = f.Cells[s];
            var a = RingLayout.AngleOf(s) * Math.PI / 180;
            var (x, y) = RingCanvas.Polar(cx, cy, m.LabelRadius, RingLayout.AngleOf(s));
            double cos = Math.Cos(a), sin = Math.Sin(a);
            var align = cos > 0.3 ? -1 : cos < -0.3 ? 1 : 0; // right of the ring: text starts at x
            var dx = cos > 0.3 ? -14 * k : cos < -0.3 ? 14 * k : 0;
            var ty = sin < -0.9 ? y - 8 * k : sin > 0.9 ? y + 4 * k : y - 5 * k;
            var name = cell.IsEmpty ? "Empty" : cell.Name;
            var room = align == 0 ? 150 * k : m.Extent - Math.Abs(x + dx - cx) - 4 * k;
            c.HaloText(RingSlotText.Fit(name, 18), RingFont.Label, 15 * k, x + dx, ty - 4 * k, cell.IsEmpty ? Muted : Ink, Paper, align, Math.Min(150 * k, room));
            if (!cell.IsEmpty) c.HaloText(RingSlotText.Fit(cell.Shortcut, 22), RingFont.Mono, 10.5 * k, x + dx, ty + 13 * k, Muted, Paper, align, Math.Min(150 * k, room));
        }
        else if (f.FolderName is { } folder)
            c.HaloText(RingSlotText.Fit(folder, 16), RingFont.Label, 13 * k, cx, cy + 26 * k, Ink, Paper, maxWidth: 120 * k);
    }

    // ---- 3 Keypad: a 3x3 numpad of keycaps, directions are grid cells ----

    private static void Keypad(RingCanvas c, RingFrame f)
    {
        var m = f.Metrics;
        double k = m.Scale, cx = m.Extent, cy = m.Extent;
        double kw = m.KeyWidth, kh = m.KeyHeight, gap = m.KeyGap;
        double ox = cx - (kw * 1.5 + gap), oy = cy - (kh * 1.5 + gap);
        var soft = f.Accent.WithAlpha(.16f);
        c.FillRoundRect(ox - 10 * k, oy - 10 * k, kw * 3 + gap * 2 + 20 * k, kh * 3 + gap * 2 + 20 * k, 14 * k, Paper.WithAlpha(.82f));
        c.StrokeRoundRect(ox - 10 * k, oy - 10 * k, kw * 3 + gap * 2 + 20 * k, kh * 3 + gap * 2 + 20 * k, 14 * k, Rule, 1);
        for (var i = 0; i < f.Cells.Count; i++)
        {
            var cell = f.Cells[i];
            var (col, row) = RingLayout.KeypadCell(i);
            var on = f.Selected == i;
            double x = ox + col * (kw + gap), y = oy + row * (kh + gap) + (on ? 2 * k : 0);
            c.FillRoundRect(x, y + 3 * k, kw, kh, 8 * k, KeyShadow);
            c.FillRoundRect(x, y, kw, kh, 8 * k, on ? Paper3 : Paper2);
            c.StrokeRoundRect(x, y, kw, kh, 8 * k, on ? f.Accent : cell.IsEmpty ? Muted : Rule, on ? 1.6 * k : 1, dashed: cell.IsEmpty && !on);
            if (on) c.FillRoundRect(x, y, kw, kh, 8 * k, soft);
            if (!cell.IsEmpty) Icon(c, cell, x + 12 * k + 8 * k, y + 20.4 * k, 16 * k, on ? f.Accent : Ink);
            var name = cell.IsEmpty ? "Empty" : cell.Name;
            c.Text(RingSlotText.Fit(name, 12), RingFont.Label, 13.5 * k, x + 12 * k, y + 43.3 * k, cell.IsEmpty ? Muted : Ink, -1, kw - 20 * k);
            c.Text(RingSlotText.Fit(cell.IsEmpty ? "set in Slender" : cell.Shortcut, 15), RingFont.Mono, 10 * k, x + 12 * k, y + 59.5 * k, Muted, -1, kw - 20 * k);
        }
        double mx = ox + kw + gap, my = oy + kh + gap;
        c.StrokeRoundRect(mx, my, kw, kh, 8 * k, Rule, 1, dashed: true);
        c.Line(cx - 8 * k, cy, cx + 8 * k, cy, f.Accent, 2 * k);
        c.Line(cx, cy - 8 * k, cx, cy + 8 * k, f.Accent, 2 * k);
        if (f.FolderName is { } folder) c.Text(RingSlotText.Fit(folder, 13), RingFont.Label, 11 * k, cx, my + kh - 13.5 * k, Ink, maxWidth: kw - 8 * k);
        else c.Text("Esc", RingFont.Mono, 10 * k, cx, my + kh - 13.5 * k, Muted);
    }

    /// <summary>The slot's icon: the file icon bitmap when there is one, else its glyph, centred on (x, y) at the given size.</summary>
    private static void Icon(RingCanvas c, RingCell cell, double x, double y, double size, RingColor color)
    {
        if (cell.Icon != 0)
        {
            var side = size * 1.25;
            c.DrawBitmap(cell.Icon, x - side / 2, y - side / 2, side, side);
        }
        else if (cell.Glyph.Length > 0) c.Text(cell.Glyph, RingFont.Icons, size, x, y, color, maxWidth: size * 3);
    }
}
