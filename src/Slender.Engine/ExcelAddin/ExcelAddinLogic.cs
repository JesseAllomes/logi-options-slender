namespace Slender.Engine.ExcelAddin;

/// <summary>
/// The decisions the add-in makes, kept free of Excel so they can be tested. The add-in project compiles this file in.
/// </summary>
public static class ExcelAddinLogic
{
    /// <summary>Zoom presets, in percent. Excel allows 10 to 400.</summary>
    public static readonly IReadOnlyList<int> ZoomPresets = [10, 25, 50, 75, 100, 125, 150, 200, 300, 400];

    /// <summary>Table styles the cycle steps through (Excel's built-in medium styles, then light ones).</summary>
    public static readonly IReadOnlyList<string> TableStyles =
    [
        "TableStyleMedium2", "TableStyleMedium9", "TableStyleMedium3", "TableStyleMedium4", "TableStyleMedium5",
        "TableStyleMedium6", "TableStyleLight9", "TableStyleLight15", "TableStyleLight1",
    ];

    /// <summary>XlChartType values the cycle steps through: column, bar, line, line with markers, area, pie, scatter.</summary>
    public static readonly IReadOnlyList<int> ChartTypes = [51, 57, 4, 65, 1, 5, -4169];

    /// <summary>
    /// The index of the next visible sheet from <paramref name="current"/> in direction <paramref name="step"/> (+1 or -1), wrapping
    /// round. Hidden and very hidden sheets are skipped. -1 when no other sheet is visible.
    /// </summary>
    public static int NextVisibleSheet(IReadOnlyList<bool> visible, int current, int step)
    {
        var n = visible.Count;
        if (n == 0 || step == 0) return -1;
        var dir = step > 0 ? 1 : -1;
        var start = Math.Clamp(current, 0, n - 1);
        for (var i = 1; i < n; i++)
        {
            var idx = ((start + dir * i) % n + n) % n;
            if (visible[idx]) return idx;
        }
        return -1;
    }

    /// <summary>The next zoom preset above (direction +1) or below (-1) <paramref name="current"/>; stays at the end of the range.</summary>
    public static int NextZoom(int current, int direction)
    {
        if (direction > 0)
        {
            foreach (var z in ZoomPresets) if (z > current) return z;
            return ZoomPresets[^1];
        }
        for (var i = ZoomPresets.Count - 1; i >= 0; i--)
        {
            if (ZoomPresets[i] < current) return ZoomPresets[i];
        }
        return ZoomPresets[0];
    }

    /// <summary>The item after <paramref name="current"/> in the cycle; the first item when current is not in it.</summary>
    public static T NextInCycle<T>(IReadOnlyList<T> cycle, T current) where T : IEquatable<T>
    {
        for (var i = 0; i < cycle.Count; i++)
        {
            if (cycle[i].Equals(current)) return cycle[(i + 1) % cycle.Count];
        }
        return cycle[0];
    }

    /// <summary>
    /// What the thumb wheel does inside Excel. A cell selection scrolls columns. A chart, shape or picture selection (anything that
    /// is not a Range) has no use for sideways scrolling, so the wheel steps through sheets instead.
    /// </summary>
    public static WheelTarget WheelTargetFor(bool selectionIsRange) => selectionIsRange ? WheelTarget.Columns : WheelTarget.Sheets;
}

public enum WheelTarget { Columns, Sheets }
