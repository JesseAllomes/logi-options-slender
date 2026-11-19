using Slender.Engine.Devices;

namespace Slender.GalaxyBuds;

/// <summary>
/// The only messages Slender ever sends to the buds, with the payload length each takes. The send path checks this for every frame;
/// anything else (firmware, reset, power, pairing, factory/test modes, Find-My keys, hearing data, 144 touchpad lock, 136/167 manager
/// messages, 166/157 loud tests) is refused. 34 is the model request and only valid empty.
/// </summary>
public static class BudsAllowlist
{
    private static readonly Dictionary<byte, int> Lengths = new()
    {
        [BudsIds.DebugSku] = 0,
        [BudsIds.NoiseControls] = 1,
        [BudsIds.AmbientLevel] = 1,
        [BudsIds.NoiseOneBud] = 1,
        [BudsIds.VoiceDetect] = 1,
        [BudsIds.VoiceDetectTimeout] = 1,
        [BudsIds.SirenDetect] = 1,
        [BudsIds.TouchHold] = 2,
        [BudsIds.Cycle] = 2,
        [BudsIds.EqPreset] = 1,
        [BudsIds.AdaptiveEq] = 1,
        [BudsIds.Balance] = 1,
        [BudsIds.SpatialAudio] = 2,
        [BudsIds.AmbientCustom] = 4,
        [BudsIds.Sidetone] = 1,
        [BudsIds.InEarCalls] = 1,
        [BudsIds.Seamless] = 1,
        [BudsIds.AutoPause] = 1,
        [BudsIds.FindStart] = 0,
        [BudsIds.FindStop] = 0,
    };

    public static bool IsAllowed(byte id, int payloadLength) => Lengths.TryGetValue(id, out int len) && len == payloadLength;

    public static IEnumerable<byte> AllowedIds => Lengths.Keys;
}

/// <summary>Turns a catalog setting and logical raw value into the buds' message. Pure.</summary>
public static class BudsCommands
{
    /// <summary>
    /// Builds the message for <paramref name="key"/> = <paramref name="raw"/>. Messages that carry several fields take the others from
    /// <paramref name="m"/> (what the buds last reported); when one of those is not known yet, there is no command and
    /// <paramref name="problem"/> says why. Settings that are not writable (gestures, adaptive volume) never produce one.
    /// </summary>
    public static bool TryBuild(string key, int raw, BudsModel m, out byte id, out byte[] payload, out string? problem)
    {
        id = 0;
        payload = [];
        problem = null;
        var def = GalaxyBudsSettingsCatalog.Find(key);
        if (def is null || !def.Accepts(raw))
        {
            problem = "That setting or value is not allowed.";
            return false;
        }

        byte v = (byte)raw;
        switch (key)
        {
            case GalaxyBudsSettingsCatalog.NoiseMode: return One(BudsIds.NoiseControls, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.AmbientLevel: return One(BudsIds.AmbientLevel, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.NoiseOneBud: return One(BudsIds.NoiseOneBud, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.VoiceDetect: return One(BudsIds.VoiceDetect, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.VoiceDetectTimeout: return One(BudsIds.VoiceDetectTimeout, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.SirenDetect: return One(BudsIds.SirenDetect, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.EqPreset: return One(BudsIds.EqPreset, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.AdaptiveEq: return One(BudsIds.AdaptiveEq, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.Balance: return One(BudsIds.Balance, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.Sidetone: return One(BudsIds.Sidetone, v, out id, out payload);
            case GalaxyBudsSettingsCatalog.AutoPause: return One(BudsIds.AutoPause, v, out id, out payload);
            // Wire 0 = on for these two.
            case GalaxyBudsSettingsCatalog.InEarCalls: return One(BudsIds.InEarCalls, (byte)(raw == 0 ? 1 : 0), out id, out payload);
            case GalaxyBudsSettingsCatalog.Seamless: return One(BudsIds.Seamless, (byte)(raw == 0 ? 1 : 0), out id, out payload);

            case GalaxyBudsSettingsCatalog.TouchHoldLeft:
                return Two(BudsIds.TouchHold, v, m.Get(GalaxyBudsSettingsCatalog.TouchHoldRight), true, out id, out payload, out problem);
            case GalaxyBudsSettingsCatalog.TouchHoldRight:
                return Two(BudsIds.TouchHold, m.Get(GalaxyBudsSettingsCatalog.TouchHoldLeft), v, false, out id, out payload, out problem);
            case GalaxyBudsSettingsCatalog.CycleLeft:
                return Two(BudsIds.Cycle, v, m.Get(GalaxyBudsSettingsCatalog.CycleRight), true, out id, out payload, out problem);
            case GalaxyBudsSettingsCatalog.CycleRight:
                return Two(BudsIds.Cycle, m.Get(GalaxyBudsSettingsCatalog.CycleLeft), v, false, out id, out payload, out problem);

            case GalaxyBudsSettingsCatalog.Audio360:
                return Two(BudsIds.SpatialAudio, v, m.Get(GalaxyBudsSettingsCatalog.HeadTracking), true, out id, out payload, out problem);
            case GalaxyBudsSettingsCatalog.HeadTracking:
                return Two(BudsIds.SpatialAudio, m.Get(GalaxyBudsSettingsCatalog.Audio360), v, false, out id, out payload, out problem);

            case GalaxyBudsSettingsCatalog.AmbientCustom:
            case GalaxyBudsSettingsCatalog.AmbientCustomLeft:
            case GalaxyBudsSettingsCatalog.AmbientCustomRight:
            case GalaxyBudsSettingsCatalog.AmbientCustomTone:
                return Ambient(key, raw, m, out id, out payload, out problem);
        }

        problem = "That setting cannot be changed from here.";
        return false;
    }

    private static bool One(byte msg, byte value, out byte id, out byte[] payload)
    {
        id = msg;
        payload = [value];
        return true;
    }

    /// <summary>A two-byte message: the changed side is given, the other side comes from the buds' last report.</summary>
    private static bool Two(byte msg, int? a, int? b, bool changedFirst, out byte id, out byte[] payload, out string? problem)
    {
        id = msg;
        payload = [];
        problem = null;
        if (a is null || b is null)
        {
            problem = "The buds have not reported their current settings yet.";
            return false;
        }

        payload = [(byte)a.Value, (byte)b.Value];
        _ = changedFirst;
        return true;
    }

    private static bool Ambient(string key, int raw, BudsModel m, out byte id, out byte[] payload, out string? problem)
    {
        id = BudsIds.AmbientCustom;
        payload = [];
        problem = null;
        int? on = m.Get(GalaxyBudsSettingsCatalog.AmbientCustom);
        int? l = m.Get(GalaxyBudsSettingsCatalog.AmbientCustomLeft);
        int? r = m.Get(GalaxyBudsSettingsCatalog.AmbientCustomRight);
        int? tone = m.Get(GalaxyBudsSettingsCatalog.AmbientCustomTone);
        switch (key)
        {
            case GalaxyBudsSettingsCatalog.AmbientCustom: on = raw; break;
            case GalaxyBudsSettingsCatalog.AmbientCustomLeft: l = raw; break;
            case GalaxyBudsSettingsCatalog.AmbientCustomRight: r = raw; break;
            default: tone = raw; break;
        }

        if (on is null || l is null || r is null || tone is null)
        {
            problem = "The buds have not reported their current settings yet.";
            return false;
        }

        payload = [(byte)on.Value, (byte)l.Value, (byte)r.Value, (byte)tone.Value];
        return true;
    }
}
