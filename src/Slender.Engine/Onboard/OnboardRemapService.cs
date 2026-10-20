using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>
/// The tray's side of on-keyboard remaps. It does nothing by itself: no constructor, connect, start or config-reload path calls
/// it. <see cref="ReadAsync"/> only reads; <see cref="ApplyAsync"/> is the one place that writes keyboard flash, and it runs only
/// when Settings sends an apply request after the user saved a change.
/// </summary>
public sealed class OnboardRemapService
{
    private readonly IOnboardKeyboard _keyboard;
    private readonly ILog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OnboardRemapService(IOnboardKeyboard keyboard, ILog? log = null)
    {
        _keyboard = keyboard;
        _log = log ?? NullLog.Instance;
    }

    public async Task<OnboardReadReply> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadCoreAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<OnboardReadReply> ReadCoreAsync(CancellationToken ct)
    {
        try
        {
            var caps = await _keyboard.GetOnboardCapabilitiesAsync(ct).ConfigureAwait(false);
            if (caps is null) return new("this keyboard has no on-keyboard remapping", null, []);
            var maps = await _keyboard.ReadOnboardMappingsAsync(ct).ConfigureAwait(false);
            return new(null, caps, [.. maps.Where(m => OnboardRemapKeys.IsKnown(m.Cid))]);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HidppException or IOException or InvalidOperationException or TimeoutException)
        {
            return new(ex.Message, null, []);
        }
    }

    /// <summary>
    /// Sets <paramref name="sets"/> and resets <paramref name="resets"/>, through <see cref="OnboardSync"/>: only keys whose current
    /// mapping differs are written. A key in both lists is set. Stops at the first write failure; keys not tried report a failure.
    /// </summary>
    public async Task<OnboardApplyReply> ApplyAsync(IReadOnlyDictionary<ushort, string> sets, IEnumerable<ushort> resets, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var resetList = resets.Where(c => !sets.ContainsKey(c)).Distinct().ToList();
            if (sets.Count == 0 && resetList.Count == 0) return new(null, []);

            var read = await ReadCoreAsync(ct).ConfigureAwait(false);
            if (!read.Ok) return new(read.Error, []);

            var plan = OnboardSync.Plan(sets, read.Mappings, resetList);
            var results = new Dictionary<ushort, OnboardKeyResult>();
            foreach (var r in plan.Rejected) results[r.Cid] = new(r.Cid, OnboardOutcome.Failed, r.Reason);
            var listed = read.Mappings.Select(m => m.Cid).ToHashSet();
            foreach (var c in resetList.Where(c => !listed.Contains(c))) results[c] = new(c, OnboardOutcome.Failed, "keyboard does not list this control");

            string? stopped = null;
            foreach (var op in plan.Ops)
            {
                if (stopped is not null) { results[op.Cid] = new(op.Cid, OnboardOutcome.Failed, "not attempted: " + stopped); continue; }
                try
                {
                    await OnboardSync.ApplyAsync(_keyboard, new OnboardSyncPlan([op], []), ct).ConfigureAwait(false);
                    results[op.Cid] = new(op.Cid, OnboardOutcome.Applied);
                    _log.Info(op is OnboardSetOp s ? $"on-keyboard remap: set {s.Cid} to {s.Chord}" : $"on-keyboard remap: reset {op.Cid}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is HidppException or IOException or InvalidOperationException or TimeoutException)
                {
                    stopped = ex.Message;
                    results[op.Cid] = new(op.Cid, OnboardOutcome.Failed, ex.Message);
                    _log.Error($"on-keyboard remap of {op.Cid} failed", ex);
                }
            }

            var all = sets.Keys.Concat(resetList).Distinct().OrderBy(c => c)
                .Select(c => results.TryGetValue(c, out var r) ? r : new OnboardKeyResult(c, OnboardOutcome.Unchanged)).ToList();
            return new(null, all);
        }
        finally { _gate.Release(); }
    }

    /// <summary>One request line to one reply line (the pipe's whole job). Never throws for a bad line.</summary>
    public async Task<string> HandleLineAsync(string? line, CancellationToken ct)
    {
        try
        {
            if (line == OnboardProtocol.ReadLine()) return OnboardProtocol.FormatRead(await ReadAsync(ct).ConfigureAwait(false));
            if (!OnboardProtocol.TryParseApply(line, out var sets, out var resets, out var error)) return OnboardProtocol.ErrLine(error);
            return OnboardProtocol.FormatApply(await ApplyAsync(sets, resets, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("on-keyboard remap request failed", ex);
            return OnboardProtocol.ErrLine(ex.Message);
        }
    }
}
