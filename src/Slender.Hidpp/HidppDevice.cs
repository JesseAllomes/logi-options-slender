namespace Slender.Hidpp;

public enum PingResult { Answered, Error, Timeout }

/// <summary>A device index on a channel, with an IRoot feature-index cache (IRoot fn0).</summary>
public sealed class HidppDevice
{
    private readonly object _lock = new();
    private readonly Dictionary<ushort, byte?> _cache = [];

    public HidppDevice(HidppChannel channel, byte deviceIndex)
    {
        Channel = channel;
        DeviceIndex = deviceIndex;
    }

    public HidppChannel Channel { get; }
    public byte DeviceIndex { get; }

    /// <summary>IRoot fn1 ping, single attempt. Error = the device answered with a HID++ error (still alive).</summary>
    public async Task<PingResult> PingAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await Channel.RequestAsync(DeviceIndex, 0, 1, new byte[] { 0, 0, 0x5A }, ct, maxRetries: 0, timeout: timeout).ConfigureAwait(false);
            return PingResult.Answered;
        }
        catch (HidppTimeoutException) { return PingResult.Timeout; }
        catch (HidppErrorException) { return PingResult.Error; }
    }

    /// <summary>Feature index (cached), or null if the device lacks the feature.</summary>
    public async Task<byte?> GetFeatureIndexAsync(ushort featureId, CancellationToken ct)
    {
        if (featureId == FeatureId.Root) return 0;
        lock (_lock)
        {
            if (_cache.TryGetValue(featureId, out var cached)) return cached;
        }
        var resp = await Channel.RequestAsync(DeviceIndex, 0, 0,
            new byte[] { (byte)(featureId >> 8), (byte)featureId, 0 }, ct).ConfigureAwait(false);
        byte? idx = resp.Params[0] == 0 ? null : resp.Params[0];
        lock (_lock) { _cache[featureId] = idx; }
        return idx;
    }

    public bool TryGetCachedIndex(ushort featureId, out byte index)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(featureId, out var v) && v is { } b) { index = b; return true; }
        }
        index = 0;
        return false;
    }

    public void ClearCache() { lock (_lock) _cache.Clear(); }

    public async Task<HidppMessage> CallAsync(ushort featureId, byte function, ReadOnlyMemory<byte> parameters, CancellationToken ct)
    {
        HidppAllowlist.EnsureAllowed(featureId, function); // before any index lookup or write
        var idx = await GetFeatureIndexAsync(featureId, ct).ConfigureAwait(false)
            ?? throw new HidppFeatureNotSupportedException(featureId);
        return await Channel.RequestAsync(DeviceIndex, idx, function, parameters, ct).ConfigureAwait(false);
    }

    /// <summary>Fire-and-forget call. Returns false when the feature is absent.</summary>
    public async Task<bool> SendAsync(ushort featureId, byte function, ReadOnlyMemory<byte> parameters, CancellationToken ct)
    {
        HidppAllowlist.EnsureAllowed(featureId, function);
        var idx = await GetFeatureIndexAsync(featureId, ct).ConfigureAwait(false);
        if (idx is null) return false;
        await Channel.SendAsync(DeviceIndex, idx.Value, function, parameters, ct).ConfigureAwait(false);
        return true;
    }
}
