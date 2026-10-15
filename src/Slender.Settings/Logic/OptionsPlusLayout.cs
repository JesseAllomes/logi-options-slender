using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slender.Settings.Logic;

/// <summary>Reads Logi Options+ depot files (manifest.json and the image metadata) and maps their slots to Slender control ids.</summary>
public static partial class OptionsPlusLayout
{
    private static readonly Dictionary<string, (ushort Id, string Name)> MouseSlots = new(StringComparer.Ordinal)
    {
        ["SLOT_NAME_MIDDLE_BUTTON"] = (0x0052, "Middle click"),
        ["SLOT_NAME_MODESHIFT_BUTTON"] = (0x00C4, "Mode shift"),
        ["SLOT_NAME_THUMBWHEEL"] = (DeviceIds.ThumbWheelPseudoId, "Thumb wheel"),
        ["SLOT_NAME_FORWARD_BUTTON"] = (0x0056, "Forward"),
        ["SLOT_NAME_BACK_BUTTON"] = (0x0053, "Back"),
        ["SLOT_NAME_GESTURE_BUTTON"] = (0x00C3, "Gesture button"),
        ["ASSIGNMENT_NAME_SHOW_RADIAL_MENU"] = (0x01A0, "Sense Panel"),
    };

    // Friendlier names than the generic title-casing gives.
    private static readonly Dictionary<string, string> NameOverrides = new(StringComparer.Ordinal)
    {
        ["SLOT_NAME_MISSION_CONTROL"] = "Task view",
        ["SLOT_NAME_SWITCH_APPLICATIONS"] = "Switch apps",
        ["SLOT_NAME_PREVIOUS"] = "Previous track",
        ["SLOT_NAME_NEXT"] = "Next track",
        ["SLOT_NAME_PLAY_PAUSE"] = "Play / pause",
    };

    [GeneratedRegex(@"_c(\d+)$")]
    private static partial Regex DecimalCidSuffix();

    /// <summary>Finds the file for (modelId, resource key) in a manifest.json, or null.</summary>
    public static string? FindResource(string manifestJson, string modelId, string resourceKey)
    {
        using JsonDocument doc = JsonDocument.Parse(manifestJson);
        if (!doc.RootElement.TryGetProperty("devices", out JsonElement devices) || devices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement device in devices.EnumerateArray())
        {
            if (!device.TryGetProperty("modelId", out JsonElement model) || model.GetString() != modelId)
            {
                continue;
            }

            if (!device.TryGetProperty("resources", out JsonElement resources) || resources.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement res in resources.EnumerateArray())
            {
                if (res.TryGetProperty("key", out JsonElement key) && key.GetString() == resourceKey
                    && res.TryGetProperty("src", out JsonElement src))
                {
                    return src.GetString();
                }
            }
        }

        return null;
    }

    /// <summary>The image metadata file for a model: the device's own "image_metadata" resource, else the manifest-wide one.</summary>
    public static string? FindMetadataFile(string manifestJson, string modelId)
    {
        string? own = FindResource(manifestJson, modelId, "image_metadata");
        if (own is not null)
        {
            return own;
        }

        using JsonDocument doc = JsonDocument.Parse(manifestJson);
        if (doc.RootElement.TryGetProperty("resources", out JsonElement resources) && resources.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement res in resources.EnumerateArray())
            {
                if (res.TryGetProperty("key", out JsonElement key) && key.GetString() == "image_metadata"
                    && res.TryGetProperty("src", out JsonElement src))
                {
                    return src.GetString();
                }
            }
        }

        return null;
    }

    /// <summary>True if the manifest lists a device with this model id.</summary>
    public static bool HasModel(string manifestJson, string modelId)
    {
        using JsonDocument doc = JsonDocument.Parse(manifestJson);
        if (!doc.RootElement.TryGetProperty("devices", out JsonElement devices) || devices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return devices.EnumerateArray().Any(d => d.TryGetProperty("modelId", out JsonElement m) && m.GetString() == modelId);
    }

    /// <summary>
    /// Parses the hotspots of one image key (for example "device_buttons_image") from a metadata file. Slots Slender cannot map
    /// are skipped. Returns null when the image key is missing.
    /// </summary>
    public static (int Width, int Height, IReadOnlyList<Hotspot> Hotspots)? ParseMetadata(string metadataJson, string imageKey, DeviceKind kind)
    {
        using JsonDocument doc = JsonDocument.Parse(metadataJson);
        if (!doc.RootElement.TryGetProperty("images", out JsonElement images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement image in images.EnumerateArray())
        {
            if (!image.TryGetProperty("key", out JsonElement key) || key.GetString() != imageKey)
            {
                continue;
            }

            int width = 0;
            int height = 0;
            if (image.TryGetProperty("origin", out JsonElement origin))
            {
                width = origin.TryGetProperty("width", out JsonElement w) ? w.GetInt32() : 0;
                height = origin.TryGetProperty("height", out JsonElement h) ? h.GetInt32() : 0;
            }

            var spots = new List<Hotspot>();
            if (image.TryGetProperty("assignments", out JsonElement assignments) && assignments.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in assignments.EnumerateArray())
                {
                    string slotId = a.TryGetProperty("slotId", out JsonElement sid) ? sid.GetString() ?? string.Empty : string.Empty;
                    string slotName = a.TryGetProperty("slotName", out JsonElement sn) ? sn.GetString() ?? string.Empty : string.Empty;
                    if (!TryMapSlot(kind, slotId, slotName, out ushort id, out string name))
                    {
                        continue;
                    }

                    if (!a.TryGetProperty("marker", out JsonElement marker))
                    {
                        continue;
                    }

                    double x = marker.TryGetProperty("x", out JsonElement mx) ? mx.GetDouble() : 0;
                    double y = marker.TryGetProperty("y", out JsonElement my) ? my.GetDouble() : 0;
                    int side = 1;
                    if (a.TryGetProperty("label", out JsonElement label) && label.TryGetProperty("x", out JsonElement lx) && lx.GetDouble() < 0)
                    {
                        side = -1;
                    }

                    spots.Add(new Hotspot(id, name, slotName, x, y, side));
                }
            }

            return (width, height, spots);
        }

        return null;
    }

    /// <summary>
    /// Maps an Options+ slot to a Slender control id and display name. Mouse slots map by slot name; any slot also maps by
    /// the decimal CID after "_c" in its id ("ergo-k860-6b359_c199" is 199).
    /// </summary>
    public static bool TryMapSlot(DeviceKind kind, string slotId, string slotName, out ushort id, out string name)
    {
        if (kind == DeviceKind.Mouse && MouseSlots.TryGetValue(slotName, out (ushort Id, string Name) known))
        {
            id = known.Id;
            name = known.Name;
            return true;
        }

        Match m = DecimalCidSuffix().Match(slotId);
        if (m.Success && ushort.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort cid))
        {
            id = cid;
            name = FriendlyName(slotName);
            return true;
        }

        id = 0;
        name = string.Empty;
        return false;
    }

    /// <summary>"SLOT_NAME_VOLUME_UP" becomes "Volume up".</summary>
    public static string FriendlyName(string slotName)
    {
        if (NameOverrides.TryGetValue(slotName, out string? over))
        {
            return over;
        }

        string s = slotName;
        foreach (string prefix in new[] { "SLOT_NAME_", "ASSIGNMENT_NAME_" })
        {
            if (s.StartsWith(prefix, StringComparison.Ordinal))
            {
                s = s[prefix.Length..];
            }
        }

        if (s.EndsWith("_BUTTON", StringComparison.Ordinal))
        {
            s = s[..^"_BUTTON".Length];
        }

        s = s.Replace('_', ' ').ToLowerInvariant();
        return s.Length == 0 ? slotName : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
