using System.Diagnostics;

namespace Slender.Hidpp;

public sealed class HidppChannelOptions
{
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(2000);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelayBase { get; init; } = TimeSpan.FromMilliseconds(100);
    public int ReadPollMs { get; init; } = 250;
    public byte SwId { get; init; } = HidppChannel.DefaultSwId;
}

/// <summary>
/// One background reader per transport, request/response correlation, retries, notifications.
/// Requests are serialised (one in flight). Requests must NEVER be issued synchronously from a
/// <see cref="Notification"/>/<see cref="ReportReceived"/> handler (those run on the reader thread, so a
/// blocking request would wait for a response only the blocked reader could deliver); this is guarded and throws.
/// </summary>
public sealed class HidppChannel : IDisposable
{
    public const byte DefaultSwId = 0x0B;

    [ThreadStatic] private static bool t_inHandler;

    private sealed class Pending(byte feature, byte function)
    {
        public byte Feature { get; } = feature;
        public byte Function { get; } = function;
        public TaskCompletionSource<HidppMessage> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly IHidppTransport _transport;
    private readonly HidppChannelOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly object _lock = new();
    private readonly TaskCompletionSource _faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Pending? _pending;
    private Task? _reader;
    private bool _disposed;

    public HidppChannel(IHidppTransport transport, HidppChannelOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new HidppChannelOptions();
    }

    public byte SwId => _options.SwId;

    /// <summary>Raised (on the reader thread) for reports that are not the response to the in-flight request.</summary>
    public event EventHandler<HidppMessage>? Notification;

    /// <summary>Raised (on the reader thread) for every parsed inbound report, before dispatch.</summary>
    public event EventHandler<HidppMessage>? ReportReceived;

    /// <summary>Raised for every report written (raw 20 bytes). For diagnostics.</summary>
    public event EventHandler<byte[]>? ReportSent;

    /// <summary>Completes when the reader loop dies (transport error) or the channel is disposed.</summary>
    public Task Faulted => _faulted.Task;
    public Exception? FaultException { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _reader ??= Task.Run(ReadLoopAsync);
        }
    }

    private async Task ReadLoopAsync()
    {
        var buf = new byte[64];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = await _transport.ReadAsync(buf, _options.ReadPollMs, _cts.Token).ConfigureAwait(false);
                if (n <= 0) continue;
                if (!HidppMessage.TryParse(buf.AsSpan(0, n), out var msg)) continue;
                Dispatch(msg);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested) Fault(ex);
        }
    }

    private void Dispatch(HidppMessage msg)
    {
        Raise(ReportReceived, msg);

        Pending? matched = null;
        lock (_lock)
        {
            var p = _pending;
            if (p != null && Matches(p, msg))
            {
                matched = p;
                _pending = null;
            }
        }

        if (matched != null)
        {
            if (msg.IsError) matched.Tcs.TrySetException(new HidppErrorException(msg.ErrorCode));
            else matched.Tcs.TrySetResult(msg);
            return;
        }
        if (msg.IsError) return; // stale error for a request we gave up on
        Raise(Notification, msg);
    }

    private bool Matches(Pending p, HidppMessage m) => m.IsError
        ? m.ErrorFeatureIndex == p.Feature && m.ErrorFunction == p.Function && m.ErrorSwId == SwId
        : m.FeatureIndex == p.Feature && m.Function == p.Function && m.SwId == SwId;

    private void Raise(EventHandler<HidppMessage>? handler, HidppMessage msg)
    {
        if (handler == null) return;
        t_inHandler = true;
        try { handler(this, msg); }
        catch (Exception ex) { Debug.WriteLine($"HID++ handler threw: {ex}"); }
        finally { t_inHandler = false; }
    }

    private void Fault(Exception ex)
    {
        FaultException = ex;
        FailPending(new HidppException("HID++ transport faulted", ex));
        _faulted.TrySetResult();
    }

    private void FailPending(Exception ex)
    {
        Pending? p;
        lock (_lock) { p = _pending; _pending = null; }
        p?.Tcs.TrySetException(ex);
    }

    private static void GuardNotReaderThread()
    {
        if (t_inHandler)
            throw new InvalidOperationException("HID++ requests must not be issued from a notification handler (reader thread); hop to another thread first.");
    }

    /// <summary>Sends a request and awaits the matching response. Throws <see cref="HidppTimeoutException"/> / <see cref="HidppErrorException"/>.</summary>
    public Task<HidppMessage> RequestAsync(byte deviceIndex, byte featureIndex, byte function, ReadOnlyMemory<byte> parameters,
        CancellationToken ct, int? maxRetries = null, TimeSpan? timeout = null)
    {
        GuardNotReaderThread(); // thrown synchronously, not captured in the task
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequestCoreAsync(deviceIndex, featureIndex, function, parameters, ct, maxRetries, timeout);
    }

    private async Task<HidppMessage> RequestCoreAsync(byte deviceIndex, byte featureIndex, byte function, ReadOnlyMemory<byte> parameters,
        CancellationToken ct, int? maxRetries, TimeSpan? timeout)
    {
        int retries = maxRetries ?? _options.MaxRetries;
        var perAttempt = timeout ?? _options.RequestTimeout;
        var frame = HidppMessage.Encode(deviceIndex, featureIndex, function, SwId, parameters.Span);

        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                var pend = new Pending(featureIndex, function);
                lock (_lock) { _pending = pend; }
                try
                {
                    await _transport.WriteAsync(frame, ct).ConfigureAwait(false);
                    ReportSent?.Invoke(this, frame);
                    return await pend.Tcs.Task.WaitAsync(perAttempt, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    if (attempt >= retries)
                        throw new HidppTimeoutException($"No response from device 0x{deviceIndex:X2} feature index {featureIndex} fn {function}");
                }
                catch (HidppErrorException e) when (e.IsTransient && attempt < retries) { }
                finally
                {
                    lock (_lock) { if (ReferenceEquals(_pending, pend)) _pending = null; }
                }
                await Task.Delay(_options.RetryDelayBase * (attempt + 1), ct).ConfigureAwait(false);
            }
        }
        finally { _requestLock.Release(); }
    }

    /// <summary>Writes a request without waiting for the response (the response surfaces as a notification).</summary>
    public ValueTask SendAsync(byte deviceIndex, byte featureIndex, byte function, ReadOnlyMemory<byte> parameters, CancellationToken ct)
    {
        GuardNotReaderThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return SendCoreAsync(deviceIndex, featureIndex, function, parameters, ct);
    }

    private async ValueTask SendCoreAsync(byte deviceIndex, byte featureIndex, byte function, ReadOnlyMemory<byte> parameters, CancellationToken ct)
    {
        var frame = HidppMessage.Encode(deviceIndex, featureIndex, function, SwId, parameters.Span);
        await _transport.WriteAsync(frame, ct).ConfigureAwait(false);
        ReportSent?.Invoke(this, frame);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _cts.Cancel();
        _transport.Dispose();
        FailPending(new ObjectDisposedException(nameof(HidppChannel)));
        _faulted.TrySetResult();
    }
}
