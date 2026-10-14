using System.Threading.Channels;
using Slender.Hidpp;

namespace Slender.Tests.Hidpp;

/// <summary>Scripted in-memory transport: every write is passed to <see cref="OnWrite"/>, whose returned reports are queued for reading.</summary>
internal sealed class FakeTransport : IHidppTransport
{
    private readonly Channel<byte[]> _in = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte[]> _writes = [];
    private volatile bool _disposed;

    public Func<byte[], IEnumerable<byte[]>>? OnWrite { get; set; }

    /// <summary>When false the leading report-ID byte is stripped from inbound reports.</summary>
    public bool IncludeReportId { get; set; } = true;

    public bool IsOpen => !_disposed;
    public bool Disposed => _disposed;

    public List<byte[]> Writes { get { lock (_writes) return [.. _writes]; } }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> report, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var copy = report.ToArray();
        lock (_writes) _writes.Add(copy);
        if (OnWrite != null)
            foreach (var r in OnWrite(copy)) Inject(r);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, int timeoutMs, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        byte[] item;
        try { item = await _in.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return 0; }
        var data = IncludeReportId ? item : item[1..];
        data.CopyTo(buffer);
        return data.Length;
    }

    public void Inject(byte[] report) => _in.Writer.TryWrite(report);

    /// <summary>Makes the next read throw, as a yanked device would.</summary>
    public void Fault() => _in.Writer.TryComplete(new IOException("device gone"));

    public void Dispose() => _disposed = true;
}

internal sealed class FakeFactory : IHidppTransportFactory
{
    private readonly Queue<FakeTransport> _queue = new();
    public int OpenCalls;

    public void Enqueue(FakeTransport t) => _queue.Enqueue(t);

    public IHidppTransport? TryOpen()
    {
        Interlocked.Increment(ref OpenCalls);
        lock (_queue) return _queue.Count > 0 ? _queue.Dequeue() : null;
    }
}

/// <summary>Models a device: IRoot ping/getFeature plus per-(feature index, function) handlers.</summary>
internal sealed class FakeDevice
{
    public const byte Reprog = 5, Thumb = 7, Haptics = 9, Battery = 3, HiRes = 11;

    public HashSet<byte> AcceptedIndices { get; } = [0xFF];
    public Dictionary<ushort, byte> Features { get; } = new()
    {
        [FeatureId.ReprogControlsV4] = Reprog,
        [FeatureId.ThumbWheel] = Thumb,
        [FeatureId.Haptics] = Haptics,
        [FeatureId.UnifiedBattery] = Battery,
        [FeatureId.HiResWheel] = HiRes,
    };
    public Dictionary<(byte Feature, byte Function), Func<byte[], byte[]?>> Handlers { get; } = [];
    public volatile bool Silent;

    private readonly object _lock = new();
    private readonly List<(byte Device, byte Feature, byte Function, byte[] Params)> _calls = [];
    public List<(byte Device, byte Feature, byte Function, byte[] Params)> Calls { get { lock (_lock) return [.. _calls]; } }

    public int CountCalls(byte feature, byte function) => Calls.Count(c => c.Feature == feature && c.Function == function);

    public static byte[] Frame(byte dev, byte feature, byte function, byte swId, params byte[] p)
        => HidppMessage.Encode(dev, feature, function, swId, p);

    public IEnumerable<byte[]> Handle(byte[] report)
    {
        byte dev = report[1], feat = report[2], fn = (byte)(report[3] >> 4), sw = (byte)(report[3] & 0xF);
        var p = report[4..];
        lock (_lock) _calls.Add((dev, feat, fn, p));
        if (Silent || !AcceptedIndices.Contains(dev)) return [];

        if (feat == 0)
        {
            if (fn == 0)
            {
                ushort id = (ushort)((p[0] << 8) | p[1]);
                return [Frame(dev, 0, 0, sw, Features.TryGetValue(id, out var idx) ? idx : (byte)0, 0, 0)];
            }
            if (fn == 1) return [Frame(dev, 0, 1, sw, 4, 2, p[2])];
        }
        if (Handlers.TryGetValue((feat, fn), out var h))
        {
            var reply = h(p);
            return reply == null ? [] : [Frame(dev, feat, fn, sw, reply)];
        }
        return [];
    }

    public FakeTransport NewTransport() => new() { OnWrite = Handle };
}

internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not met");
            await Task.Delay(5);
        }
    }

    public static LogiMouseOptions FastOptions() => new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(150),
        MaxRetries = 1,
        RetryDelayBase = TimeSpan.FromMilliseconds(1),
        ReadPollMs = 20,
        ProbeTimeout = TimeSpan.FromMilliseconds(60),
        BackoffInitial = TimeSpan.FromMilliseconds(20),
        BackoffMax = TimeSpan.FromMilliseconds(80),
        HeartbeatInterval = TimeSpan.FromMilliseconds(30),
        AsleepReconnectAfter = TimeSpan.FromSeconds(60),
        DisposeUndivertBudget = TimeSpan.FromMilliseconds(500),
    };
}
