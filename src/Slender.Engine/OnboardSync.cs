using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>One change to the keyboard's persistent memory.</summary>
public abstract record OnboardOp(ushort Cid);

/// <summary>Store <see cref="Action"/> (encoded from <see cref="Chord"/>) for the control.</summary>
public sealed record OnboardSetOp(ushort Cid, string Chord, OnboardAction Action) : OnboardOp(Cid);

/// <summary>Restore the control's factory mapping.</summary>
public sealed record OnboardResetOp(ushort Cid) : OnboardOp(Cid);

public sealed record OnboardRejected(ushort Cid, string Chord, string Reason);

/// <summary>Result of <see cref="OnboardSync.Plan"/>. <see cref="Ops"/> is empty when the keyboard already matches.</summary>
public sealed record OnboardSyncPlan(IReadOnlyList<OnboardOp> Ops, IReadOnlyList<OnboardRejected> Rejected)
{
    public bool IsEmpty => Ops.Count == 0;
}

/// <summary>
/// Pure diff between the desired on-keyboard map (CID to chord) and what the keyboard reports, giving the minimal set of
/// persistent writes. Every op writes keyboard flash, so nothing is emitted for a key that already matches.
/// </summary>
public static class OnboardSync
{
    /// <summary>
    /// <paramref name="desired"/>: CID to chord. <paramref name="current"/>: <see cref="IOnboardKeyboard.ReadOnboardMappingsAsync"/>.
    /// <paramref name="previouslyManaged"/>: CIDs Slender wrote earlier; those missing from <paramref name="desired"/> are reset
    /// (never touches a key Slender did not manage). A reset is skipped only for a key whose own 0x1C00 status says it is not
    /// remapped (<see cref="OnboardMapping.IsRemapped"/>); no table of assumed factory actions decides anything. A desired CID
    /// the keyboard does not list is rejected.
    /// </summary>
    public static OnboardSyncPlan Plan(
        IReadOnlyDictionary<ushort, string> desired,
        IReadOnlyList<OnboardMapping> current,
        IEnumerable<ushort>? previouslyManaged = null)
    {
        var now = new Dictionary<ushort, OnboardMapping>();
        foreach (var m in current) now[m.Cid] = m;

        var ops = new List<OnboardOp>();
        var rejected = new List<OnboardRejected>();

        foreach (var (cid, chord) in desired.OrderBy(kv => kv.Key))
        {
            if (!now.TryGetValue(cid, out var have)) { rejected.Add(new(cid, chord, "keyboard does not list this control")); continue; }
            if (!OnboardChordEncoder.TryEncode(chord, out var want, out var err)) { rejected.Add(new(cid, chord, err)); continue; }
            if (want != have.Action) ops.Add(new OnboardSetOp(cid, chord, want));
        }

        if (previouslyManaged != null)
        {
            foreach (var cid in previouslyManaged.Distinct().OrderBy(c => c))
            {
                if (desired.ContainsKey(cid) || !now.TryGetValue(cid, out var have)) continue;
                if (!have.IsRemapped) continue; // the keyboard itself says this key is on its own mapping
                ops.Add(new OnboardResetOp(cid));
            }
        }
        return new OnboardSyncPlan(ops, rejected);
    }

    /// <summary>Runs the plan in order. Stops at the first failure so a half-applied plan is not extended.</summary>
    public static async Task ApplyAsync(IOnboardKeyboard keyboard, OnboardSyncPlan plan, CancellationToken ct)
    {
        foreach (var op in plan.Ops)
        {
            ct.ThrowIfCancellationRequested();
            switch (op)
            {
                case OnboardSetOp s: await keyboard.SetOnboardActionAsync(s.Cid, s.Action, ct).ConfigureAwait(false); break;
                case OnboardResetOp r: await keyboard.ResetOnboardKeyAsync(r.Cid, ct).ConfigureAwait(false); break;
            }
        }
    }
}
