using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class RawXyAllowlistTests
{
    private static byte[] Req(ushort cid, byte flags, byte r1 = 0, byte r2 = 0) => [(byte)(cid >> 8), (byte)cid, flags, r1, r2];

    [Theory]
    [InlineData((ushort)0x00C3, (byte)0x33)] // divert + raw XY
    [InlineData((ushort)0x00C3, (byte)0x22)] // undivert + raw XY off
    [InlineData((ushort)0x00D7, (byte)0x33)]
    [InlineData((ushort)0x00C3, (byte)0x03)]
    [InlineData((ushort)0x0053, (byte)0x03)]
    [InlineData((ushort)0x0053, (byte)0x02)]
    public void Divert_and_raw_xy_flags_are_allowed_where_intended(ushort cid, byte flags) =>
        Assert.True(HidppAllowlist.IsAllowed(0x1B04, 3, Req(cid, flags)));

    [Theory]
    [InlineData((ushort)0x00C3, (byte)0x07)] // persist
    [InlineData((ushort)0x00C3, (byte)0x0F)] // persist + valid
    [InlineData((ushort)0x00C3, (byte)0x37)] // raw XY + persist
    [InlineData((ushort)0x00C3, (byte)0x73)] // force raw XY
    [InlineData((ushort)0x00C3, (byte)0xC3)] // force raw XY valid
    [InlineData((ushort)0x00C3, (byte)0x04)]
    public void Persist_and_force_raw_xy_stay_blocked_even_for_the_gesture_button(ushort cid, byte flags) =>
        Assert.False(HidppAllowlist.IsAllowed(0x1B04, 3, Req(cid, flags)));

    [Theory]
    [InlineData((ushort)0x0052)]
    [InlineData((ushort)0x0053)]
    [InlineData((ushort)0x0056)]
    [InlineData((ushort)0x00C4)]
    [InlineData((ushort)0x01A0)]
    public void Raw_xy_is_refused_for_other_controls(ushort cid)
    {
        Assert.False(HidppAllowlist.IsAllowed(0x1B04, 3, Req(cid, 0x33)));
        Assert.False(HidppAllowlist.IsAllowed(0x1B04, 3, Req(cid, 0x22)));
        Assert.False(HidppAllowlist.IsAllowed(0x1B04, 3, Req(cid, 0x10)));
    }

    [Fact]
    public void Remap_target_and_short_payloads_are_refused()
    {
        Assert.False(HidppAllowlist.IsAllowed(0x1B04, 3, Req(0x00C3, 0x03, 0, 0x50)));
        Assert.False(HidppAllowlist.IsAllowed(0x1B04, 3, new byte[] { 0x00, 0xC3 }));
    }

    [Fact]
    public void Reads_on_the_feature_are_unaffected() => Assert.True(HidppAllowlist.IsAllowed(0x1B04, 2, new byte[] { 0x00, 0xC3 }));

    [Fact]
    public async Task Device_refuses_a_persist_write_before_writing()
    {
        var dev = new FakeDevice();
        var t = dev.NewTransport();
        var ch = new HidppChannel(t, new HidppChannelOptions { RequestTimeout = TimeSpan.FromMilliseconds(100), MaxRetries = 0, ReadPollMs = 20 });
        ch.Start();
        using (ch)
        {
            var d = new HidppDevice(ch, 0xFF);
            await Assert.ThrowsAsync<HidppBlockedRequestException>(() => d.CallAsync(FeatureId.ReprogControlsV4, 3, Req(0x00C3, 0x37), CancellationToken.None));
            await Assert.ThrowsAsync<HidppBlockedRequestException>(() => d.CallAsync(FeatureId.ReprogControlsV4, 3, Req(0x0053, 0x33), CancellationToken.None));
            Assert.Empty(t.Writes);
        }
    }
}

public class RawXyMouseTests
{
    private static (LogiMouse mouse, FakeTransport transport) Create(Func<byte[], byte[]?>? reprogSet = null)
    {
        var dev = new FakeDevice();
        dev.Handlers[(FakeDevice.Reprog, 3)] = reprogSet ?? (_ => []);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        return (new LogiMouse(f, Wait.FastOptions()), t);
    }

    private static bool Wrote(FakeTransport t, ushort cid, byte flags) => t.Writes.Any(w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3
        && w[4] == (byte)(cid >> 8) && w[5] == (byte)cid && w[6] == flags);

    private static async Task<LogiMouse> Started(LogiMouse mouse)
    {
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        return mouse;
    }

    [Fact]
    public void RawXy_event_decodes_signed_big_endian_deltas()
    {
        Assert.True(HidppMessage.TryParse(FakeDevice.Frame(0xFF, FakeDevice.Reprog, 1, 0, 0xFF, 0xF6, 0x00, 0x2A), out var msg));
        Assert.Equal(new RawXyEvent(-10, 42), LogiDeviceBase.ParseRawXy(msg));
        Assert.True(HidppMessage.TryParse(FakeDevice.Frame(0xFF, FakeDevice.Reprog, 1, 0, 0x80, 0x00, 0x7F, 0xFF), out var big));
        Assert.Equal(new RawXyEvent(-32768, 32767), LogiDeviceBase.ParseRawXy(big));
    }

    [Fact]
    public async Task Gesture_divert_with_raw_xy_writes_0x33_and_reports_raw_active()
    {
        var (mouse, t) = Create();
        using (await Started(mouse))
        {
            await mouse.SetDivertedControlsAsync([ControlId.Gesture, ControlId.ModeShift], [ControlId.Gesture], CancellationToken.None);
            Assert.True(Wrote(t, ControlId.Gesture, 0x33));
            Assert.True(Wrote(t, ControlId.ModeShift, 0x03));
            Assert.True(mouse.IsRawXyActive(ControlId.Gesture));
            Assert.False(mouse.IsRawXyActive(ControlId.ModeShift));
            Assert.DoesNotContain(t.Writes, w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3 && (w[6] & 0x0C) != 0); // never persist
        }
    }

    [Fact]
    public async Task Raw_xy_is_never_requested_for_other_controls()
    {
        var (mouse, t) = Create();
        using (await Started(mouse))
        {
            await mouse.SetDivertedControlsAsync([ControlId.Back], [ControlId.Back], CancellationToken.None);
            Assert.True(Wrote(t, ControlId.Back, 0x03));
            Assert.False(Wrote(t, ControlId.Back, 0x33));
            Assert.False(mouse.IsRawXyActive(ControlId.Back));
        }
    }

    [Fact]
    public async Task Refused_raw_xy_falls_back_to_a_plain_divert()
    {
        var (mouse, t) = Create(p => p[2] == 0x33 ? null : []);
        using (await Started(mouse))
        {
            await mouse.SetDivertedControlsAsync([ControlId.Gesture], [ControlId.Gesture], CancellationToken.None);
            Assert.True(Wrote(t, ControlId.Gesture, 0x33));
            Assert.True(Wrote(t, ControlId.Gesture, 0x03));
            Assert.False(mouse.IsRawXyActive(ControlId.Gesture));
        }
    }

    [Fact]
    public async Task Switching_between_plain_and_raw_xy_rewrites_the_divert()
    {
        var (mouse, t) = Create();
        using (await Started(mouse))
        {
            await mouse.SetDivertedControlsAsync([ControlId.Gesture], [], CancellationToken.None);
            Assert.False(mouse.IsRawXyActive(ControlId.Gesture));
            Assert.True(Wrote(t, ControlId.Gesture, 0x03));
            await mouse.SetDivertedControlsAsync([ControlId.Gesture], [ControlId.Gesture], CancellationToken.None);
            Assert.True(mouse.IsRawXyActive(ControlId.Gesture));
            await mouse.SetDivertedControlsAsync([ControlId.Gesture], [], CancellationToken.None);
            Assert.False(mouse.IsRawXyActive(ControlId.Gesture));
        }
    }

    [Fact]
    public async Task RawXy_notification_reaches_subscribers()
    {
        var (mouse, t) = Create();
        using (await Started(mouse))
        {
            var got = new List<RawXyEvent>();
            mouse.RawXyMoved += (_, e) => { lock (got) got.Add(e); };
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Reprog, 1, 0, 0x00, 0x0C, 0xFF, 0xF0));
            await Wait.UntilAsync(() => { lock (got) return got.Count == 1; });
            lock (got) Assert.Equal(new RawXyEvent(12, -16), got[0]);
        }
    }
}
