using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

public class HidppChannelTests
{
    private static HidppChannelOptions Fast(int retries = 3) => new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(60),
        MaxRetries = retries,
        RetryDelayBase = TimeSpan.FromMilliseconds(1),
        ReadPollMs = 20,
    };

    [Fact]
    public async Task Response_is_correlated_amid_interleaved_notifications()
    {
        var t = new FakeTransport();
        t.OnWrite = w => new[]
        {
            FakeDevice.Frame(0xFF, 0x05, 0, 0, 0x00, 0xC3, 0x00, 0x00),            // notification (swId 0) before the response
            FakeDevice.Frame(0xFF, 0x09, 1, 0x0B, 0xEE),                           // response for a different feature: not ours
            FakeDevice.Frame(0xFF, w[2], (byte)(w[3] >> 4), (byte)(w[3] & 0xF), 0x42), // the real response
        };
        using var ch = new HidppChannel(t, Fast());
        var notes = new List<HidppMessage>();
        ch.Notification += (_, m) => { lock (notes) notes.Add(m); };
        ch.Start();

        var resp = await ch.RequestAsync(0xFF, 0x07, 2, new byte[] { 1 }, CancellationToken.None);

        Assert.Equal(0x42, resp.Params[0]);
        await Wait.UntilAsync(() => { lock (notes) return notes.Count == 2; });
        lock (notes)
        {
            Assert.Equal(0x05, notes[0].FeatureIndex);
            Assert.Equal(0x09, notes[1].FeatureIndex);
        }
    }

    [Fact]
    public async Task Works_when_transport_omits_report_id()
    {
        var t = new FakeTransport { IncludeReportId = false };
        t.OnWrite = w => [FakeDevice.Frame(w[1], w[2], (byte)(w[3] >> 4), (byte)(w[3] & 0xF), 7)];
        using var ch = new HidppChannel(t, Fast());
        ch.Start();
        var resp = await ch.RequestAsync(0xFF, 3, 1, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        Assert.Equal(7, resp.Params[0]);
    }

    [Fact]
    public async Task Timeout_retries_then_throws()
    {
        var t = new FakeTransport { OnWrite = _ => [] };
        using var ch = new HidppChannel(t, Fast(retries: 3));
        ch.Start();
        await Assert.ThrowsAsync<HidppTimeoutException>(() =>
            ch.RequestAsync(0xFF, 3, 1, ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        Assert.Equal(4, t.Writes.Count); // 1 attempt + 3 retries
    }

    [Fact]
    public async Task Timeout_then_success_on_retry()
    {
        var t = new FakeTransport();
        int n = 0;
        t.OnWrite = w => Interlocked.Increment(ref n) < 3
            ? []
            : [FakeDevice.Frame(w[1], w[2], (byte)(w[3] >> 4), (byte)(w[3] & 0xF), 9)];
        using var ch = new HidppChannel(t, Fast());
        ch.Start();
        var r = await ch.RequestAsync(0xFF, 3, 1, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        Assert.Equal(9, r.Params[0]);
        Assert.Equal(3, t.Writes.Count);
    }

    [Fact]
    public async Task Busy_error_is_retried_but_other_errors_are_not()
    {
        var t = new FakeTransport();
        int n = 0;
        t.OnWrite = w =>
        {
            int i = Interlocked.Increment(ref n);
            byte code = i == 1 ? (byte)0x08 : (byte)0x02;
            return [ErrorFrame(w, code)];
        };
        using var ch = new HidppChannel(t, Fast());
        ch.Start();
        var ex = await Assert.ThrowsAsync<HidppErrorException>(() =>
            ch.RequestAsync(0xFF, 3, 1, ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        Assert.Equal(0x02, ex.Code);
        Assert.Equal(2, t.Writes.Count);
    }

    [Fact]
    public async Task Request_from_notification_handler_is_rejected()
    {
        var t = new FakeTransport();
        using var ch = new HidppChannel(t, Fast());
        var seen = new TaskCompletionSource<Exception?>();
        ch.Notification += (_, _) =>
        {
            try { _ = ch.RequestAsync(0xFF, 3, 1, ReadOnlyMemory<byte>.Empty, CancellationToken.None); seen.TrySetResult(null); }
            catch (InvalidOperationException e) { seen.TrySetResult(e); }
        };
        ch.Start();
        t.Inject(FakeDevice.Frame(0xFF, 5, 0, 0, 1));
        Assert.NotNull(await seen.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Transport_failure_completes_Faulted_and_fails_request()
    {
        var t = new FakeTransport { OnWrite = _ => [] };
        using var ch = new HidppChannel(t, Fast());
        ch.Start();
        var req = ch.RequestAsync(0xFF, 3, 1, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        t.Fault();
        await ch.Faulted.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<HidppException>(() => req);
    }

    [Fact]
    public async Task Feature_index_is_cached()
    {
        var dev = new FakeDevice();
        var t = dev.NewTransport();
        using var ch = new HidppChannel(t, Fast());
        ch.Start();
        var d = new HidppDevice(ch, 0xFF);

        Assert.Equal(FakeDevice.Haptics, await d.GetFeatureIndexAsync(FeatureId.Haptics, CancellationToken.None));
        Assert.Equal(FakeDevice.Haptics, await d.GetFeatureIndexAsync(FeatureId.Haptics, CancellationToken.None));
        Assert.Null(await d.GetFeatureIndexAsync(FeatureId.AdjustableDpi, CancellationToken.None));
        Assert.Null(await d.GetFeatureIndexAsync(FeatureId.AdjustableDpi, CancellationToken.None));

        Assert.Equal(2, dev.CountCalls(0, 0)); // one lookup per distinct feature
        Assert.True(d.TryGetCachedIndex(FeatureId.Haptics, out var idx));
        Assert.Equal(FakeDevice.Haptics, idx);
    }

    [Fact]
    public async Task Missing_feature_throws_on_call()
    {
        var dev = new FakeDevice();
        using var ch = new HidppChannel(dev.NewTransport(), Fast());
        ch.Start();
        var d = new HidppDevice(ch, 0xFF);
        await Assert.ThrowsAsync<HidppFeatureNotSupportedException>(() =>
            d.CallAsync(FeatureId.AdjustableDpi, 2, new byte[] { 0 }, CancellationToken.None));
    }

    private static byte[] ErrorFrame(byte[] req, byte code)
    {
        var f = FakeDevice.Frame(req[1], 0xFF, 0, 0, req[3], code);
        f[3] = req[2];
        return f;
    }
}
