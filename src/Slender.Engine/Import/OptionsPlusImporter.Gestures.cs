using System.Globalization;
using System.Text.Json.Nodes;
using Slender.Hidpp;

namespace Slender.Engine.Import;

public static partial class OptionsPlusImporter
{
    private static readonly (string Key, GestureDirection Direction)[] SwipeDirections =
    [
        ("up", GestureDirection.Up), ("down", GestureDirection.Down), ("left", GestureDirection.Left), ("right", GestureDirection.Right),
    ];

    /// <summary>
    /// Gesture swipes: the selected preset's up/down/left/right inner cards become Profile.GestureSwipes (the click card
    /// is handled by <see cref="ConvertGesture"/>). App profiles keep only what differs from the default profile; an
    /// empty direction where the default has a swipe becomes an explicit None. Presets with no direction actions
    /// (pan, zoom/rotate) leave the swipes inherited.
    /// </summary>
    private static void ApplyGestureSwipes(IReadOnlyList<ParsedProfile> profiles, State state, List<Profile> merged)
    {
        var defaultSwipes = new Dictionary<GestureDirection, ActionSpec>();
        foreach (ParsedProfile pp in profiles.OrderByDescending(p => p.IsDefault))
        {
            string? process = pp.IsDefault ? null
                : pp.App?.Path is { } path ? Path.GetFileNameWithoutExtension(path).ToLowerInvariant() : null;
            if (!pp.IsDefault && process is null) continue; // not imported (reported by Import)

            var swipes = ConvertSwipes(pp, state, pp.IsDefault ? null : defaultSwipes);
            if (pp.IsDefault) foreach (var kv in swipes) defaultSwipes[kv.Key] = kv.Value;

            int i = merged.FindIndex(p => process is null
                ? p.AppProcess is null
                : p.AppProcess is not null && string.Equals(Strip(p.AppProcess), process, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) merged[i] = merged[i] with { GestureSwipes = swipes };
        }
    }

    private static Dictionary<GestureDirection, ActionSpec> ConvertSwipes(ParsedProfile profile, State state,
        IReadOnlyDictionary<GestureDirection, ActionSpec>? baseline)
    {
        var result = new Dictionary<GestureDirection, ActionSpec>();
        foreach (JsonNode? n in profile.Json["assignments"] as JsonArray ?? [])
        {
            if (n is not JsonObject a || Str(a, "slotId") is not { } slot || !slot.StartsWith(MouseDevicePrefix, StringComparison.Ordinal) || !IsGestureSlot(slot)
                || a["card"] is not JsonObject card || Str(card, "attribute") != "ONE_OF") continue;
            string where = $"{profile.DisplayName} / {slot}";
            string? selected = Str(card, "selectedNestedCard");
            if (selected is null || card["nestedCards"]?[selected]?["nestedCards"] is not JsonObject inner) continue;

            bool any = SwipeDirections.Any(d => HasMacro(inner[d.Key]));
            if (!any) continue; // motion preset or empty: swipes stay inherited
            if (HasMacro(inner["horizontal"]))
                state.Notes.Add(new(ImportNoteKind.Unmapped, where + " horizontal", $"Gesture preset '{selected}' has a continuous horizontal item; Slender swipes fire once per release, so it was not imported."));

            foreach (var (key, direction) in SwipeDirections)
            {
                ActionSpec? action = null;
                if (HasMacro(inner[key]))
                {
                    Conv c = ConvertCard((JsonObject)inner[key]!, state.Macros);
                    if (c.Approximation is not null) state.Notes.Add(new(ImportNoteKind.Approximated, $"{where} swipe {key}", c.Approximation));
                    if (c.Action is null)
                    {
                        bool overrides = baseline is not null && baseline.ContainsKey(direction);
                        state.Notes.Add(new(ImportNoteKind.Unmapped, $"{where} swipe {key}", (c.Problem ?? "Unsupported assignment.")
                            + (overrides ? " Swiping this way does nothing in this app (the default profile's swipe is not used)." : "")));
                        if (overrides) result[direction] = new ActionSpec.None();
                        continue;
                    }
                    action = c.Action;
                }
                action ??= new ActionSpec.None();
                if (baseline is null)
                {
                    if (action is not ActionSpec.None) result[direction] = action;
                }
                else if (baseline.TryGetValue(direction, out var inherited) ? !inherited.Equals(action) : action is not ActionSpec.None)
                {
                    result[direction] = action; // differs from default (including an explicit None)
                }
            }
        }
        return result;
    }

    private static bool IsGestureSlot(string slot) => slot.EndsWith("_c" + ControlId.Gesture.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static bool HasMacro(JsonNode? node) => node is JsonObject o && o["macro"] is JsonObject { Count: > 0 };
}
