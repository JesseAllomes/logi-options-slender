using Slender.Engine.Devices;

namespace Slender.Tray;

/// <summary>Menu status-line wording, kept pure so it can be tested.</summary>
public static class TrayText
{
    public const string Paused = "Paused — Logi Options+ is running";

    /// <summary>The headset submenu's detail line from the base's report: battery and charging, docked, out of range. Null when the base has said none of it.</summary>
    public static string? Headset(Slender.Engine.HeadsetState? h)
    {
        if (h is null || h.Registered == false) return null;
        var parts = new List<string>(3);
        if (h.BatteryPercent is { } p) parts.Add(h.Charging == true ? $"Battery {p}% charging" : $"Battery {p}%");
        if (h.Docked is { } docked) parts.Add(docked ? "Docked" : "Off the base");
        if (h.InRange == false) parts.Add("Out of range");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The buds battery line: "L 100% · R 98% · Case 95%". Null when none of it is known.</summary>
    public static string? BudsBattery(Slender.Engine.BudsState? b)
    {
        if (b is null) return null;
        var parts = new List<string>(3);
        if (b.BatteryLeft is { } l) parts.Add($"L {l}%");
        if (b.BatteryRight is { } r) parts.Add($"R {r}%");
        if (b.BatteryCase is { } c) parts.Add($"Case {c}%");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>Lowest battery among the earbuds that are connected (a bud reported disconnected is left out); null when none is known.</summary>
    public static int? BudsLowest(Slender.Engine.BudsState? b)
    {
        if (b is null) return null;
        int? low = null;
        if (b.BatteryLeft is { } l && b.PlacementLeft != "disconnected") low = l;
        if (b.BatteryRight is { } r && b.PlacementRight != "disconnected") low = low is { } x ? Math.Min(x, r) : r;
        return low;
    }

    public static string Device(string name, bool connected, int? percent, bool charging, string? estimateShort = null) =>
        !connected
            ? $"{name} — not connected"
            : percent is { } p
                ? $"{name} — connected, {p}%{(charging ? " charging" : string.IsNullOrEmpty(estimateShort) ? string.Empty : $", {estimateShort} left")}"
                : $"{name} — connected";

    /// <summary>
    /// The line for a device given the registry's word (null = no registry: today's text). Hidden has no line (null). Asleep and
    /// disconnected only while the device is not connected; Active keeps the connected/not-connected wording.
    /// </summary>
    public static string? Line(string name, DevicePresence? presence, bool connected, int? percent, bool charging, string? estimateShort = null) =>
        presence switch
        {
            DevicePresence.Hidden => null,
            DevicePresence.Sleeping when !connected => $"{name} — asleep",
            DevicePresence.Disconnected when !connected => $"{name} — disconnected",
            _ => Device(name, connected, percent, charging, estimateShort),
        };
}
