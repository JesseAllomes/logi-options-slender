using System.Globalization;
using System.Text.Json.Nodes;

namespace Slender.Engine.Import;

public static partial class OptionsPlusImporter
{
    private const string RadialDevicePrefix = "radial-menu-virtual-device-";
    private const int FirstRingControl = 408; // radial-menu-virtual-device-10000000_c408 = slot 0, clockwise from the top

    /// <summary>
    /// Actions Ring: c408..c415 of any profile become that profile's RingSlots. A profile with no ring assignments keeps
    /// its RingSlots (null follows All apps). A FOLDER card becomes a folder: children from nestedCards in nestedCardsOrder,
    /// name from the card when it is not an ASSIGNMENT_NAME_* key, else "Folder".
    /// </summary>
    private static void ApplyRingSlots(IReadOnlyList<ParsedProfile> profiles, State state, List<Profile> merged)
    {
        foreach (ParsedProfile pp in profiles)
        {
            string? process = pp.IsDefault ? null
                : pp.App?.Path is { } path ? Path.GetFileNameWithoutExtension(path).ToLowerInvariant() : null;
            if (!pp.IsDefault && process is null) continue; // not imported (reported by Import)

            RingSlot[]? ring = ConvertRing(pp, state);
            if (ring is null) continue;

            int i = merged.FindIndex(p => process is null
                ? p.AppProcess is null
                : p.AppProcess is not null && string.Equals(Strip(p.AppProcess), process, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) merged[i] = merged[i] with { RingSlots = ring };
        }
    }

    private static RingSlot[]? ConvertRing(ParsedProfile profile, State state)
    {
        RingSlot[]? ring = null;
        foreach (JsonNode? n in profile.Json["assignments"] as JsonArray ?? [])
        {
            if (n is not JsonObject a || Str(a, "slotId") is not { } slot || a["card"] is not JsonObject card) continue;
            int us = slot.IndexOf('_');
            if (us <= 0 || !slot.StartsWith(RadialDevicePrefix, StringComparison.Ordinal)) continue;
            string rest = slot[(us + 1)..];
            if (rest.Length < 2 || rest[0] != 'c' || !int.TryParse(rest.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int cid)
                || cid < FirstRingControl || cid >= FirstRingControl + Profile.RingSlotCount) continue; // reported by ConvertProfile

            ring ??= Enumerable.Repeat(RingSlot.Empty, Profile.RingSlotCount).ToArray();
            ring[cid - FirstRingControl] = ConvertRingCard(card, $"{profile.DisplayName} / {slot}", state, allowFolder: true);
        }

        return ring;
    }

    private static RingSlot ConvertRingCard(JsonObject card, string where, State state, bool allowFolder)
    {
        if (Str(card, "attribute") == "FOLDER")
        {
            if (!allowFolder)
            {
                state.Notes.Add(new(ImportNoteKind.Unmapped, where, "A folder inside a folder is not supported; it was left empty."));
                return RingSlot.Empty;
            }

            string name = Str(card, "name") is { Length: > 0 } custom && !custom.StartsWith("ASSIGNMENT_NAME_", StringComparison.Ordinal) ? custom : "Folder";
            var children = new List<RingSlot>();
            if (card["nestedCards"] is JsonObject nested)
            {
                var keys = new List<string>();
                foreach (JsonNode? k in card["nestedCardsOrder"] as JsonArray ?? [])
                    if (k is JsonValue kv && kv.TryGetValue(out string? key) && nested.ContainsKey(key)) keys.Add(key);
                keys.AddRange(nested.Select(kv => kv.Key).Where(k => !keys.Contains(k)));
                foreach (string key in keys)
                {
                    if (nested[key] is not JsonObject child) continue;
                    if (children.Count == Profile.RingSlotCount)
                    {
                        state.Notes.Add(new(ImportNoteKind.Unmapped, $"{where} / {name}", $"Slender folders hold {Profile.RingSlotCount} actions; '{Humanize(Str(child, "name"))}' and any after it were not imported."));
                        break;
                    }

                    children.Add(ConvertRingCard(child, $"{where} / {name} / {key}", state, allowFolder: false));
                }
            }

            while (children.Count < Profile.RingSlotCount) children.Add(RingSlot.Empty);
            return new RingSlot(new ActionSpec.None()) { Name = name, Folder = children };
        }

        Conv c = ConvertCard(card, state.Macros);
        if (c.Approximation is not null) state.Notes.Add(new(ImportNoteKind.Approximated, where, c.Approximation));
        if (c.Action is null)
        {
            state.Notes.Add(new(ImportNoteKind.Unmapped, where, $"Actions ring item ({DescribeCard(card, state.Macros)}) was left empty."));
            return RingSlot.Empty;
        }

        return new RingSlot(c.Action);
    }
}
