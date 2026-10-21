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
        // Spare controls (flags 0x464/0x474 in the 2026-10-15 inventory): divertable, diverted only while bound.
        KeyboardId.Spare00EB, KeyboardId.Spare00EC, KeyboardId.Spare0101,
    ];

    /// <summary>Easy-Switch channels on the K860 (0x40A3 host indices 0..2).</summary>
    public const int ChannelCount = 3;

    private bool? _fnDesired;
    private DisabledKeys? _disabledDesired;
    private bool?[] _channelFnDesired = new bool?[ChannelCount];
    private volatile int _currentChannel = -1;
    private volatile int _fnNow = -1; // the current host's Fn inversion as last read or written: -1 unknown, 0 off, 1 on

    /// <summary>The keyboard's own Fn lock (current host) as last read or written; null when unknown or disconnected.</summary>
    public bool? FnLockNow => _fnNow switch { 0 => false, 1 => true, _ => null };

    /// <summary>The Fn lock state was read from the keyboard (on connect and on <see cref="ReadFnLockAsync"/>). Background thread.</summary>
    public event EventHandler<bool>? FnLockRead;

    public LogiKeyboard(IHidppTransportFactory factory, LogiKeyboardOptions? options = null)
        : base(factory, options ?? new LogiKeyboardOptions()) { }

    public override string Name => "ERGO K860";

    public event EventHandler<ButtonEvent>? KeyChanged;

    protected override IReadOnlyCollection<ushort> KnownDivertableControls => DivertableKeys;

    protected override void DispatchControl(ButtonEvent e) => KeyChanged?.Invoke(this, e);

    public int? CurrentChannel => _currentChannel is var c and >= 0 ? c : null;

    /// <summary>
    /// Reads the Easy-Switch channels again (0x1814 fn0, 0x1815 fn0/fn1/fn3; read only) and adopts the real current channel.
    /// Null when disconnected or the keyboard lacks the features.
    /// </summary>
    public async Task<HostsInfo?> RefreshHostsAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = CurrentSession;
            if (s == null || !IsConnected) return null;
            var hosts = await ReadHostsAsync(s, ct).ConfigureAwait(false);
            if (hosts is { Current: >= 0 }) _currentChannel = hosts.Current;
            return hosts;
        }
        finally { Gate.Release(); }
    }

    protected override async Task ApplyAfterDivertsAsync(Session s, CancellationToken ct)
    {
        // Real Easy-Switch state first (0x1814/0x1815 reads): the current channel it names replaces the 0x40A3 inference below.
        if (await ReadHostsAsync(s, ct).ConfigureAwait(false) is { Current: >= 0 } hosts) _currentChannel = hosts.Current;
        bool? fn;
        DisabledKeys? disabled;
        bool?[] channels;
        lock (State) { fn = _fnDesired; disabled = _disabledDesired; channels = (bool?[])_channelFnDesired.Clone(); }
        // Only a change made while the keyboard was away is written here. Once applied it is forgotten, so a later Fn+Esc on the
        // keyboard is never overwritten by a reconnect; the keyboard keeps its own Fn lock.
        if (fn is { } on && await TrySetFnAsync(s, on, ct).ConfigureAwait(false))
        {
            lock (State) if (_fnDesired == on) _fnDesired = null;
            _fnNow = on ? 1 : 0;
        }
        // Per-channel values work the same way: a change made while away is written once, then forgotten.
        if (await ApplyChannelsAsync(s, channels, ct).ConfigureAwait(false)) ForgetWrittenChannels(channels);
        if (disabled is { } keys) await TrySetDisabledAsync(s, keys, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 0x40A3 event: the keyboard announces Fn+Esc at once. The read that follows is authoritative and raises <see cref="FnLockRead"/>,
    /// so the tray saves the new state within a moment instead of at the next 30 s poll. Read only.
    /// </summary>
    protected override bool HandleOtherNotification(Session s, HidppMessage msg)
    {
        if (!s.Device.TryGetCachedIndex(KeyboardFeatureId.FnInversionMultiHost, out var fn) || msg.FeatureIndex != fn) return false;
        _ = Task.Run(async () =>
        {
            try { await ReadFnLockAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Fn lock read after event: {ex.Message}"); }
        });
        return true;
    }

    protected override void ResetAppliedState()
    {
        _currentChannel = -1;
        _fnNow = -1;
    }

    /// <summary>
    /// 0x40A3 per host. Solaar settings_templates.py K375sFnSwap: request = [host, state] (write fn1), reply state is the byte after
    /// the host (read_skip_byte_count 1, fn0). Solaar takes the host byte from 0x1815 HostsInfo; explicit indices 0-2 are used here (the
    /// 2026-10-15 probe read all three). The current channel comes from the read-only 0x1814/0x1815 reads when the keyboard answers them;
    /// otherwise it is inferred: the one channel whose read matches a read with host 0xFF, when exactly one does.
    /// Returns true once every chosen value is written.
    /// </summary>
    private async Task<bool> ApplyChannelsAsync(Session s, bool?[] channels, CancellationToken ct)
    {
        bool written = false;
        try
        {
            if (await s.Device.GetFeatureIndexAsync(KeyboardFeatureId.FnInversionMultiHost, ct).ConfigureAwait(false) == null) return false;
            for (int i = 0; i < ChannelCount; i++)
            {
                if (channels[i] is not { } want) continue;
                await s.Device.CallAsync(KeyboardFeatureId.FnInversionMultiHost, 1,
                    new byte[] { (byte)i, want ? (byte)1 : (byte)0 }, ct).ConfigureAwait(false);
            }
            written = true;
            // The real channel (0x1814) wins; the inference (the one channel whose Fn read matches the current host) is the fallback.
            if (_currentChannel < 0) _currentChannel = await InferChannelAsync(s, ct).ConfigureAwait(false) ?? -1;
            else await ReadCurrentFnAsync(s, ct).ConfigureAwait(false);
        }
        catch (HidppException) { }
        return written;
    }

    /// <summary>Drops the pending per-channel values that were just written (a newer pending value for a channel is kept).</summary>
    private void ForgetWrittenChannels(bool?[] written)
    {
        lock (State)
            for (int i = 0; i < ChannelCount; i++)
                if (written[i] is not null && _channelFnDesired[i] == written[i]) _channelFnDesired[i] = null;
    }

    /// <summary>0x40A3 fn0 for the current host: the keyboard's Fn lock now. Records it and raises <see cref="FnLockRead"/>.</summary>
    private async Task<byte> ReadCurrentFnAsync(Session s, CancellationToken ct)
    {
        var now = (await s.Device.CallAsync(KeyboardFeatureId.FnInversionMultiHost, 0, new byte[] { CurrentHost }, ct).ConfigureAwait(false)).Params[1];
        _fnNow = now != 0 ? 1 : 0;
        try { FnLockRead?.Invoke(this, now != 0); } catch { } // a handler must not break the connect sequence
        return now;
    }

    /// <summary>
    /// Reads the keyboard's Fn lock now (the user may have pressed Fn+Esc); null when disconnected or the feature is missing.
    /// Raises <see cref="FnLockRead"/> with the value. Read only (0x40A3 fn0).
    /// </summary>
    public async Task<bool?> ReadFnLockAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var s = CurrentSession;
            if (s == null || !IsConnected) return null;
            if (await s.Device.GetFeatureIndexAsync(KeyboardFeatureId.FnInversionMultiHost, ct).ConfigureAwait(false) == null) return null;
            return await ReadCurrentFnAsync(s, ct).ConfigureAwait(false) != 0;
        }
        catch (HidppException) { return null; }
        finally { Gate.Release(); }
    }

    private async Task<int?> InferChannelAsync(Session s, CancellationToken ct)
    {
        var now = await ReadCurrentFnAsync(s, ct).ConfigureAwait(false);
        int match = -1;
        for (int i = 0; i < ChannelCount; i++)
        {
            var v = (await s.Device.CallAsync(KeyboardFeatureId.FnInversionMultiHost, 0, new byte[] { (byte)i }, ct).ConfigureAwait(false)).Params[1];
            if (v != now) continue;
            if (match >= 0) return null; // ambiguous
            match = i;
        }
        return match >= 0 ? match : null;
    }

    public async Task SetChannelFnLockAsync(IReadOnlyList<bool?> channels, CancellationToken ct)
    {
        var wanted = new bool?[ChannelCount];
        for (int i = 0; i < ChannelCount && i < channels.Count; i++) wanted[i] = channels[i];
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Merged into what is still pending, so two changes made while the keyboard is away are both written on connect.
            lock (State)
                for (int i = 0; i < ChannelCount; i++)
                    if (wanted[i] is not null) _channelFnDesired[i] = wanted[i];
            var s = CurrentSession;
            if (s == null || !IsConnected) return; // written on the next connect, then forgotten
            if (await ApplyChannelsAsync(s, wanted, ct).ConfigureAwait(false)) ForgetWrittenChannels(wanted);
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// 0x4521 per Solaar settings_templates.py DisableKeyboardKeys: fn0 = capability mask (byte 0), fn1 = read (byte 0),
    /// fn2 = write (one mask byte). Only bits the keyboard says it can switch off are sent. Returns the mask read back.
    /// </summary>
    private static async Task<DisabledKeys?> TrySetDisabledAsync(Session s, DisabledKeys keys, CancellationToken ct)
    {
        try
        {
            if (await s.Device.GetFeatureIndexAsync(KeyboardFeatureId.DisableKeys, ct).ConfigureAwait(false) == null) return null;
            var caps = (await s.Device.CallAsync(KeyboardFeatureId.DisableKeys, 0, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[0];
            var mask = (byte)((byte)keys & caps);
            await s.Device.CallAsync(KeyboardFeatureId.DisableKeys, 2, new byte[] { mask }, ct).ConfigureAwait(false);
            return (DisabledKeys)(await s.Device.CallAsync(KeyboardFeatureId.DisableKeys, 1, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false)).Params[0];
        }
        catch (HidppException) { return null; }
    }

    public async Task<DisabledKeys?> SetDisabledKeysAsync(DisabledKeys keys, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (State) _disabledDesired = keys;
            var s = CurrentSession;
            if (s == null || !IsConnected) return null; // applied on (re)connect
            return await TrySetDisabledAsync(s, keys, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
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
            if (s == null || !IsConnected) return; // applied on the next connect, then forgotten
            if (await TrySetFnAsync(s, enabled, ct).ConfigureAwait(false))
            {
                lock (State) if (_fnDesired == enabled) _fnDesired = null; // written: the keyboard owns it from here
                _fnNow = enabled ? 1 : 0;
            }
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
