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
public sealed class LogiKeyboard : LogiDeviceBase, ILogiKeyboard
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
}
