using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class AllowlistTests
{
    private static (HidppDevice Dev, FakeTransport T, HidppChannel Ch) Rig(Action<FakeDevice>? tweak = null)
    {
        var dev = new FakeDevice();
        dev.Features[0x00D0] = 20;
        dev.Features[0x1814] = 21;
        dev.Features[0x8100] = 22;
        dev.Features[0x19C0] = 23;
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => new byte[16];
        tweak?.Invoke(dev);
        var t = dev.NewTransport();
        var ch = new HidppChannel(t, new HidppChannelOptions { RequestTimeout = TimeSpan.FromMilliseconds(100), MaxRetries = 0, ReadPollMs = 20 });
        ch.Start();
        return (new HidppDevice(ch, 0xFF), t, ch);
    }

    [Fact]
    public async Task Allowed_call_passes()
    {
        var (d, t, ch) = Rig();
        using (ch)
        {
            await d.CallAsync(FeatureId.ReprogControlsV4, 3, new byte[] { 0, 0xC3, 3, 0, 0 }, CancellationToken.None);
            Assert.Contains(t.Writes, w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3);
        }
    }

    [Theory]
    [InlineData((ushort)0x00D0, (byte)0)]
    [InlineData((ushort)0x00D1, (byte)0)]
    [InlineData((ushort)0x1814, (byte)1)]   // switch host: drops the link
    [InlineData((ushort)0x1815, (byte)2)]   // not one of the read getters
    [InlineData((ushort)0x1815, (byte)4)]
    [InlineData((ushort)0x8100, (byte)2)]
    [InlineData((ushort)0x1B04, (byte)4)]   // function beyond the allowed range
    [InlineData((ushort)0x19C0, (byte)4)]   // force sensing: only fn0-3 (fn3 = set force)
    [InlineData((ushort)0x1000, (byte)1)]
    [InlineData((ushort)0x2201, (byte)4)]
    [InlineData((ushort)0x1234, (byte)0)]   // unknown feature
    public async Task Blocked_call_throws_and_writes_nothing(ushort feature, byte fn)
    {
        var (d, t, ch) = Rig();
        using (ch)
        {
            await Assert.ThrowsAsync<HidppBlockedRequestException>(() => d.CallAsync(feature, fn, ReadOnlyMemory<byte>.Empty, CancellationToken.None));
            await Assert.ThrowsAsync<HidppBlockedRequestException>(() => d.SendAsync(feature, fn, ReadOnlyMemory<byte>.Empty, CancellationToken.None));
            Assert.Empty(t.Writes); // not even an IRoot index lookup
        }
    }

    [Fact]
    public void Allowlist_matches_the_specified_table()
    {
        var spec = new (ushort F, byte Max)[]
        {
            (0x0000, 1), (0x0001, 1), (0x0005, 1), (0x1000, 0), (0x1004, 1), (0x19B0, 4), (0x19C0, 3),
            (0x1B04, 3), (0x2110, 1), (0x2111, 2), (0x2121, 2), (0x2150, 2), (0x2201, 3),
        };
        foreach (var (f, max) in spec)
        {
            for (byte fn = 0; fn <= max; fn++) Assert.True(HidppAllowlist.IsAllowed(f, fn), $"{f:X4} fn{fn}");
            Assert.False(HidppAllowlist.IsAllowed(f, (byte)(max + 1)), $"{f:X4} fn{max + 1}");
        }
        foreach (ushort never in new ushort[] { 0x00D0, 0x00D1, 0x8100 })
            for (byte fn = 0; fn < 16; fn++) Assert.False(HidppAllowlist.IsAllowed(never, fn));
    }

    [Fact]
    public void Host_features_allow_only_their_read_functions()
    {
        // 0x1814 ChangeHost: fn0 getHostInfo only; fn1 switches host.
        Assert.True(HidppAllowlist.IsAllowed(0x1814, 0));
        for (byte fn = 1; fn < 16; fn++) Assert.False(HidppAllowlist.IsAllowed(0x1814, fn), $"0x1814 fn{fn}");
        // 0x1815 HostsInfo: fn0 capabilities/count, fn1 getHostInfo, fn3 getHostFriendlyName. fn2 and the rest stay refused.
        foreach (byte fn in new byte[] { 0, 1, 3 }) Assert.True(HidppAllowlist.IsAllowed(0x1815, fn), $"0x1815 fn{fn}");
        foreach (byte fn in new byte[] { 2, 4, 5, 6, 7, 8, 15 }) Assert.False(HidppAllowlist.IsAllowed(0x1815, fn), $"0x1815 fn{fn}");
        Assert.Throws<HidppBlockedRequestException>(() => HidppAllowlist.EnsureAllowed(0x1814, 1));
        Assert.Throws<HidppBlockedRequestException>(() => HidppAllowlist.EnsureAllowed(0x1815, 2, new byte[] { 0 }));
    }

    [Fact]
    public async Task Host_reads_pass_and_host_writes_write_nothing()
    {
        var (d, t, ch) = Rig(dev =>
        {
            dev.Features[0x1815] = 24;
            dev.Handlers[(21, 0)] = _ => [3, 1];
            dev.Handlers[(24, 0)] = _ => [0, 0, 3, 1];
        });
        using (ch)
        {
            await d.CallAsync(0x1814, 0, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
            await d.CallAsync(0x1815, 0, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
            int before = t.Writes.Count;
            await Assert.ThrowsAsync<HidppBlockedRequestException>(() => d.CallAsync(0x1814, 1, new byte[] { 1 }, CancellationToken.None));
            await Assert.ThrowsAsync<HidppBlockedRequestException>(() => d.SendAsync(0x1815, 2, new byte[] { 0, 1 }, CancellationToken.None));
            Assert.Equal(before, t.Writes.Count);
        }
    }

    [Fact]
    public void Thumb_wheel_divert_and_invert_share_fn2_and_nothing_beyond_it_is_allowed()
    {
        Assert.True(HidppAllowlist.IsAllowed(0x2150, 0)); // getInfo
        Assert.True(HidppAllowlist.IsAllowed(0x2150, 1)); // getConfig
        Assert.True(HidppAllowlist.IsAllowed(0x2150, 2)); // setConfig [divert, invert]
        Assert.False(HidppAllowlist.IsAllowed(0x2150, 3));
    }
}
