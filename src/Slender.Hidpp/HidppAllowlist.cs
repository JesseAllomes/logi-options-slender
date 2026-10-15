namespace Slender.Hidpp;

/// <summary>Thrown before any bytes are written when a request is not on the write allowlist.</summary>
public sealed class HidppBlockedRequestException : HidppException
{
    public ushort FeatureId { get; }
    public byte Function { get; }

    public HidppBlockedRequestException(ushort featureId, byte function)
        : base($"HID++ request blocked by allowlist: feature 0x{featureId:X4} fn {function}")
    {
        FeatureId = featureId;
        Function = function;
    }
}

/// <summary>
/// The only (feature, function) requests Slender may send. Anything else (notably firmware update 0x00D0/0x00D1,
/// host change/pairing 0x1814/0x1815 and onboard profiles 0x8100) is refused by <see cref="HidppDevice"/>
/// before the feature index is even looked up.
/// </summary>
public static class HidppAllowlist
{
    private static readonly Dictionary<ushort, byte> MaxFunction = new()
    {
        [0x0000] = 1,
        [0x0001] = 1,
        [0x0005] = 1,
        [0x1000] = 0,
        [0x1004] = 1,
        // Haptics: fn1 getState, fn2 setState [enabled, level 0-100] (haptic strength), fn4 play waveform. fn3 is silent.
        [0x19B0] = 4,
        // Force sensing (Sense Panel): fn2 read; fn3 set force threshold [0, hi, lo] (m26). fn0/fn1 stay unused.
        [0x19C0] = 3,
        [0x1B04] = 3,
        [0x2110] = 1,
        [0x2111] = 2, // SmartShift enhanced: fn1 get, fn2 set [mode, autoDisengage, torque] (wheel resistance = torque, m26)
        [0x2121] = 2,
        [0x2150] = 2,
        [0x2201] = 3,
        // ERGO K860 Fn inversion (multi-host variant, the only one the keyboard exposes): fn0 get, fn1 set. Keyboard path only.
        [0x40A3] = 1,
        // ERGO K860 Disable Keys. Solaar settings_templates.py DisableKeyboardKeys: fn0 getCapabilities (mask), fn1 getDisabledKeys
        // (read_fnid 0x10), fn2 setDisabledKeys (write_fnid 0x20, one mask byte). Keyboard path only; volatile, re-applied on connect.
        [0x4521] = 2,
        // ERGO K860 Persistent Remappable Action: fn0-3 are reads (capabilities, count, cid info, mapping).
        // fn4 (set mapping) and fn5 (reset) WRITE PERSISTENT KEYBOARD MEMORY that survives power cycles and applies on every
        // PC. Only LogiKeyboard's onboard methods may call them (keyboard path only). See docs/protocol.md.
        [PersistentRemapId.Feature] = PersistentRemapId.FnReset,
    };

    public static bool IsAllowed(ushort featureId, byte function) =>
        MaxFunction.TryGetValue(featureId, out var max) && function <= max;

    /// <summary>Throws <see cref="HidppBlockedRequestException"/> unless the request is allowlisted.</summary>
    public static void EnsureAllowed(ushort featureId, byte function)
    {
        if (!IsAllowed(featureId, function)) throw new HidppBlockedRequestException(featureId, function);
    }
}
