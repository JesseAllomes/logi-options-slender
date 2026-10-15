using System.Text.Json;
using System.Text.Json.Nodes;

namespace Slender.Engine.Import;

/// <summary>Macros from macros.db (JSON: macro_infos.macroInfos[]), flattened to key steps and delays for lookup and reporting.</summary>
internal sealed class MacroLibrary
{
    internal sealed record Step(int? Key, IReadOnlyList<int> Modifiers, string Label, int DelayMs = 0);

    internal sealed record Macro(string Id, string Name, IReadOnlyList<Step> All)
    {
        /// <summary>Key steps only (delays excluded).</summary>
        public IReadOnlyList<Step> Steps { get; } = All.Where(s => s.DelayMs == 0).ToList();

        public string Describe() => string.Join(", ", All.Select(s => s.DelayMs > 0 ? $"wait {s.DelayMs} ms" : s.Label));
    }

    private readonly List<Macro> _macros = [];

    public static MacroLibrary Parse(string? json, List<ImportNote> notes)
    {
        var lib = new MacroLibrary();
        if (string.IsNullOrWhiteSpace(json)) return lib;
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException)
        {
            notes.Add(new(ImportNoteKind.Unmapped, "macros.db", "The macros JSON could not be parsed; macro assignments cannot be imported."));
            return lib;
        }
        foreach (JsonNode? n in root?["macro_infos"]?["macroInfos"] as JsonArray ?? [])
        {
            if (n is not JsonObject m) continue;
            string id = OptionsPlusImporter.Str(m, "id") ?? "", name = OptionsPlusImporter.Str(m, "name") ?? id;
            var steps = new List<Step>();
            foreach (JsonNode? c in m["cards"] as JsonArray ?? [])
            {
                if (c?["macro"] is not JsonObject macro) continue;
                switch (OptionsPlusImporter.Str(macro, "type"))
                {
                    case "DELAY":
                        steps.Add(new Step(null, [], "", macro["delay"]?["durationMs"] is JsonValue dv && dv.TryGetValue(out int ms) && ms > 0 ? ms : 1));
                        break;
                    case "INPUT_SEQUENCE":
                        steps.Add(ReadSequence(macro["inputSequence"]));
                        break;
                    default:
                        steps.Add(new Step(null, [], $"unsupported step '{OptionsPlusImporter.Str(macro, "type")}'"));
                        break;
                }
            }
            lib._macros.Add(new Macro(id, name, steps));
        }
        return lib;
    }

    private static Step ReadSequence(JsonNode? seq)
    {
        var downs = new List<(int Usage, string Name)>();
        bool other = false;
        foreach (JsonNode? list in seq?["componentLists"] as JsonArray ?? [])
            foreach (JsonNode? comp in list?["components"] as JsonArray ?? [])
            {
                if (comp is not JsonObject o || o.ContainsKey("delay")) continue;
                if (o["keyboard"] is JsonObject kb)
                {
                    bool down = kb["isDown"] is JsonValue dv && dv.TryGetValue(out bool b) && b;
                    if (down && kb["hidUsage"] is JsonValue uv && uv.TryGetValue(out int u))
                        downs.Add((u, OptionsPlusImporter.Str(kb, "displayName") ?? "usage " + u));
                }
                else other = true;
            }
        var mods = downs.Where(d => HidKeys.IsModifier(d.Usage)).Select(d => d.Usage).ToList();
        var keys = downs.Where(d => !HidKeys.IsModifier(d.Usage)).ToList();
        string label = string.Join('+', downs.OrderBy(d => HidKeys.IsModifier(d.Usage) ? 0 : 1).Select(d => d.Name));
        if (other) label += " (+ non-keyboard input)";
        return new Step(keys.Count == 1 && !other ? keys[0].Usage : null, mods, label.Length > 0 ? label : "(empty input)");
    }

    public Macro? Find(string? id, string? name)
    {
        if (id is not null && _macros.FirstOrDefault(m => m.Id == id) is { } byId) return byId;
        // Card names can be truncated ("Enter and close tab (Ado"); fall back to a unique prefix match.
        if (!string.IsNullOrEmpty(name))
        {
            var hits = _macros.Where(m => m.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) || name.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 1) return hits[0];
        }
        return null;
    }
}
