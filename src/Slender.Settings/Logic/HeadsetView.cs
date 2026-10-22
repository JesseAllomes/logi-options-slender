using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Plain words for what the headset base reported (m55), kept pure so they can be tested.</summary>
public static class HeadsetView
{
    /// <summary>The state rows (Battery, Charging, Docked, Signal, Range) the base has told us; empty when it has said nothing.</summary>
    public static IReadOnlyList<(string Label, string Value)> StatusRows(HeadsetState? h)
    {
        var rows = new List<(string, string)>(5);
        if (h is null) return rows;
        if (h.Registered == false)
        {
            rows.Add(("Headset", "Not paired with the base"));
            return rows;
        }
        if (h.BatteryPercent is { } p) rows.Add(("Battery", $"{p}%"));
        if (h.Charging is { } c) rows.Add(("Charging", c ? "Yes" : "No"));
        if (h.Docked is { } d) rows.Add(("Docked", d ? "Yes" : "No"));
        if (h.Signal is { } s) rows.Add(("Signal", SignalWord(s)));
        if (h.InRange is { } r) rows.Add(("Range", r ? "In range" : "Out of range"));
        return rows;
    }

    /// <summary>The base reports 0 to 7.</summary>
    public static string SignalWord(int signal) => signal switch
    {
        <= 0 => "None",
        <= 2 => "Weak",
        <= 4 => "Fair",
        _ => "Good",
    };

    /// <summary>The read-only settings grouped as USB Connect does, groups and settings in the order the tray sent them.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<HeadsetSetting> Items)> Groups(HeadsetState? h)
    {
        if (h?.Settings is not { Count: > 0 } list) return [];
        var order = new List<string>();
        foreach (var s in list)
            if (!order.Contains(s.Group)) order.Add(s.Group);
        return [.. order.Select(g => (g, (IReadOnlyList<HeadsetSetting>)[.. list.Where(s => s.Group == g)]))];
    }
}
