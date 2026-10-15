using System.Text.Json;

namespace Slender.Settings.Logic;

/// <summary>Text and tolerant parsing for the popover and the firmware rows. No UI types.</summary>
public static class PopoverModel
{
    /// <summary>"You're in Excel · 3 buttons changed"; null when no foreground app is known.</summary>
    public static string? Summary(string? displayName, int changed)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        string count = changed switch { 0 => "no buttons changed", 1 => "1 button changed", _ => $"{changed} buttons changed" };
        return $"You're in {displayName} · {count}";
    }

    /// <summary>Process name with a capital ("excel" becomes "Excel").</summary>
    public static string ProcessDisplayName(string process) =>
        process.Length == 0 ? process : char.ToUpperInvariant(process[0]) + process[1..];

    private static readonly string[] FirmwareKeys = ["firmware", "firmwareVersion", "fwVersion"];

    /// <summary>
    /// Reads device id to firmware version from status.json text. The tray does not publish a firmware field yet, so everything is
    /// optional: a missing field, a non-string value, an empty string, a missing devices array or malformed JSON just yields no
    /// entry. Accepts "firmware", "firmwareVersion" or "fwVersion", in any letter case, as a string or a number.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseFirmware(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !TryGetIgnoreCase(doc.RootElement, "devices", out JsonElement devices) || devices.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement device in devices.EnumerateArray())
            {
                if (device.ValueKind != JsonValueKind.Object || !TryGetIgnoreCase(device, "id", out JsonElement id) || id.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                foreach (string key in FirmwareKeys)
                {
                    if (!TryGetIgnoreCase(device, key, out JsonElement fw))
                    {
                        continue;
                    }

                    string? text = fw.ValueKind switch
                    {
                        JsonValueKind.String => fw.GetString(),
                        JsonValueKind.Number => fw.GetRawText(),
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result[id.GetString()!] = text.Trim();
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }

        return result;
    }

    private static bool TryGetIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
