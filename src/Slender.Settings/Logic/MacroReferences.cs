using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>
/// Finds and removes every place an <see cref="ActionSpec.Macro"/> refers to one macro: mouse buttons, keys, gesture swipes, ring slots
/// (folders too), the thumb wheel's second action and flick, and either half of a Press / Hold, in every profile. Pure.
/// </summary>
public static class MacroReferences
{
    /// <summary>How many places use the macro.</summary>
    public static int Count(SlenderConfig config, string id)
    {
        Scrub(config, id, out int n);
        return n;
    }

    /// <summary>The config with every reference to the macro set to None (the macro itself stays); <paramref name="count"/> is how many were changed.</summary>
    public static SlenderConfig Scrub(SlenderConfig config, string id, out int count)
    {
        int n = 0;
        var profiles = new List<Profile>(config.Profiles.Count);
        bool changed = false;
        foreach (Profile p in config.Profiles)
        {
            Profile q = ScrubProfile(p, id, ref n);
            changed |= !ReferenceEquals(p, q);
            profiles.Add(q);
        }

        count = n;
        return changed ? config with { Profiles = profiles } : config;
    }

    /// <summary>Delete a macro and clear everything that used it.</summary>
    public static SlenderConfig Delete(SlenderConfig config, string id) => MacroOps.Remove(Scrub(config, id, out _), id);

    /// <summary>"Used by 3 controls. Deleting the macro turns them off." and so on.</summary>
    public static string UsageText(int count) => count switch
    {
        0 => "Not used by any control.",
        1 => "Used by 1 control. Deleting the macro turns that control off.",
        _ => $"Used by {count} controls. Deleting the macro turns them off.",
    };

    private static Profile ScrubProfile(Profile p, string id, ref int n)
    {
        int before = n;
        var bindings = ScrubMap(p.Bindings, id, ref n);
        var keys = ScrubMap(p.KeyBindings, id, ref n);
        var swipes = ScrubMap(p.GestureSwipes, id, ref n);
        var ring = ScrubSlots(p.RingSlots, id, ref n);
        ThumbWheelSecond? alt = p.ThumbWheelAlt is { } a
            ? a with { Forward = ScrubAction(a.Forward, id, ref n), Back = ScrubAction(a.Back, id, ref n) } : null;
        ThumbWheelFlick? flick = p.ThumbFlick is { } f
            ? f with { Right = ScrubAction(f.Right, id, ref n), Left = ScrubAction(f.Left, id, ref n) } : null;
        return n == before ? p : p with { Bindings = bindings, KeyBindings = keys, GestureSwipes = swipes, RingSlots = ring, ThumbWheelAlt = alt, ThumbFlick = flick };
    }

    private static IReadOnlyDictionary<TKey, ActionSpec> ScrubMap<TKey>(IReadOnlyDictionary<TKey, ActionSpec> map, string id, ref int n) where TKey : notnull
    {
        Dictionary<TKey, ActionSpec>? copy = null;
        foreach (var (key, action) in map)
        {
            int before = n;
            ActionSpec scrubbed = ScrubAction(action, id, ref n);
            if (n != before)
            {
                copy ??= new Dictionary<TKey, ActionSpec>(map);
                copy[key] = scrubbed;
            }
        }

        return copy ?? map;
    }

    private static IReadOnlyList<RingSlot>? ScrubSlots(IReadOnlyList<RingSlot>? slots, string id, ref int n)
    {
        if (slots is null)
        {
            return null;
        }

        List<RingSlot>? copy = null;
        for (int i = 0; i < slots.Count; i++)
        {
            RingSlot slot = slots[i];
            int before = n;
            RingSlot scrubbed = slot.Folder is not null
                ? slot with { Folder = ScrubSlots(slot.Folder, id, ref n) } // a folder's own Action is ignored
                : slot with { Action = ScrubAction(slot.Action, id, ref n) };
            if (n != before)
            {
                copy ??= [.. slots];
                copy[i] = scrubbed;
            }
        }

        return copy ?? slots;
    }

    private static ActionSpec ScrubAction(ActionSpec action, string id, ref int n)
    {
        switch (action)
        {
            case ActionSpec.Macro m when m.Id == id:
                n++;
                return new ActionSpec.None();
            case ActionSpec.TapHold th:
                int before = n;
                ActionSpec tap = ScrubAction(th.Tap, id, ref n);
                ActionSpec hold = ScrubAction(th.Hold, id, ref n);
                if (n == before)
                {
                    return action;
                }

                return tap is ActionSpec.None && hold is ActionSpec.None ? new ActionSpec.None() : new ActionSpec.TapHold(tap, hold);
            default:
                return action;
        }
    }
}
