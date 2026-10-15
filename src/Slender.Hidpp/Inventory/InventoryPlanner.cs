namespace Slender.Hidpp.Inventory;

public enum PlanDisposition { Called, ListedOnly }

/// <summary>Decision for one feature: which vetted getters to run, or why it is only listed.</summary>
public sealed record FeaturePlan(ushort FeatureId, PlanDisposition Disposition, string Reason, IReadOnlyList<InventoryGetter> Getters);

public sealed record PlannedCall(InventoryGetter Getter, byte[] Params, string Label);

/// <summary>
/// Decides what the inventory may send. It only ever returns getters from <see cref="InventoryGetters.Table"/>, never for a
/// feature in <see cref="InventoryGetters.NeverCall"/> and never for a feature flagged engineering/manufacturing.
/// </summary>
public static class InventoryPlanner
{
    public static FeaturePlan Plan(ushort featureId, FeatureTypeFlags flags)
    {
        if (InventoryGetters.NeverCall.Contains(featureId))
            return new(featureId, PlanDisposition.ListedOnly, "on the never-call list (firmware/reset/host/profile/engineering class)", []);
        if (flags.EngineeringOrManufacturing)
            return new(featureId, PlanDisposition.ListedOnly, "flagged engineering/manufacturing", []);
        if (featureId is 0x0000 or 0x0001)
            return new(featureId, PlanDisposition.Called, "read in the IRoot/IFeatureSet enumeration phase", []);
        var getters = InventoryGetters.For(featureId).Where(g => !g.Core).ToList();
        if (getters.Count == 0)
            return new(featureId, PlanDisposition.ListedOnly, "no vetted read-only getter in the table", []);
        return new(featureId, PlanDisposition.Called, "vetted read-only getters", getters);
    }

    /// <summary>
    /// Concrete calls for one getter. <paramref name="replies"/> holds the successful replies seen so far for this feature, by function
    /// (count getters and CID listings), so iteration only follows values the device itself reported.
    /// </summary>
    public static IReadOnlyList<PlannedCall> Expand(InventoryGetter g, IReadOnlyDictionary<byte, IReadOnlyList<byte[]>> replies)
    {
        var it = g.Iteration;
        if (it == null || it.Kind == IterationKind.None)
            return g.Params.Select(p => new PlannedCall(g, p, $"{g.Name}{(p.Length > 0 ? " [" + Convert.ToHexString(p) + "]" : "")}")).ToList();

        if (!replies.TryGetValue(it.SourceFunction, out var src) || src.Count == 0) return [];
        var tail = it.Tail ?? [];
        var calls = new List<PlannedCall>();
        switch (it.Kind)
        {
            case IterationKind.CountedIndex:
            {
                int count = Math.Min(src[0][it.CountByte], it.Max);
                for (int i = 0; i < count; i++)
                    calls.Add(new(g, [(byte)i, .. tail], $"{g.Name} #{i}"));
                break;
            }
            case IterationKind.CountedOffset:
            {
                int count = Math.Min(src[0][it.CountByte], it.Max);
                for (int off = 0; off < count; off += it.Step)
                    calls.Add(new(g, [(byte)off, .. tail], $"{g.Name} @{off}"));
                break;
            }
            case IterationKind.PerCid:
                foreach (var r in src.Take(it.Max))
                    calls.Add(new(g, [r[0], r[1], .. tail], $"{g.Name} {r[0]:X2}{r[1]:X2}"));
                break;
        }
        return calls;
    }
}
