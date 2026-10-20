using Slender.Engine;
using Slender.Hidpp;
using Slender.Tray;

namespace Slender.Tests.Engine;

public class OnboardRemapServiceTests
{
    private static readonly OnboardAction WinTab = OnboardAction.Keystroke(0x2B, OnboardModifiers.Meta);
    private static readonly OnboardAction PlayPause = OnboardAction.Consumer(0xCD);

    private sealed class FakeOnboard : IOnboardKeyboard
    {
        public readonly Dictionary<ushort, OnboardAction> State = new()
        {
            [224] = WinTab, [229] = PlayPause, [199] = default, [233] = OnboardAction.Consumer(0xE9),
        };
        public readonly List<string> Writes = [];
        public int Reads;
        public bool Disconnected;
        public ushort? FailOn;

        public Task<ushort?> GetOnboardCapabilitiesAsync(CancellationToken ct)
        {
            if (Disconnected) throw new HidppException("ERGO K860 not connected");
            return Task.FromResult<ushort?>(0x0141);
        }

        public Task<IReadOnlyList<OnboardMapping>> ReadOnboardMappingsAsync(CancellationToken ct)
        {
            if (Disconnected) throw new HidppException("ERGO K860 not connected");
            Reads++;
            return Task.FromResult<IReadOnlyList<OnboardMapping>>([.. State.Select(kv => new OnboardMapping(kv.Key, kv.Value, 0))]);
        }

        public Task SetOnboardActionAsync(ushort cid, OnboardAction a, CancellationToken ct)
        {
            if (cid == FailOn) throw new HidppException("boom");
            Writes.Add($"set {cid} {Convert.ToHexString(a.ToBytes())}");
            State[cid] = a;
            return Task.CompletedTask;
        }

        public Task SetOnboardChordAsync(ushort cid, string chord, CancellationToken ct) => SetOnboardActionAsync(cid, OnboardChordEncoder.Encode(chord), ct);

        public Task ResetOnboardKeyAsync(ushort cid, CancellationToken ct)
        {
            if (cid == FailOn) throw new HidppException("boom");
            Writes.Add($"reset {cid}");
            State[cid] = OnboardRemapKeys.FactoryDefaults.GetValueOrDefault(cid);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Creating_the_service_and_reading_never_writes()
    {
        var kb = new FakeOnboard();
        var service = new OnboardRemapService(kb);
        Assert.Empty(kb.Writes);
        Assert.Equal(0, kb.Reads);
        var read = await service.ReadAsync(default);
        await service.HandleLineAsync("read", default);
        Assert.True(read.Ok);
        Assert.Equal((ushort)0x0141, read.Capabilities);
        Assert.Equal([199, 224, 229, 233], read.Mappings.Select(m => (int)m.Cid).Order().ToArray());
        Assert.Empty(kb.Writes);
    }

    [Fact]
    public async Task Read_back_reports_not_connected()
    {
        var service = new OnboardRemapService(new FakeOnboard { Disconnected = true });
        var read = await service.ReadAsync(default);
        Assert.False(read.Ok);
        Assert.Contains("not connected", read.Error);
    }

    [Fact]
    public async Task Apply_writes_only_what_differs()
    {
        var kb = new FakeOnboard();
        var service = new OnboardRemapService(kb);
        var reply = await service.ApplyAsync(new Dictionary<ushort, string> { [224] = "Win+Tab", [229] = "Ctrl+Esc" }, [], default);
        Assert.True(reply.AllSucceeded);
        Assert.Equal(["set 229 01002901"], kb.Writes);
        Assert.Equal(OnboardOutcome.Unchanged, reply.Results.Single(r => r.Cid == 224).Outcome);
        Assert.Equal(OnboardOutcome.Applied, reply.Results.Single(r => r.Cid == 229).Outcome);
    }

    [Fact]
    public async Task Reset_restores_factory_and_is_skipped_when_already_default()
    {
        var kb = new FakeOnboard();
        kb.State[229] = OnboardAction.Keystroke(0x29, OnboardModifiers.Ctrl);
        var reply = await new OnboardRemapService(kb).ApplyAsync(new Dictionary<ushort, string>(), [229, 224, 199], default);
        Assert.Equal(["reset 229"], kb.Writes);
        Assert.Equal(PlayPause, kb.State[229]);
        Assert.Equal(OnboardOutcome.Applied, reply.Results.Single(r => r.Cid == 229).Outcome);
        Assert.Equal(OnboardOutcome.Unchanged, reply.Results.Single(r => r.Cid == 224).Outcome);
        Assert.Equal(OnboardOutcome.Unchanged, reply.Results.Single(r => r.Cid == 199).Outcome);
    }

    [Fact]
    public async Task Reset_all_writes_nothing_when_the_keyboard_is_already_factory()
    {
        var kb = new FakeOnboard();
        var reply = await new OnboardRemapService(kb).ApplyAsync(new Dictionary<ushort, string>(), OnboardRemapKeys.All.Select(k => k.Cid), default);
        Assert.Empty(kb.Writes);
        Assert.True(reply.Ok);
        // this fake keyboard does not list calculator (10), so that one is reported rather than silently dropped
        Assert.Equal(OnboardOutcome.Failed, reply.Results.Single(r => r.Cid == 10).Outcome);
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", "one modifier")]
    [InlineData("Ctrl+MediaNext", "media key")]
    [InlineData("Ctrl", "modifiers only")]
    [InlineData("Banana", "cannot encode")]
    public async Task Unencodable_chords_are_rejected_with_the_encoder_reason_and_not_written(string chord, string reasonPart)
    {
        var kb = new FakeOnboard();
        var reply = await new OnboardRemapService(kb).ApplyAsync(new Dictionary<ushort, string> { [224] = chord }, [], default);
        Assert.Empty(kb.Writes);
        var r = Assert.Single(reply.Results);
        Assert.Equal(OnboardOutcome.Failed, r.Outcome);
        Assert.False(OnboardChordEncoder.TryEncode(chord, out _, out var why));
        Assert.Equal(why, r.Reason);
        Assert.Contains(reasonPart, r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_while_disconnected_writes_nothing_and_says_so()
    {
        var kb = new FakeOnboard { Disconnected = true };
        var reply = await new OnboardRemapService(kb).ApplyAsync(new Dictionary<ushort, string> { [224] = "F5" }, [], default);
        Assert.False(reply.Ok);
        Assert.Contains("not connected", reply.Error);
        Assert.Empty(kb.Writes);
    }

    [Fact]
    public async Task A_failed_write_stops_the_rest_and_reports_per_key()
    {
        var kb = new FakeOnboard { FailOn = 224 };
        var reply = await new OnboardRemapService(kb).ApplyAsync(new Dictionary<ushort, string> { [199] = "F5", [224] = "F6", [229] = "F7" }, [], default);
        Assert.False(reply.AllSucceeded);
        Assert.Equal(["set 199 01003E00"], kb.Writes);
        Assert.Equal(OnboardOutcome.Applied, reply.Results.Single(r => r.Cid == 199).Outcome);
        Assert.Equal(OnboardOutcome.Failed, reply.Results.Single(r => r.Cid == 224).Outcome);
        Assert.Contains("not attempted", reply.Results.Single(r => r.Cid == 229).Reason);
    }

    [Fact]
    public async Task Empty_apply_writes_nothing_and_does_not_even_read()
    {
        var kb = new FakeOnboard();
        var reply = await new OnboardRemapService(kb).ApplyAsync(new Dictionary<ushort, string>(), [], default);
        Assert.True(reply.Ok);
        Assert.Equal(0, kb.Reads);
        Assert.Empty(kb.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("apply\tset:abc:F5")]
    [InlineData("apply\tset:0:F5")]
    [InlineData("apply\tzap:224")]
    public async Task Garbage_request_lines_are_refused_without_a_write(string? line)
    {
        var kb = new FakeOnboard();
        var reply = await new OnboardRemapService(kb).HandleLineAsync(line, default);
        Assert.StartsWith("err", reply);
        Assert.Empty(kb.Writes);
    }

    [Fact]
    public void Protocol_round_trips()
    {
        var read = new OnboardReadReply(null, 0x141, [new OnboardMapping(224, WinTab, 0), new OnboardMapping(229, PlayPause, 0)]);
        var back = OnboardProtocol.ParseRead(OnboardProtocol.FormatRead(read));
        Assert.Equal(read.Capabilities, back.Capabilities);
        Assert.Equal(read.Mappings, back.Mappings);

        var apply = new OnboardApplyReply(null, [new(1, OnboardOutcome.Applied), new(2, OnboardOutcome.Unchanged), new(3, OnboardOutcome.Failed, "bad\tthing")]);
        var b2 = OnboardProtocol.ParseApply(OnboardProtocol.FormatApply(apply));
        Assert.Equal([OnboardOutcome.Applied, OnboardOutcome.Unchanged, OnboardOutcome.Failed], b2.Results.Select(r => r.Outcome).ToArray());
        Assert.Equal("bad thing", b2.Results[2].Reason);

        Assert.True(OnboardProtocol.TryParseApply(OnboardProtocol.ApplyLine([new OnboardRemap(224, "Ctrl+Esc")], [229]), out var sets, out var resets, out _));
        Assert.Equal("Ctrl+Esc", sets[224]);
        Assert.Equal([(ushort)229], resets);
    }

    [Fact]
    public async Task Client_and_service_work_together_through_a_fake_pipe()
    {
        var kb = new FakeOnboard();
        var service = new OnboardRemapService(kb);
        var client = new OnboardClient(async (line, ct) => await service.HandleLineAsync(line, ct));
        var read = await client.ReadAsync();
        Assert.Equal(WinTab, read.Mappings.Single(m => m.Cid == 224).Action);
        var reply = await client.ApplyAsync([new OnboardRemap(224, "Ctrl+Esc")], []);
        Assert.True(reply.AllSucceeded);
        Assert.Equal(OnboardAction.Keystroke(0x29, OnboardModifiers.Ctrl), (await client.ReadAsync()).Mappings.Single(m => m.Cid == 224).Action);
    }

    [Fact]
    public async Task Client_reports_an_unreachable_tray()
    {
        var client = new OnboardClient((_, _) => Task.FromResult<string?>(null));
        Assert.Equal(OnboardProtocol.Unreachable, (await client.ReadAsync()).Error);
        Assert.Equal(OnboardProtocol.Unreachable, (await client.ApplyAsync([], [])).Error);
    }

    [Fact]
    public async Task Real_pipe_round_trip_read_then_apply()
    {
        var kb = new FakeOnboard();
        string name = "Slender.Onboard.Test." + Guid.NewGuid().ToString("N");
        using var server = new OnboardServer(new OnboardRemapService(kb), pipeName: name);
        server.Start();
        var client = OnboardClient.ForPipe(name);
        var read = await client.ReadAsync();
        Assert.True(read.Ok);
        Assert.Empty(kb.Writes);
        var reply = await client.ApplyAsync([new OnboardRemap(229, "F5")], []);
        Assert.True(reply.AllSucceeded);
        Assert.Equal(["set 229 01003E00"], kb.Writes);
    }

    [Fact]
    public void Config_sanitizer_keeps_only_storable_known_keys()
    {
        var clean = OnboardRemapKeys.Sanitize([new(224, "Win+Tab"), new(999, "A"), new(225, "Ctrl+Shift+A"), new(229, " F5 ")]);
        Assert.Equal([new OnboardRemap(224, "Win+Tab"), new OnboardRemap(229, "F5")], clean);
    }

    [Fact]
    public void There_are_17_keys_and_every_choice_is_storable()
    {
        Assert.Equal(17, OnboardRemapKeys.All.Count);
        Assert.Equal(17, OnboardRemapKeys.All.Select(k => k.Cid).Distinct().Count());
        foreach (var k in OnboardChoices.MainKeys)
            Assert.True(OnboardChordEncoder.TryEncode(k.Name, out _, out var err), $"{k.Name}: {err}");
        foreach (var m in OnboardChoices.Modifiers.Where(m => m.Name != ""))
            Assert.True(OnboardChoices.TryBuildChord(m.Name, "Esc", out _, out _));
    }

    [Fact]
    public void Picker_rejects_a_modifier_on_a_media_key_with_the_encoder_reason()
    {
        Assert.False(OnboardChoices.TryBuildChord("Ctrl", "MediaNext", out _, out var err));
        Assert.Contains("media", err);
        Assert.False(OnboardChoices.TryBuildChord("", null, out _, out _));
        Assert.True(OnboardChoices.TryBuildChord("Win", "E", out var chord, out _));
        Assert.Equal("Win+E", chord);
        Assert.Equal(("Win", "E"), OnboardChoices.Split("Win+E"));
        Assert.Equal(("", "F5"), OnboardChoices.Split("F5"));
    }

    [Fact]
    public void Describe_reads_back_plain_text()
    {
        Assert.Equal("Win+Tab", OnboardChoices.Describe(WinTab));
        Assert.Equal("Play / pause", OnboardChoices.Describe(PlayPause));
        Assert.Contains("Not set", OnboardChoices.Describe(default));
        Assert.Equal("Ctrl+Esc", OnboardChoices.Describe(OnboardAction.Keystroke(0x29, OnboardModifiers.Ctrl)));
    }
}
