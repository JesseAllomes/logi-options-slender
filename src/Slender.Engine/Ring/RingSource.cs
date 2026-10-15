namespace Slender.Engine.Ring;

/// <summary>Where a ring's slots came from.</summary>
public enum RingSourceKind { AppProfile, DefaultProfile, BuiltIn }

/// <summary>The slots to show for the foreground app, always exactly <see cref="Profile.RingSlotCount"/> entries.</summary>
public sealed record RingSource(IReadOnlyList<RingSlot> Slots, RingSourceKind Kind)
{
    /// <summary>
    /// The foreground app's own RingSlots, else (whole ring, never slot by slot) the default profile's, else the built-in
    /// defaults. Empty slots inside a ring stay empty.
    /// </summary>
    public static RingSource Resolve(SlenderConfig config, string? process)
    {
        var profile = ProfileResolver.Resolve(config, process);
        if (profile.RingSlots is { } own)
            return new RingSource(Normalize(own), profile.AppProcess is null ? RingSourceKind.DefaultProfile : RingSourceKind.AppProfile);
        if (ProfileResolver.Default(config).RingSlots is { } def)
            return new RingSource(Normalize(def), RingSourceKind.DefaultProfile);
        return new RingSource(Normalize(RingDefaults.Slots()), RingSourceKind.BuiltIn);
    }

    /// <summary>Pads with empty slots or cuts to the slot count; null entries become empty.</summary>
    public static IReadOnlyList<RingSlot> Normalize(IReadOnlyList<RingSlot>? slots)
    {
        var result = new RingSlot[Profile.RingSlotCount];
        for (var i = 0; i < result.Length; i++)
            result[i] = slots is not null && i < slots.Count && slots[i] is { } s ? s : RingSlot.Empty;
        return result;
    }
}

/// <summary>The two directions of an adjustable slot (mouse wheel over the ring).</summary>
public static class RingWheel
{
    /// <summary>
    /// Wheel up and wheel down actions for an adjustable slot: Keys "A|B" (up = A, down = B, for example VolumeUp|VolumeDown) or
    /// Brightness (+step, -step). Null when the action is not adjustable.
    /// </summary>
    public static (ActionSpec Up, ActionSpec Down)? TryPair(ActionSpec action)
    {
        switch (action)
        {
            case ActionSpec.Keys k when k.Chord.Contains('|'):
                var (up, down) = SlenderEngine.SplitWheelChord(k.Chord);
                return up.Length == 0 || down.Length == 0 ? null : (new ActionSpec.Keys(up), new ActionSpec.Keys(down));
            case ActionSpec.Brightness b when b.StepPercent != 0:
                var step = Math.Abs(b.StepPercent);
                return (new ActionSpec.Brightness(step), new ActionSpec.Brightness(-step));
            default:
                return null;
        }
    }
}
