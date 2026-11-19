using System.Globalization;
using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.Settings.Logic;

/// <summary>How the Buds page draws one setting.</summary>
public enum BudsControlKind
{
    Toggle,
    Choice,
    Slider,
    Text,
}

/// <summary>One setting row: the catalog entry, its index (the message's wParam), the control, the value the buds hold and whether the control may be used.</summary>
public sealed record BudsRow(BudsSettingDef Def, int CatalogIndex, BudsControlKind Kind, int Raw, string Text, bool Enabled);

/// <summary>Settings of one catalog group, in catalog order.</summary>
public sealed record BudsGroup(string Name, IReadOnlyList<BudsRow> Rows);

/// <summary>Everything the Galaxy Buds page decides, kept pure so it can be tested: which settings show, as what control, and the words for battery and placement.</summary>
public static class BudsView
{
    public const string NotConnectedLine = "Open the case or put the buds in to connect.";

    /// <summary>The tray reported the buds and they are connected.</summary>
    public static bool IsConnected(BudsState? buds) => buds is { Connected: true };

    /// <summary>
    /// The settings the buds have reported, grouped in catalog order. A setting the buds have not reported is left out; a ShowWhen
    /// setting is hidden when the one it depends on is reported with another value. Controls are disabled when the buds are not
    /// connected, and for settings the catalog marks read-only.
    /// </summary>
    public static IReadOnlyList<BudsGroup> Groups(BudsState? buds)
    {
        if (buds?.Settings is not { Count: > 0 } list) return [];
        bool connected = buds.Connected;
        var groups = new List<(string Name, List<BudsRow> Rows)>();
        for (int i = 0; i < GalaxyBudsSettingsCatalog.All.Count; i++)
        {
            BudsSettingDef def = GalaxyBudsSettingsCatalog.All[i];
            BudsSetting? reported = list.FirstOrDefault(s => s.Key == def.Key);
            if (reported is null || Hidden(def, list)) continue;
            string text = def.IsRange ? reported.Raw.ToString(CultureInfo.InvariantCulture) : def.Text(reported.Raw);
            var row = new BudsRow(def, i, KindOf(def), reported.Raw, text, connected && def.CanSet);
            int at = groups.FindIndex(g => g.Name == def.Group);
            if (at < 0) groups.Add((def.Group, [row]));
            else groups[at].Rows.Add(row);
        }

        return [.. groups.Select(g => new BudsGroup(g.Name, g.Rows))];
    }

    /// <summary>The row for one setting key as the page shows it (null when hidden or unreported); used for the quick noise row.</summary>
    public static BudsRow? Row(BudsState? buds, string key) =>
        Groups(buds).SelectMany(g => g.Rows).FirstOrDefault(r => r.Def.Key == key);

    public static BudsControlKind KindOf(BudsSettingDef def)
    {
        if (!def.CanSet) return BudsControlKind.Text;
        if (def.IsRange) return BudsControlKind.Slider;
        return IsOnOff(def) ? BudsControlKind.Toggle : BudsControlKind.Choice;
    }

    /// <summary>True when the setting depends on another one (ShowWhen) that is reported with a value it does not need.</summary>
    public static bool Hidden(BudsSettingDef def, IReadOnlyList<BudsSetting> list)
    {
        if (def.ShowWhenKey is not { } key || def.ShowWhenRaws is not { } raws) return false;
        BudsSetting? other = list.FirstOrDefault(s => s.Key == key);
        return other is not null && !raws.Contains(other.Raw);
    }

    /// <summary>A choice of exactly Off (0) and On (1): shown as a switch.</summary>
    public static bool IsOnOff(BudsSettingDef def) =>
        def.IsChoice && def.ChoiceCount == 2 && def.ChoiceRaw(0) == 0 && def.ChoiceRaw(1) == 1 && def.Text(0) == "Off" && def.Text(1) == "On";

    /// <summary>Choice raws and labels in catalog order, for a list.</summary>
    public static IReadOnlyList<(int Raw, string Label)> Choices(BudsSettingDef def)
    {
        var list = new List<(int, string)>(def.ChoiceCount);
        for (int i = 0; i < def.ChoiceCount; i++)
        {
            if (def.ChoiceRaw(i) is { } raw) list.Add((raw, def.Text(raw)));
        }

        return list;
    }

    /// <summary>The noise control quick row: raw value and short label, in the order shown.</summary>
    public static readonly IReadOnlyList<(int Raw, string Label)> NoiseModes =
    [
        (0, "Off"),
        (1, "Noise cancelling"),
        (2, "Ambient"),
        (3, "Adaptive"),
    ];

    /// <summary>Placement word under a bud. Empty when the buds have not said.</summary>
    public static string PlacementText(string? placement) => placement?.Trim().ToLowerInvariant() switch
    {
        "wearing" => "In ear",
        "idle" => "Out of case",
        "in case" => "In case",
        "case closed" => "Case closed",
        "disconnected" => "Not connected",
        null or "" => string.Empty,
        var other => char.ToUpperInvariant(other[0]) + other[1..],
    };

    /// <summary>Battery text for one part: "80%", "80% charging", or a dash when not reported.</summary>
    public static string BatteryText(int? percent, bool? charging) =>
        percent is { } p ? $"{Math.Clamp(p, 0, 100)}%" + (charging == true ? " charging" : string.Empty) : BatteryGlyphModel.Dash;

    /// <summary>The "SM-R640 · Firmware x" small line; null when neither is known.</summary>
    public static string? FirmwareLine(BudsState? buds)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(buds?.Model)) parts.Add(buds!.Model!);
        if (!string.IsNullOrWhiteSpace(buds?.Firmware)) parts.Add("Firmware " + buds!.Firmware);
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The lower of the two buds' levels (the case is not counted) and whether that bud charges; null level when neither is known.</summary>
    public static (int? Percent, bool Charging) Lowest(BudsState? buds)
    {
        if (buds is null) return (null, false);
        int? l = buds.BatteryLeft, r = buds.BatteryRight;
        if (l is null && r is null) return (null, false);
        if (r is null || (l is not null && l <= r)) return (l, buds.ChargingLeft == true);
        return (r, buds.ChargingRight == true);
    }

    /// <summary>The rail glyph: the lowest bud; an empty outline when the device is greyed, not connected or nothing is known.</summary>
    public static BatteryGlyphModel Glyph(BudsState? buds, bool dimmed, string? word, int threshold)
    {
        (int? percent, bool charging) = Lowest(buds);
        return BatteryGlyphModel.Create(percent, charging, null, threshold, dimmed || !IsConnected(buds), word);
    }

    /// <summary>True while a change just sent to a control should still be shown instead of the buds' report (2 seconds).</summary>
    public static bool HoldSent(DateTimeOffset sentAt, DateTimeOffset now) => now - sentAt < TimeSpan.FromSeconds(2);

    /// <summary>
    /// The state with the values the user just changed shown in place of the buds' report (the page holds each change for 2 seconds so
    /// a status update does not fight the control). <paramref name="held"/> maps setting key to the raw value sent. Unreported keys stay unreported.
    /// </summary>
    public static BudsState? WithHeld(BudsState? buds, IReadOnlyDictionary<string, int> held)
    {
        if (buds?.Settings is not { } list || held.Count == 0) return buds;
        var changed = list.Select(s => held.TryGetValue(s.Key, out int raw) && GalaxyBudsSettingsCatalog.Find(s.Key) is { } def
            ? s with { Raw = raw, Value = def.Text(raw) }
            : s).ToList();
        return buds with { Settings = changed };
    }

    /// <summary>True when another setting is shown only while this one holds some value, so a change here can add or remove rows.</summary>
    public static bool ControlsOthers(string key) => GalaxyBudsSettingsCatalog.All.Any(d => d.ShowWhenKey == key);
}
