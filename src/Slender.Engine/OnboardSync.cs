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
    /// (never touches a key Slender did not manage). <paramref name="factoryDefaults"/>: when given, a reset is skipped for a key
    /// that already reads its factory action. A desired CID the keyboard does not list is rejected.
    /// </summary>
    public static OnboardSyncPlan Plan(
        IReadOnlyDictionary<ushort, string> desired,
        IReadOnlyList<OnboardMapping> current,
        IEnumerable<ushort>? previouslyManaged = null,
        IReadOnlyDictionary<ushort, OnboardAction>? factoryDefaults = null)
    {
        var now = new Dictionary<ushort, OnboardAction>();
        foreach (var m in current) now[m.Cid] = m.Action;

        var ops = new List<OnboardOp>();
        var rejected = new List<OnboardRejected>();

        foreach (var (cid, chord) in desired.OrderBy(kv => kv.Key))
        {
            if (!now.TryGetValue(cid, out var have)) { rejected.Add(new(cid, chord, "keyboard does not list this control")); continue; }
            if (!OnboardChordEncoder.TryEncode(chord, out var want, out var err)) { rejected.Add(new(cid, chord, err)); continue; }
            if (want != have) ops.Add(new OnboardSetOp(cid, chord, want));
        }

        if (previouslyManaged != null)
        {
            foreach (var cid in previouslyManaged.Distinct().OrderBy(c => c))
            {
                if (desired.ContainsKey(cid) || !now.TryGetValue(cid, out var have)) continue;
                if (factoryDefaults != null && factoryDefaults.TryGetValue(cid, out var def) && def == have) continue;
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

    /// <summary>
    /// Actions read from the ERGO K860 on 2026-10-15 (docs/protocol.md). Assumed to be factory defaults but not proven;
    /// pass to <see cref="Plan"/> to avoid redundant resets.
    /// </summary>
    public static IReadOnlyDictionary<ushort, OnboardAction> K860ObservedDefaults { get; } = new Dictionary<ushort, OnboardAction>
    {
        [224] = OnboardAction.Keystroke(0x2B, OnboardModifiers.Meta),
        [225] = OnboardAction.Keystroke(0x04, OnboardModifiers.Meta),
        [110] = OnboardAction.Keystroke(0x07, OnboardModifiers.Meta),
        [213] = OnboardAction.Consumer(0x0223),
        [228] = OnboardAction.Consumer(0x00B6),
        [229] = OnboardAction.Consumer(0x00CD),
        [230] = OnboardAction.Consumer(0x00B5),
        [231] = OnboardAction.Consumer(0x00E2),
        [232] = OnboardAction.Consumer(0x00EA),
        [233] = OnboardAction.Consumer(0x00E9),
        [191] = OnboardAction.Keystroke(0x46, OnboardModifiers.None),
        [10] = OnboardAction.Consumer(0x0192),
        [234] = OnboardAction.Keystroke(0x65, OnboardModifiers.None),
        [111] = OnboardAction.Keystroke(0x0F, OnboardModifiers.Meta),
    };
}
