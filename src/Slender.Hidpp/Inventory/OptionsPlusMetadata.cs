using System.Text.Json;
using System.Text.RegularExpressions;

namespace Slender.Hidpp.Inventory;

/// <summary>
/// Labels Options+ gives a device, read from its local depot JSON (slot ids such as "mx-master-4-2b042_c82" and slot names such as
/// SLOT_NAME_BRIGHTNESS_DOWN). Strictly read-only: files are copied to a temp folder and parsed there; nothing under the depots is touched.
/// </summary>
public sealed partial class OptionsPlusMetadata
{
    public static readonly OptionsPlusMetadata Empty = new();

    /// <summary>CID (decimal in the slot id) to label.</summary>
    public Dictionary<ushort, string> CidLabels { get; } = [];

    /// <summary>Non-CID slot ids (settings pages etc.), for the report.</summary>
    public SortedSet<string> Slots { get; } = [];

    /// <summary>Feature id to the Options+ settings slot that exposes it (by slot-id suffix).</summary>
    public Dictionary<ushort, string> FeatureSlots { get; } = [];

    // Suffix of a settings slot id -> HID++ feature it surfaces. Names from the slot ids themselves; the mapping is a best-effort guess.
    private static readonly (string Suffix, ushort Feature)[] SlotFeatures =
    [
        ("haptic_settings", 0x19B0), ("force_sensing_settings", 0x19C0), ("thumb_wheel_settings", 0x2150),
        ("mouse_wheel_settings", 0x2121), ("mouse_scroll_wheel_settings", 0x2121), ("mouse_settings", 0x2201),
        ("host_change", 0x1814), ("easy_switch_1", 0x1814), ("thumb_wheel_adapter", 0x2150),
    ];

    [GeneratedRegex(@"_c(\d+)$")]
    private static partial Regex CidSuffix();

    /// <summary>Parses one depot JSON document, collecting slots whose id starts with <paramref name="slug"/>.</summary>
    public void ParseJson(string json, string slug)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return; }
        using (doc) Walk(doc.RootElement, slug);
    }

    private void Walk(JsonElement e, string slug)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                if (e.TryGetProperty("slotId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { } slotId
                    && slotId.StartsWith(slug, StringComparison.OrdinalIgnoreCase))
                {
                    string? name = e.TryGetProperty("slotName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    Add(slotId, name, slug);
                }
                foreach (var p in e.EnumerateObject()) Walk(p.Value, slug);
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Walk(x, slug);
                break;
        }
    }

    private void Add(string slotId, string? slotName, string slug)
    {
        string label = Pretty(slotName);
        var m = CidSuffix().Match(slotId);
        if (m.Success && ushort.TryParse(m.Groups[1].Value, out var cid))
        {
            if (label.Length > 0 || !CidLabels.ContainsKey(cid)) CidLabels[cid] = label.Length > 0 ? label : "(unnamed Options+ slot)";
            return;
        }
        Slots.Add(label.Length > 0 ? $"{slotId} ({label})" : slotId);
        string suffix = slotId[slug.Length..].TrimStart('_', '-');
        foreach (var (s, f) in SlotFeatures)
            if (suffix.Equals(s, StringComparison.OrdinalIgnoreCase)) FeatureSlots.TryAdd(f, slotId);
    }

    private static string Pretty(string? slotName)
    {
        if (string.IsNullOrEmpty(slotName)) return "";
        var t = slotName.StartsWith("SLOT_NAME_", StringComparison.Ordinal) ? slotName["SLOT_NAME_".Length..] : slotName;
        return string.Join(' ', t.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    /// <summary>
    /// Scans depot folders (two levels, plugin depots skipped) for JSON mentioning <paramref name="slug"/>, copying each candidate to
    /// <paramref name="tempRoot"/> first. Returns <see cref="Empty"/>-like data when the folder is absent.
    /// </summary>
    public static OptionsPlusMetadata Load(string slug, string depotsRoot, string tempRoot)
    {
        var result = new OptionsPlusMetadata();
        try
        {
            if (!Directory.Exists(depotsRoot)) return result;
            Directory.CreateDirectory(tempRoot);
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
            int n = 0;
            foreach (var file in Directory.EnumerateFiles(depotsRoot, "*.json", options))
            {
                if (file.Contains("plugin", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (new FileInfo(file).Length > 2_000_000) continue;
                    var copy = Path.Combine(tempRoot, $"{n++}.json");
                    File.Copy(file, copy, overwrite: true);
                    var text = File.ReadAllText(copy);
                    if (text.Contains(slug, StringComparison.OrdinalIgnoreCase)) result.ParseJson(text, slug);
                    File.Delete(copy);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }
}
