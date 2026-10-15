namespace Slender.Hidpp;

public sealed class LogiKeyboardOptions : LogiDeviceOptions
{
    public LogiKeyboardOptions()
    {
        // The K860 answers on device index 0x01 (0xFF timed out in the 2026-10-15 probe); try it first.
        DeviceIndexCandidates = [0x01, 0xFF];
    }
}

/// <summary>ERGO K860 over HID++ 2.0 (BLE). Events fire on background threads; handlers must not call back synchronously.</summary>
public sealed class LogiKeyboard : LogiDeviceBase, ILogiKeyboard, IOnboardKeyboard
{
    /// <summary>
    /// Host-index byte for 0x40A3 meaning "the current host". Reads confirmed on device (0xFF echoes and matches host 0);
    /// writing with 0xFF is UNVERIFIED because the probe is read-only. A failed write is swallowed and retried on reconnect.
    /// </summary>
    public const byte CurrentHost = 0xFF;

    /// <summary>Keys confirmed divertable by 0x1B04 on the K860 (flags bit 0x20, 2026-10-15), including Easy-Switch.</summary>
    public static readonly ushort[] DivertableKeys =
    [
        KeyboardId.BrightnessDown, KeyboardId.BrightnessUp, KeyboardId.TaskView, KeyboardId.SwitchApps,
        KeyboardId.ShowDesktop, KeyboardId.Search, KeyboardId.PreviousTrack, KeyboardId.PlayPause,
        KeyboardId.NextTrack, KeyboardId.Mute, KeyboardId.VolumeDown, KeyboardId.VolumeUp,
        KeyboardId.ScreenCapture, KeyboardId.Calculator, KeyboardId.LookUp, KeyboardId.Lock,
        KeyboardId.EasySwitch1, KeyboardId.EasySwitch2, KeyboardId.EasySwitch3,
    ];

    private bool? _fnDesired;

    public LogiKeyboard(IHidppTransportFactory factory, LogiKeyboardOptions? options = null)
        : base(factory, options ?? new LogiKeyboardOptions()) { }

    public override string Name => "ERGO K860";

    public event EventHandler<ButtonEvent>? KeyChanged;

    protected override IReadOnlyCollection<ushort> KnownDivertableControls => DivertableKeys;

    protected override void DispatchControl(ButtonEvent e) => KeyChanged?.Invoke(this, e);

    protected override async Task ApplyAfterDivertsAsync(Session s, CancellationToken ct)
    {
        bool? fn;
        lock (State) fn = _fnDesired;
        if (fn is { } on) await TrySetFnAsync(s, on, ct).ConfigureAwait(false);
    }

    private static async Task<bool> TrySetFnAsync(Session s, bool on, CancellationToken ct)
    {
        try
        {
            await s.Device.CallAsync(KeyboardFeatureId.FnInversionMultiHost, 1,
                new byte[] { CurrentHost, on ? (byte)1 : (byte)0 }, ct).ConfigureAwait(false);
            return true;
        }
        catch (HidppException) { return false; }
    }

    public Task SetDivertedKeysAsync(IReadOnlyCollection<ushort> keyIds, CancellationToken ct) =>
        SetDivertedCoreAsync(keyIds, ct);

    public async Task SetFnLockAsync(bool enabled, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _fnDesired = enabled;
            var s = CurrentSession;
            if (s == null || !IsConnected) return; // applied on (re)connect
            await TrySetFnAsync(s, enabled, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    // ---- onboard (persistent) remaps, HID++ 0x1C00 ---------------------------------------------

    private Session RequireSession()
    {
        var s = CurrentSession;
        if (s == null || !IsConnected) throw new HidppException("ERGO K860 not connected");
        return s;
    }

    public async Task<ushort?> GetOnboardCapabilitiesAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = RequireSession();
            if (await s.Device.GetFeatureIndexAsync(PersistentRemapId.Feature, ct).ConfigureAwait(false) == null) return null;
            var r = await s.Device.CallAsync(PersistentRemapId.Feature, PersistentRemapId.FnGetCapabilities, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
            return (ushort)((r.Params[0] << 8) | r.Params[1]);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Read only: fn1 count, then fn2 (index to CID) and fn3 (CID to mapping) per control, host 0xFF.</summary>
    public async Task<IReadOnlyList<OnboardMapping>> ReadOnboardMappingsAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = RequireSession();
            if (await s.Device.GetFeatureIndexAsync(PersistentRemapId.Feature, ct).ConfigureAwait(false) == null) return [];
            var count = (await s.Device.CallAsync(PersistentRemapId.Feature, PersistentRemapId.FnGetCount, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[0];
            var list = new List<OnboardMapping>(count);
            for (byte i = 0; i < count; i++)
            {
                var info = await s.Device.CallAsync(PersistentRemapId.Feature, PersistentRemapId.FnGetCidInfo,
                    new byte[] { i, PersistentRemapId.CurrentHost }, ct).ConfigureAwait(false);
                var cid = (ushort)((info.Params[0] << 8) | info.Params[1]);
                list.Add(await ReadMappingCoreAsync(s, cid, ct).ConfigureAwait(false));
            }
            return list;
        }
        finally { Gate.Release(); }
    }

    private static async Task<OnboardMapping> ReadMappingCoreAsync(Session s, ushort cid, CancellationToken ct)
    {
        var m = await s.Device.CallAsync(PersistentRemapId.Feature, PersistentRemapId.FnGetMapping,
            new byte[] { (byte)(cid >> 8), (byte)cid, PersistentRemapId.CurrentHost }, ct).ConfigureAwait(false);
        return ParseMapping(cid, m.Params);
    }

    /// <summary>fn3 reply: CID(2), host(1), action(1), param(2 BE), modifiers(1), status(1) (Solaar hidpp20.py:802-811).</summary>
    public static OnboardMapping ParseMapping(ushort requestedCid, ReadOnlySpan<byte> p) =>
        new(requestedCid, OnboardAction.FromBytes(p.Slice(3, 4)), p[7]);

    public Task SetOnboardChordAsync(ushort cid, string chord, CancellationToken ct) =>
        SetOnboardActionAsync(cid, OnboardChordEncoder.Encode(chord), ct);

    /// <summary>PERSISTENT: writes keyboard memory (fn4). Only Key and Consumer actions are accepted.</summary>
    public async Task SetOnboardActionAsync(ushort cid, OnboardAction action, CancellationToken ct)
    {
        if (cid == 0) throw new ArgumentOutOfRangeException(nameof(cid));
        if (action.Type is not (OnboardActionType.Key or OnboardActionType.Consumer))
            throw new ArgumentException($"only Key and Consumer actions may be stored, not {action.Type}", nameof(action));
        var p = new byte[7];
        p[0] = (byte)(cid >> 8); p[1] = (byte)cid; p[2] = PersistentRemapId.CurrentHost;
        action.ToBytes().CopyTo(p, 3);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = RequireSession();
            await s.Device.CallAsync(PersistentRemapId.Feature, PersistentRemapId.FnSetMapping, p, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    /// <summary>PERSISTENT: restores the factory mapping of one control (fn5).</summary>
    public async Task ResetOnboardKeyAsync(ushort cid, CancellationToken ct)
    {
        if (cid == 0) throw new ArgumentOutOfRangeException(nameof(cid));
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = RequireSession();
            await s.Device.CallAsync(PersistentRemapId.Feature, PersistentRemapId.FnReset,
                new byte[] { (byte)(cid >> 8), (byte)cid, PersistentRemapId.CurrentHost }, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
