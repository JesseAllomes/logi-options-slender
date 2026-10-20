using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class OnboardSyncTests
{
    private static OnboardMapping M(ushort cid, OnboardAction a) => new(cid, a, 0);
    private static OnboardMapping R(ushort cid, OnboardAction a) => new(cid, a, 1); // the keyboard says remapped
    private static readonly OnboardAction WinTab = OnboardAction.Keystroke(0x2B, OnboardModifiers.Meta);
    private static readonly OnboardAction PlayPause = OnboardAction.Consumer(0xCD);

    private static readonly OnboardMapping[] Current =
    [
        M(224, WinTab), M(229, PlayPause), M(199, default),
    ];

    [Fact]
    public void Matching_state_yields_no_ops()
    {
        var plan = OnboardSync.Plan(new Dictionary<ushort, string> { [224] = "Win+Tab", [229] = "MediaPlayPause" }, Current);
        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Rejected);
    }

    [Fact]
    public void Only_differing_keys_are_set_in_cid_order()
    {
        var plan = OnboardSync.Plan(new Dictionary<ushort, string> { [229] = "Ctrl+S", [224] = "Win+Tab", [199] = "F5" }, Current);
        var ops = plan.Ops.Cast<OnboardSetOp>().ToList();
        Assert.Equal([199, 229], ops.Select(o => o.Cid).ToArray());
        Assert.Equal(OnboardAction.Keystroke(0x3E, OnboardModifiers.None), ops[0].Action);
        Assert.Equal(OnboardAction.Keystroke(0x16, OnboardModifiers.Ctrl), ops[1].Action);
    }

    [Fact]
    public void Unencodable_and_unknown_controls_are_rejected_not_written()
    {
        var plan = OnboardSync.Plan(new Dictionary<ushort, string> { [224] = "Ctrl+Shift", [999] = "A", [199] = "Banana" }, Current);
        Assert.Empty(plan.Ops);
        Assert.Equal([199, 224, 999], plan.Rejected.Select(r => r.Cid).Order().ToArray());
    }

    [Fact]
    public void Previously_managed_keys_dropped_from_desired_are_reset()
    {
        var changed = new[] { R(224, OnboardAction.Keystroke(0x16, OnboardModifiers.Ctrl)), R(229, PlayPause) };
        var plan = OnboardSync.Plan(new Dictionary<ushort, string>(), changed, previouslyManaged: [224, 229, 500]);
        // both read remapped (status 1), so both are reset; 500 is not on the keyboard so it is skipped
        Assert.Equal([224, 229], plan.Ops.OfType<OnboardResetOp>().Select(o => o.Cid).ToArray());
    }

    [Fact]
    public void Reset_is_skipped_when_the_keyboard_reports_the_key_not_remapped()
    {
        var plan = OnboardSync.Plan(new Dictionary<ushort, string>(), Current, previouslyManaged: [224, 229]);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Reset_follows_the_status_byte_not_the_action_it_reads()
    {
        // Same action bytes, different status: only the one the keyboard calls remapped is reset. No table of factory actions is consulted.
        var plan = OnboardSync.Plan(new Dictionary<ushort, string>(), [M(224, WinTab), R(229, WinTab)], previouslyManaged: [224, 229]);
        Assert.Equal([229], plan.Ops.OfType<OnboardResetOp>().Select(o => o.Cid).ToArray());
    }

    [Fact]
    public void Any_non_zero_status_counts_as_remapped()
    {
        Assert.False(new OnboardMapping(1, default, 0).IsRemapped);
        Assert.True(new OnboardMapping(1, default, 1).IsRemapped);
        Assert.True(new OnboardMapping(1, default, 0x80).IsRemapped);
    }

    [Fact]
    public void Unmanaged_keys_are_never_reset()
    {
        var changed = new[] { R(224, OnboardAction.Keystroke(0x16, OnboardModifiers.Ctrl)) };
        Assert.True(OnboardSync.Plan(new Dictionary<ushort, string>(), changed).IsEmpty);
        Assert.True(OnboardSync.Plan(new Dictionary<ushort, string>(), changed, previouslyManaged: []).IsEmpty);
    }

    private sealed class FakeOnboard : IOnboardKeyboard
    {
        public List<string> Log = [];
        public ushort? FailOn;
        public Task<ushort?> GetOnboardCapabilitiesAsync(CancellationToken ct) => Task.FromResult<ushort?>(0x0141);
        public Task<IReadOnlyList<OnboardMapping>> ReadOnboardMappingsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OnboardMapping>>(Current);
        public Task SetOnboardActionAsync(ushort cid, OnboardAction a, CancellationToken ct)
        {
            if (cid == FailOn) throw new HidppException("boom");
            Log.Add($"set {cid} {Convert.ToHexString(a.ToBytes())}");
            return Task.CompletedTask;
        }
        public Task SetOnboardChordAsync(ushort cid, string chord, CancellationToken ct) => SetOnboardActionAsync(cid, OnboardChordEncoder.Encode(chord), ct);
        public Task ResetOnboardKeyAsync(ushort cid, CancellationToken ct) { Log.Add($"reset {cid}"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Apply_runs_ops_in_order_and_stops_at_first_failure()
    {
        var plan = OnboardSync.Plan(new Dictionary<ushort, string> { [199] = "F5", [229] = "F6" },
            Current.Append(R(500, default)).ToArray(), previouslyManaged: [500]);
        // ops: set 199, set 229, reset 500
        var ok = new FakeOnboard();
        await OnboardSync.ApplyAsync(ok, plan, default);
        Assert.Equal(["set 199 01003E00", "set 229 01003F00", "reset 500"], ok.Log.ToArray());

        var bad = new FakeOnboard { FailOn = 199 };
        await Assert.ThrowsAsync<HidppException>(() => OnboardSync.ApplyAsync(bad, plan, default));
        Assert.Empty(bad.Log);
    }
}
