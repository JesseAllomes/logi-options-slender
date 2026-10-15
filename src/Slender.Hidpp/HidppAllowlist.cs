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
        // ERGO K860 Persistent Remappable Action: fn0-3 are reads (capabilities, count, cid info, mapping).
        // fn4 (set mapping) and fn5 (reset) WRITE PERSISTENT KEYBOARD MEMORY that survives power cycles and applies on every
        // PC. Only LogiKeyboard's onboard methods may call them (keyboard path only). See docs/protocol.md.
        [PersistentRemapId.Feature] = PersistentRemapId.FnReset,
    };

    /// <summary>
    /// The only controls whose 0x1B04 setCidReporting may carry the raw XY bits (flags bit4 value, bit5 valid): the gesture button
    /// 0x00C3 and its virtual twin 0x00D7. Raw XY makes the mouse report movement deltas instead of moving the cursor while the
    /// control is held, so it is confined to the gesture path. The persistent bits (0x04/0x08), force raw XY (0x40/0x80) and any
    /// remap target (params 3-4) stay blocked for every control.
    /// </summary>
    public static readonly IReadOnlySet<ushort> RawXyControls = new HashSet<ushort> { 0x00C3, 0x00D7 };

    private const byte DivertBits = 0x03, RawXyBits = 0x30;

    public static bool IsAllowed(ushort featureId, byte function) =>
        MaxFunction.TryGetValue(featureId, out var max) && function <= max;

    /// <summary>
    /// Payload rule on top of <see cref="IsAllowed"/>: 0x1B04 fn3 (setCidReporting) may only set the temporary divert bits, plus the
    /// raw XY bits for <see cref="RawXyControls"/>. Other requests are judged by feature and function alone.
    /// </summary>
    public static bool IsAllowed(ushort featureId, byte function, ReadOnlySpan<byte> parameters)
    {
        if (!IsAllowed(featureId, function)) return false;
        if (featureId != 0x1B04 || function != 3) return true;
        if (parameters.Length < 3) return false;
        ushort cid = (ushort)((parameters[0] << 8) | parameters[1]);
        byte flags = parameters[2];
        if ((flags & ~(DivertBits | RawXyBits)) != 0) return false; // persist, force raw XY, anything unknown
        if ((flags & RawXyBits) != 0 && !RawXyControls.Contains(cid)) return false;
        for (int i = 3; i < parameters.Length; i++) if (parameters[i] != 0) return false; // no remap target
        return true;
    }

    /// <summary>Throws <see cref="HidppBlockedRequestException"/> unless the request (including its payload) is allowlisted.</summary>
    public static void EnsureAllowed(ushort featureId, byte function, ReadOnlySpan<byte> parameters)
    {
        if (!IsAllowed(featureId, function, parameters)) throw new HidppBlockedRequestException(featureId, function);
    }

    /// <summary>Throws <see cref="HidppBlockedRequestException"/> unless the request is allowlisted.</summary>
    public static void EnsureAllowed(ushort featureId, byte function)
    {
        if (!IsAllowed(featureId, function)) throw new HidppBlockedRequestException(featureId, function);
    }
}
