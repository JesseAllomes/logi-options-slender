using Slender.Engine;
using Slender.Engine.Devices;

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

    /// <summary>
    /// The base's settings for one page, grouped as USB Connect does in catalog order. Each is matched to the catalog by Key; a tray
    /// older than m59 sends none, so those match by label and group and carry no Raw (shown read-only). Settings the catalog does
    /// not know are left out.
    /// </summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<HeadsetSettingItem> Items)> Groups(HeadsetState? h, YealinkTarget target)
    {
        if (h?.Settings is not { Count: > 0 } list) return [];
        var groups = new List<(string Group, List<HeadsetSettingItem> Items)>();
        for (int i = 0; i < YealinkSettingsCatalog.All.Count; i++)
        {
            YealinkSettingDef def = YealinkSettingsCatalog.All[i];
            if (def.Target != target) continue;
            HeadsetSetting? reported = list.FirstOrDefault(s => s.Key == def.Key)
                ?? list.FirstOrDefault(s => s.Key is null && s.Group == def.Group && s.Label == def.Label);
            if (reported is null) continue;
            var item = new HeadsetSettingItem(def, i, reported.Key is null ? null : reported.Raw, reported.Value);
            int at = groups.FindIndex(g => g.Group == def.Group);
            if (at < 0) groups.Add((def.Group, [item]));
            else groups[at].Items.Add(item);
        }

        return [.. groups.Select(g => (g.Group, (IReadOnlyList<HeadsetSettingItem>)g.Items))];
    }

    /// <summary>A choice of exactly Off and On (raw "0" and "1"): shown as a switch instead of a list.</summary>
    public static bool IsOnOff(YealinkSettingDef def) =>
        def.IsChoice && def.ChoiceCount == 2 && def.Values![0] == "0" && def.Values[2] == "1" && def.ChoiceLabel(0) == "Off" && def.ChoiceLabel(1) == "On";
}

/// <summary>One base setting as the page shows it: the catalog entry, its index (the message's wParam), the value as the base holds it (null when not known) and as text.</summary>
public sealed record HeadsetSettingItem(YealinkSettingDef Def, int CatalogIndex, string? Raw, string Text)
{
    /// <summary>True when the page may offer a control: the catalog allows it and the base told us the raw value.</summary>
    public bool Adjustable => Def.CanSet && Raw is not null;
}
