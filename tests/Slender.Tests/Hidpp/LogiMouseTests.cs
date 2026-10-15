using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class LogiMouseTests
{
    private static (LogiMouse mouse, FakeDevice dev, FakeTransport transport, FakeFactory factory) Create(Action<FakeDevice>? setup = null)
    {
        var dev = new FakeDevice();
        setup?.Invoke(dev);
        var t = dev.NewTransport();
        var f = new FakeFactory();
        f.Enqueue(t);
        return (new LogiMouse(f, Wait.FastOptions()), dev, t, f);
    }

    private static byte[] ButtonNotification(params ushort[] cids)
    {
        var p = new List<byte>();
        foreach (var c in cids) { p.Add((byte)(c >> 8)); p.Add((byte)c); }
        p.AddRange([0, 0]);
        return FakeDevice.Frame(0xFF, FakeDevice.Reprog, 0, 0, [.. p]);
    }

    [Fact]
    public async Task Device_index_falls_back_from_FF_to_01()
    {
        var (mouse, dev, _, _) = Create(d => { d.AcceptedIndices.Clear(); d.AcceptedIndices.Add(0x01); });
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);

            var calls = dev.Calls;
            Assert.Equal(0xFF, calls[0].Device);     // first ping went to 0xFF, unanswered
            Assert.Equal(0x01, calls[1].Device);     // then 0x01 answered
            Assert.All(calls.Skip(2), c => Assert.Equal(0x01, c.Device));
        }
    }

    [Fact]
    public async Task Connect_clears_native_wheel_target_via_read_modify_write()
    {
        var (mouse, dev, _, _) = Create(d =>
        {
            d.Handlers[(FakeDevice.HiRes, 1)] = _ => [0x07];
            d.Handlers[(FakeDevice.HiRes, 2)] = _ => [0x06];
        });
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            var set = Assert.Single(dev.Calls, c => c.Feature == FakeDevice.HiRes && c.Function == 2);
            Assert.Equal(0x06, set.Params[0]); // bit0 cleared, hi-res + invert kept
        }
    }

    [Fact]
    public async Task Divert_is_applied_and_reapplied_after_reconnect()
    {
        var dev = new FakeDevice();
        dev.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
        dev.Handlers[(FakeDevice.Thumb, 1)] = _ => [0, 0];
        dev.Handlers[(FakeDevice.Thumb, 2)] = _ => [];
        var t1 = dev.NewTransport();
        var t2 = dev.NewTransport();
        var factory = new FakeFactory();
        factory.Enqueue(t1);
        factory.Enqueue(t2);
        using var mouse = new LogiMouse(factory, Wait.FastOptions());
        var states = new List<bool>();
        mouse.ConnectionChanged += (_, c) => { lock (states) states.Add(c); };

        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        await mouse.SetDivertedControlsAsync([ControlId.Gesture, ControlId.Back], CancellationToken.None);
        await mouse.SetThumbWheelDivertedAsync(true, CancellationToken.None);

        bool Diverted(FakeTransport t, ushort cid) => t.Writes.Any(w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3
            && w[4] == (byte)(cid >> 8) && w[5] == (byte)cid && w[6] == 0x03);
        Assert.True(Diverted(t1, ControlId.Gesture));
        Assert.True(Diverted(t1, ControlId.Back));
        Assert.Contains(t1.Writes, w => w[2] == FakeDevice.Thumb && w[3] >> 4 == 2 && w[4] == 1 && w[5] == 0 && w[6] == 0);

        t1.Fault(); // device drops
        await Wait.UntilAsync(() => t2.Writes.Count > 0 && mouse.IsConnected && factory.OpenCalls >= 2);
        await Wait.UntilAsync(() => Diverted(t2, ControlId.Gesture) && Diverted(t2, ControlId.Back));
        Assert.Contains(t2.Writes, w => w[2] == FakeDevice.Thumb && w[3] >> 4 == 2 && w[4] == 1);
        lock (states) Assert.Equal(new[] { true, false, true }, states.Take(3));
    }

    [Fact]
    public async Task Replacing_diverted_set_undiverts_removed_controls()
    {
        var (mouse, dev, t, _) = Create(d => d.Handlers[(FakeDevice.Reprog, 3)] = _ => []);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.SetDivertedControlsAsync([ControlId.Gesture, ControlId.Back], CancellationToken.None);
            await mouse.SetDivertedControlsAsync([ControlId.Gesture], CancellationToken.None);
            var back = t.Writes.Where(w => w[2] == FakeDevice.Reprog && w[5] == (byte)ControlId.Back).Select(w => w[6]).ToArray();
            Assert.Equal(new byte[] { 0x02, 0x03, 0x02 }, back); // connect resets to native, then divert, then undivert
        }
    }

    [Fact]
    public async Task Connect_resets_leftover_diverts_and_thumb_wheel_keeping_invert()
    {
        // Options+ leaves controls and the thumb wheel diverted (seen on device 2026-10-15).
        var (mouse, _, t, _) = Create(d =>
        {
            d.Handlers[(FakeDevice.Reprog, 3)] = _ => [];
            d.Handlers[(FakeDevice.Thumb, 1)] = _ => [0x01, 0x01]; // diverted, inverted
            d.Handlers[(FakeDevice.Thumb, 2)] = _ => [];
        });
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await Wait.UntilAsync(() => t.Writes.Any(w => w[2] == FakeDevice.Thumb && w[3] >> 4 == 2));

            foreach (var cid in new[] { ControlId.Middle, ControlId.Back, ControlId.Forward, ControlId.Gesture, ControlId.ModeShift, ControlId.SensePanel })
                Assert.Contains(t.Writes, w => w[2] == FakeDevice.Reprog && w[3] >> 4 == 3
                    && w[4] == (byte)(cid >> 8) && w[5] == (byte)cid && w[6] == (cid == ControlId.Gesture ? 0x22 : 0x02)); // gesture undivert also clears raw XY
            Assert.Contains(t.Writes, w => w[2] == FakeDevice.Thumb && w[3] >> 4 == 2 && w[4] == 0 && w[5] == 1);
        }
    }

    [Fact]
    public async Task Dispose_undiverts_everything()
    {
        var (mouse, _, t, _) = Create(d => d.Handlers[(FakeDevice.Reprog, 3)] = _ => []);
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        await mouse.SetDivertedControlsAsync([ControlId.Gesture], CancellationToken.None);
        mouse.Dispose();
        Assert.Contains(t.Writes, w => w[2] == FakeDevice.Reprog && w[5] == (byte)ControlId.Gesture && w[6] == 0x22); // undivert also clears raw XY
        Assert.True(t.Disposed);
    }

    [Fact]
    public async Task Button_press_and_release_are_diffed()
    {
        var (mouse, _, t, _) = Create(d => d.Handlers[(FakeDevice.Reprog, 3)] = _ => []);
        using (mouse)
        {
            var events = new List<ButtonEvent>();
            mouse.ButtonChanged += (_, e) => { lock (events) events.Add(e); };
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.SetDivertedControlsAsync([ControlId.Gesture, ControlId.Back], CancellationToken.None);

            t.Inject(ButtonNotification(ControlId.Gesture));
            t.Inject(ButtonNotification(ControlId.Gesture, ControlId.Back));
            t.Inject(ButtonNotification(ControlId.Back));
            t.Inject(ButtonNotification());

            await Wait.UntilAsync(() => { lock (events) return events.Count == 4; });
            lock (events)
            {
                Assert.Equal(new ButtonEvent(ControlId.Gesture, true), events[0]);
                Assert.Equal(new ButtonEvent(ControlId.Back, true), events[1]);
                Assert.Equal(new ButtonEvent(ControlId.Gesture, false), events[2]);
                Assert.Equal(new ButtonEvent(ControlId.Back, false), events[3]);
            }
        }
    }

    [Fact]
    public void ParseDivertedControls_stops_at_zero_pair()
    {
        Assert.True(HidppMessage.TryParse(ButtonNotification(0x00C3, 0x0052), out var m));
        Assert.Equal(new ushort[] { 0x00C3, 0x0052 }.Order(), LogiMouse.ParseDivertedControls(m).Order());
    }

    [Fact]
    public void ParseThumbWheel_decodes_signed_rotation_and_flags()
    {
        var right = FakeDevice.Frame(0xFF, 7, 0, 0, 0x00, 0x05, 0, 0, 0, 0x01);
        var left = FakeDevice.Frame(0xFF, 7, 0, 0, 0xFF, 0xFB, 0, 0, 0, 0x06);
        Assert.True(HidppMessage.TryParse(right, out var r));
        Assert.True(HidppMessage.TryParse(left, out var l));
        Assert.Equal(new ThumbWheelEvent(5, true, false, false), LogiMouse.ParseThumbWheel(r));
        Assert.Equal(new ThumbWheelEvent(-5, false, true, true), LogiMouse.ParseThumbWheel(l));
    }

    [Fact]
    public async Task Thumb_wheel_notification_raises_event()
    {
        var (mouse, _, t, _) = Create(d => d.Handlers[(FakeDevice.Thumb, 2)] = _ => []);
        using (mouse)
        {
            var got = new TaskCompletionSource<ThumbWheelEvent>();
            mouse.ThumbWheelMoved += (_, e) => got.TrySetResult(e);
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.SetThumbWheelDivertedAsync(true, CancellationToken.None);
            t.Inject(FakeDevice.Frame(0xFF, FakeDevice.Thumb, 0, 0, 0xFF, 0xFE, 0, 0, 0, 0));
            Assert.Equal(-2, (await got.Task.WaitAsync(TimeSpan.FromSeconds(3))).Rotation);
        }
    }

    [Fact]
    public async Task Haptic_sends_fn4_with_waveform_fire_and_forget()
    {
        var (mouse, dev, t, _) = Create(); // no handler for 19B0: a waiting implementation would time out
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await mouse.PlayHapticAsync(3, CancellationToken.None);
            Assert.True(sw.ElapsedMilliseconds < 140);
            var call = Assert.Single(dev.Calls, c => c.Feature == FakeDevice.Haptics);
            Assert.Equal(LogiMouse.HapticPlayFunction, call.Function);
            Assert.Equal(4, call.Function);
            Assert.Equal(3, call.Params[0]);
            Assert.Contains(t.Writes, w => w[2] == FakeDevice.Haptics && w[3] == (byte)((4 << 4) | HidppChannel.DefaultSwId) && w[4] == 3);
        }
    }

    [Fact]
    public async Task Haptic_is_a_noop_when_feature_missing()
    {
        var (mouse, dev, _, _) = Create(d => d.Features.Remove(FeatureId.Haptics));
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.PlayHapticAsync(0, CancellationToken.None);
            Assert.DoesNotContain(dev.Calls, c => c.Feature == FakeDevice.Haptics);
        }
    }

    [Fact]
    public async Task Battery_prefers_unified_0x1004()
    {
        var (mouse, _, _, _) = Create(d => d.Handlers[(FakeDevice.Battery, 1)] = _ => [80, 0, 1]);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.Equal(new BatteryStatus(80, true), await mouse.GetBatteryAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Battery_falls_back_to_0x1000()
    {
        var (mouse, _, _, _) = Create(d =>
        {
            d.Features.Remove(FeatureId.UnifiedBattery);
            d.Features[FeatureId.Battery] = FakeDevice.Battery;
            d.Handlers[(FakeDevice.Battery, 0)] = _ => [55, 50, 0];
        });
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.Equal(new BatteryStatus(55, false), await mouse.GetBatteryAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Three_timeouts_mean_asleep_and_any_answer_wakes_and_reapplies_diverts()
    {
        var (mouse, dev, t, _) = Create(d => d.Handlers[(FakeDevice.Reprog, 3)] = _ => []);
        using (mouse)
        {
            await mouse.StartAsync(CancellationToken.None);
            await Wait.UntilAsync(() => mouse.IsConnected);
            await mouse.SetDivertedControlsAsync([ControlId.Gesture], CancellationToken.None);
            int divertsBefore = t.Writes.Count(w => w[2] == FakeDevice.Reprog && w[6] == 0x03);

            dev.Silent = true;
            await Wait.UntilAsync(() => !mouse.IsConnected);
            dev.Silent = false;
            await Wait.UntilAsync(() => mouse.IsConnected);
            Assert.True(t.Writes.Count(w => w[2] == FakeDevice.Reprog && w[6] == 0x03) > divertsBefore);
        }
    }

    [Fact]
    public async Task Reconnect_backs_off_until_a_transport_appears()
    {
        var dev = new FakeDevice();
        var factory = new FakeFactory();
        using var mouse = new LogiMouse(factory, Wait.FastOptions());
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => factory.OpenCalls >= 3);
        Assert.False(mouse.IsConnected);
        factory.Enqueue(dev.NewTransport());
        await Wait.UntilAsync(() => mouse.IsConnected);
    }

    [Fact]
    public async Task Thumb_divert_uses_fn2_and_keeps_the_invert_bit_in_both_modes()
    {
        var f = new FakeFactory();
        var dev = new FakeDevice();
        dev.Handlers[(FakeDevice.Thumb, 1)] = _ => [0, 0];
        dev.Handlers[(FakeDevice.Thumb, 2)] = _ => [];
        dev.Handlers[(FakeDevice.Thumb, 0)] = _ => [0, 20, 0, 120, 0, 0x03, 0x03, 0xE8];
        var t = dev.NewTransport();
        f.Enqueue(t);
        using var mouse = new LogiMouse(f, Wait.FastOptions());
        await mouse.StartAsync(CancellationToken.None);
        await Wait.UntilAsync(() => mouse.IsConnected);
        await Wait.UntilAsync(() => mouse.ThumbWheelTouchSupported == true); // caps 0x03: timestamp + touch
        await mouse.SetThumbWheelInvertAsync(true, CancellationToken.None);
        Assert.True(await mouse.TrySetThumbWheelDivertedAsync(true, CancellationToken.None));
        Assert.True(await mouse.TrySetThumbWheelDivertedAsync(false, CancellationToken.None));
        var sets = t.Writes.Where(w => w[2] == FakeDevice.Thumb && w[3] >> 4 == 2).Select(w => (w[4], w[5])).ToList();
        Assert.Contains(((byte)1, (byte)1), sets); // diverted + inverted
        Assert.Equal(((byte)0, (byte)1), sets[^1]); // native again, still inverted
    }

    [Fact]
    public async Task Thumb_divert_reports_false_when_the_device_rejects_it_or_is_gone()
    {
        var (mouse, _, _, _) = Create(d => d.Handlers[(FakeDevice.Thumb, 1)] = _ => throw new InvalidOperationException());
        using (mouse)
        {
            Assert.False(await mouse.TrySetThumbWheelDivertedAsync(true, CancellationToken.None)); // not connected yet
        }
    }
}
