using Slender.Engine.Devices;

namespace Slender.Tray;

/// <summary>One device's line for the tray tooltip.</summary>
public readonly record struct TipDevice(string Name, bool Connected, int? Percent, bool Charging, string? EstimateShort, DevicePresence? Presence = null);

/// <summary>The tray icon tooltip: Windows keeps 127 characters of it, so it is built to fit and cut cleanly if it ever would not.</summary>
public static class BatteryTip
{
    public const int MaxLength = 127;

    public static string Build(IEnumerable<TipDevice> devices)
    {
        var lines = new List<string> { "Slender" };
        foreach (TipDevice d in devices)
        {
            if (d.Presence == DevicePresence.Hidden) continue;
            if (!d.Connected)
            {
                if (d.Presence == DevicePresence.Sleeping) lines.Add($"{d.Name} asleep");
                else if (d.Presence == DevicePresence.Disconnected) lines.Add($"{d.Name} disconnected");
                continue;
            }

            string line = d.Percent is { } p ? $"{d.Name} {p}%" : d.Name;
            if (d.Charging) line += " charging";
            else if (!string.IsNullOrEmpty(d.EstimateShort)) line += $" {d.EstimateShort} left";
            lines.Add(line);
        }

        string text = string.Join("\n", lines);
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
    }
}
