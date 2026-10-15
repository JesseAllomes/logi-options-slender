using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class OnboardRemapTests
{
    private const byte PIdx = 11;

    // ---- encoding table (Solaar layout: action, param BE, modifier mask) ----

    [Theory]
    [InlineData("Ctrl+S", "01 00 16 01")]
    [InlineData("Win+E", "01 00 08 08")]
    [InlineData("Ctrl+Escape", "01 00 29 01")]
    [InlineData("Win+D", "01 00 07 08")]
    [InlineData("Win+Tab", "01 00 2B 08")]
    [InlineData("alt+left", "01 00 50 04")]
    [InlineData("F5", "01 00 3E 00")]
    [InlineData("F13", "01 00 68 00")]
    [InlineData("F24", "01 00 73 00")]
    [InlineData("PrintScreen", "01 00 46 00")]
    [InlineData("Ctrl+0", "01 00 27 01")]
    [InlineData("Ctrl+1", "01 00 1E 01")]
    [InlineData("Ctrl+Z", "01 00 1D 01")]
    [InlineData("Ctrl+PageDown", "01 00 4E 01")]
    [InlineData("Menu", "01 00 65 00")]
    [InlineData("MediaPlayPause", "07 00 CD 00")]
    [InlineData("MediaNext", "07 00 B5 00")]
    [InlineData("MediaPrevious", "07 00 B6 00")]
    [InlineData("VolumeMute", "07 00 E2 00")]
    [InlineData("VolumeUp", "07 00 E9 00")]
    [InlineData("VolumeDown", "07 00 EA 00")]
    [InlineData("Calculator", "07 01 92 00")]
    public void Chords_encode_to_the_solaar_layout(string chord, string hex)
    {
        Assert.True(OnboardChordEncoder.TryEncode(chord, out var a, out var err), err);
        Assert.Equal(Convert.FromHexString(hex.Replace(" ", "")), a.ToBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]        // modifiers only
    [InlineData("Ctrl+Shift+S")]      // two modifiers: the K860 drops them all (tested on device)
    [InlineData("Win+Shift+S")]
    [InlineData("Ctrl+A+B")]          // two main keys
    [InlineData("Ctrl+Banana")]       // unknown name
    [InlineData("Ctrl+VolumeUp")]     // modifier with a consumer key
    [InlineData("MediaNext+A")]
    [InlineData("Ctrl++A")]
    [InlineData("Ctrl+")]
    public void Unencodable_chords_are_rejected(string chord)
    {
        Assert.False(OnboardChordEncoder.TryEncode(chord, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Throws<ArgumentException>(() => OnboardChordEncoder.Encode(chord));
    }

    [Fact]
    public void Action_round_trips_through_bytes()
    {
        var a = OnboardChordEncoder.Encode("Win+E");
        Assert.Equal(a, OnboardAction.FromBytes(a.ToBytes()));
    }

    [Fact]
    public void Mapping_reply_parses_as_on_device()
    {
        // 2026-10-15 K860 reply for CID 0xE0: 00E0 00 01 002B 08 00
        var p = Convert.FromHexString("00E00001002B08000000000000000000");
        var m = LogiKeyboard.ParseMapping(0xE0, p);
        Assert.Equal(new OnboardMapping(0xE0, OnboardAction.Keystroke(0x2B, OnboardModifiers.Meta), 0), m);
    }

    // ---- allowlist ----

    [Fact]
    public void Allowlist_covers_exactly_0x1C00_fn0_to_fn5()
    {
        for (byte fn = 0; fn <= 5; fn++) Assert.True(HidppAllowlist.IsAllowed(0x1C00, fn), $"fn{fn}");
        for (byte fn = 6; fn < 16; fn++) Assert.False(HidppAllowlist.IsAllowed(0x1C00, fn), $"fn{fn}");
        // other persistent-memory features stay blocked
        foreach (ushort never in new ushort[] { 0x8100, 0x8101, 0x1814, 0x1815, 0x00D0, 0x00D1, 0x1C01, 0x1BFF })
            for (byte fn = 0; fn < 16; fn++) Assert.False(HidppAllowlist.IsAllowed(never, fn), $"{never:X4} fn{fn}");
    }

    // ---- LogiKeyboard over a fake keyboard ----

    private sealed class Rig : IDisposable
    {
        public FakeDevice Dev = new();
        public FakeTransport T;
        public LogiKeyboard Kb;
        public Dictionary<ushort, byte[]> Maps = new()
        {
            [0xE0] = [0x01, 0x00, 0x2B, 0x08],
            [0xE5] = [0x07, 0x00, 0xCD, 0x00],
            [0xC7] = [0x00, 0x00, 0x00, 0x00],
        };
        private readonly ushort[] _order = [0xE0, 0xE5, 0xC7];

        public Rig()
        {
            Dev.Features.Clear();
            Dev.Features[FeatureId.ReprogControlsV4] = FakeDevice.Reprog;
            Dev.Features[FeatureId.Battery] = FakeDevice.Battery;
            Dev.Features[PersistentRemapId.Feature] = PIdx;
            Dev.AcceptedIndices.Clear();
            Dev.AcceptedIndices.Add(0x01);
            Dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
            Dev.Handlers[(FakeDevice.Battery, 0)] = _ => [87, 80, 0];
            Dev.Handlers[(PIdx, 0)] = _ => [0x01, 0x41];
            Dev.Handlers[(PIdx, 1)] = _ => [(byte)_order.Length];
            Dev.Handlers[(PIdx, 2)] = p => [(byte)(_order[p[0]] >> 8), (byte)_order[p[0]]];
            Dev.Handlers[(PIdx, 3)] = p =>
            {
                var cid = (ushort)((p[0] << 8) | p[1]);
                return [p[0], p[1], 0, .. Maps[cid], 0];
            };
            Dev.Handlers[(PIdx, 4)] = p => { Maps[(ushort)((p[0] << 8) | p[1])] = p[3..7]; return []; };
            Dev.Handlers[(PIdx, 5)] = p => []; // reset: acknowledged
            T = Dev.NewTransport();
            var f = new FakeFactory();
            f.Enqueue(T);
            Kb = new LogiKeyboard(f, new LogiKeyboardOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(150), MaxRetries = 1, RetryDelayBase = TimeSpan.FromMilliseconds(1),
                ReadPollMs = 20, ProbeTimeout = TimeSpan.FromMilliseconds(60), BackoffInitial = TimeSpan.FromMilliseconds(20),
                BackoffMax = TimeSpan.FromMilliseconds(80), HeartbeatInterval = TimeSpan.FromMilliseconds(500),
                DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
            });
        }

        public async Task ConnectAsync()
        {
            await Kb.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => Kb.IsConnected);
        }

        public List<byte[]> PWrites(int fn) => [.. T.Writes.Where(w => w[2] == PIdx && w[3] >> 4 == fn)];
        public void Dispose() => Kb.Dispose();
    }

    [Fact]
    public async Task Reads_capabilities_and_every_mapping_without_any_write()
    {
        using var r = new Rig();
        await r.ConnectAsync();
        Assert.Equal((ushort)0x0141, await r.Kb.GetOnboardCapabilitiesAsync(default));
        var maps = await r.Kb.ReadOnboardMappingsAsync(default);
        Assert.Equal(3, maps.Count);
        Assert.Equal(new OnboardMapping(0xE0, OnboardAction.Keystroke(0x2B, OnboardModifiers.Meta), 0), maps[0]);
        Assert.Equal(OnboardAction.Consumer(0xCD), maps[1].Action);
        Assert.Equal(OnboardActionType.Empty, maps[2].Action.Type);
        Assert.Empty(r.PWrites(4));
        Assert.Empty(r.PWrites(5));
        Assert.All(r.PWrites(2).Concat(r.PWrites(3)), w => Assert.Equal(0xFF, w[w[3] >> 4 == 2 ? 5 : 6]));
    }

    [Fact]
    public async Task Set_chord_sends_fn4_with_cid_host_and_action_bytes()
    {
        using var r = new Rig();
        await r.ConnectAsync();
        await r.Kb.SetOnboardChordAsync(0xE0, "Win+E", default);
        var w = Assert.Single(r.PWrites(4));
        Assert.Equal(Convert.FromHexString("00E0FF01000808"), w[4..11]);
        Assert.Equal(OnboardAction.Keystroke(0x08, OnboardModifiers.Meta), (await r.Kb.ReadOnboardMappingsAsync(default))[0].Action);
    }

    [Fact]
    public async Task Reset_sends_fn5_with_cid_and_host()
    {
        using var r = new Rig();
        await r.ConnectAsync();
        await r.Kb.ResetOnboardKeyAsync(0xE5, default);
        var w = Assert.Single(r.PWrites(5));
        Assert.Equal(Convert.FromHexString("00E5FF"), w[4..7]);
    }

    [Fact]
    public async Task Bad_input_writes_nothing()
    {
        using var r = new Rig();
        await r.ConnectAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => r.Kb.SetOnboardChordAsync(0xE0, "Ctrl+Shift", default));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Kb.SetOnboardActionAsync(0xE0, new OnboardAction(OnboardActionType.Mouse, 1, 0), default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => r.Kb.ResetOnboardKeyAsync(0, default));
        Assert.Empty(r.PWrites(4));
        Assert.Empty(r.PWrites(5));
    }

    [Fact]
    public async Task Not_connected_throws_and_writes_nothing()
    {
        using var r = new Rig();
        await Assert.ThrowsAsync<HidppException>(() => r.Kb.SetOnboardChordAsync(0xE0, "Ctrl+S", default));
        Assert.Empty(r.T.Writes);
    }

    [Fact]
    public async Task Missing_feature_reads_as_unsupported()
    {
        using var r = new Rig();
        r.Dev.Features.Remove(PersistentRemapId.Feature);
        await r.ConnectAsync();
        Assert.Null(await r.Kb.GetOnboardCapabilitiesAsync(default));
        Assert.Empty(await r.Kb.ReadOnboardMappingsAsync(default));
    }
}
