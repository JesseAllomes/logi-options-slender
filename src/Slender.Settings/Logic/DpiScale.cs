using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>
/// The pointer speeds a slider offers: the sensor's real min/max/step (and explicit values when it lists unevenly) from the tray's
/// status, else the fixed 200-8000 range in steps of 50 while the mouse has not been read. Pure.
/// </summary>
public sealed record DpiScale(int Min, int Max, int Step, IReadOnlyList<int>? Values = null)
{
    /// <summary>The fixed range used while the sensor's list is unknown.</summary>
    public static DpiScale Fallback { get; } = new(PointerMapping.DpiMin, PointerMapping.DpiMax, PointerMapping.DpiStep);

    /// <summary>The mouse's published range, or <see cref="Fallback"/> when it has none (or an unusable one).</summary>
    public static DpiScale From(DeviceState? mouse)
    {
        if (mouse is not { DpiMin: { } min, DpiMax: { } max, DpiStep: { } step } || min <= 0 || max < min || step <= 0) return Fallback;
        IReadOnlyList<int>? values = mouse.DpiValues is { Count: > 0 } v && v.All(x => x > 0) ? [.. v.Order()] : null;
        return new(min, max, step, values);
    }

    /// <summary>The valid speed nearest <paramref name="value"/> (ties go lower), like the engine's clamp.</summary>
    public int Snap(double value)
    {
        if (Values is { Count: > 0 })
        {
            int best = Values[0];
            foreach (int v in Values)
            {
                if (Math.Abs(v - value) < Math.Abs(best - value)) best = v;
            }

            return best;
        }

        double c = Math.Clamp(value, Min, Max);
        int steps = (int)Math.Floor((c - Min) / Step + 0.5);
        return Math.Clamp(Min + steps * Step, Min, Max);
    }
}
