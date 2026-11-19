namespace Slender.Engine.Devices;

/// <summary>
/// One Galaxy Buds setting Slender shows and (when <see cref="CanSet"/>) may change. Raw is a logical int: for a choice one of the
/// even entries of Values (raw/label pairs), for a range a number Min..Max. The module maps each key to the buds' own message and
/// payload (src/Slender.GalaxyBuds); the catalog never holds wire ids. ShowWhenKey/ShowWhenRaws hide a setting unless another
/// setting holds one of the listed raws, as Galaxy Wearable does. Settings marked Editable = false are shown read-only (the write
/// is unproven or risky on Buds4 Pro, see the private research map).
/// </summary>
public sealed record BudsSettingDef(
    string Key,
    string Group,
    string Label,
    string[]? Values = null,
    int Min = 0,
    int Max = -1,
    bool Editable = true,
    string? Help = null,
    string? ShowWhenKey = null,
    int[]? ShowWhenRaws = null)
{
    public bool IsChoice => Values is { Length: >= 4 };

    public bool IsRange => Values is null && Max >= Min;

    public bool CanSet => Editable && (IsChoice || IsRange);

    public int ChoiceCount => IsChoice ? Values!.Length / 2 : 0;

    /// <summary>Raw value of choice <paramref name="index"/>; null when out of range.</summary>
    public int? ChoiceRaw(int index) =>
        IsChoice && index >= 0 && index < ChoiceCount && int.TryParse(Values![index * 2], out var r) ? r : null;

    /// <summary>True when Slender may write <paramref name="raw"/>: a listed choice, or inside the range.</summary>
    public bool Accepts(int raw)
    {
        if (!CanSet) return false;
        if (IsRange) return raw >= Min && raw <= Max;
        for (int i = 0; i < ChoiceCount; i++)
            if (ChoiceRaw(i) == raw) return true;
        return false;
    }

    /// <summary>The label for a raw value; the number itself when the table has no entry (ranges, values the buds report but Slender does not list).</summary>
    public string Text(int raw)
    {
        if (Values is { } v)
            for (int i = 0; i + 1 < v.Length; i += 2)
                if (v[i] == raw.ToString(System.Globalization.CultureInfo.InvariantCulture)) return v[i + 1];
        return raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Every Galaxy Buds4 Pro setting Slender handles, in page order (groups as Galaxy Wearable groups them). Settings → tray messages
/// carry an index into <see cref="All"/>, so append new entries at the end of their group only before a release, never reorder
/// after one ships. Source: personal/galaxy-buds-research docs/capability-map.md (2026-11-19).
/// </summary>
public static class GalaxyBudsSettingsCatalog
{
    public const string NoiseMode = "noise_mode";
    public const string AmbientLevel = "ambient_level";
    public const string NoiseOneBud = "noise_one_bud";
    public const string VoiceDetect = "voice_detect";
    public const string VoiceDetectTimeout = "voice_detect_timeout";
    public const string SirenDetect = "siren_detect";
    public const string TouchHoldLeft = "touch_hold_left";
    public const string TouchHoldRight = "touch_hold_right";
    public const string CycleLeft = "cycle_left";
    public const string CycleRight = "cycle_right";
    public const string Gestures = "gestures";
    public const string EqPreset = "eq_preset";
    public const string AdaptiveEq = "adaptive_eq";
    public const string Balance = "balance";
    public const string Audio360 = "audio_360";
    public const string HeadTracking = "head_tracking";
    public const string AdaptiveVolume = "adaptive_volume";
    public const string AmbientCustom = "ambient_custom";
    public const string AmbientCustomLeft = "ambient_custom_left";
    public const string AmbientCustomRight = "ambient_custom_right";
    public const string AmbientCustomTone = "ambient_custom_tone";
    public const string Sidetone = "sidetone";
    public const string InEarCalls = "in_ear_calls";
    public const string Seamless = "seamless";
    public const string AutoPause = "auto_pause";

    public const string GroupNoise = "Noise control";
    public const string GroupTouch = "Touch controls";
    public const string GroupSound = "Sound";
    public const string GroupAmbient = "Ambient sound";
    public const string GroupCalls = "Calls";
    public const string GroupConnection = "Connection";

    private static readonly string[] OnOff = ["0", "Off", "1", "On"];

    // Noise-control cycle masks: ambient 1, adaptive 2, off 4, noise cancelling 8; at least two modes.
    private static readonly string[] Cycle =
    [
        "9", "Noise cancelling, Ambient",
        "10", "Noise cancelling, Adaptive",
        "3", "Ambient, Adaptive",
        "11", "Noise cancelling, Ambient, Adaptive",
        "12", "Noise cancelling, Off",
        "5", "Ambient, Off",
        "6", "Adaptive, Off",
        "13", "Noise cancelling, Ambient, Off",
        "14", "Noise cancelling, Adaptive, Off",
        "7", "Ambient, Adaptive, Off",
        "15", "All four",
    ];

    private static readonly string[] TouchHold =
    [
        "2", "Switch noise controls",
        "1", "Voice assistant",
        "3", "Volume (left down, right up)",
        "4", "Spotify",
    ];

    public static IReadOnlyList<BudsSettingDef> All { get; } =
    [
        new(NoiseMode, GroupNoise, "Noise controls", ["0", "Off", "1", "Noise cancelling", "2", "Ambient sound", "3", "Adaptive"]),
        new(AmbientLevel, GroupNoise, "Ambient sound level", ["0", "1", "1", "2", "2", "3", "3", "4", "4", "5"]),
        new(NoiseOneBud, GroupNoise, "Noise controls with one earbud", OnOff),
        new(VoiceDetect, GroupNoise, "Voice detect", OnOff, Help: "Turns on ambient sound and lowers media when you start talking."),
        new(VoiceDetectTimeout, GroupNoise, "Back to previous mode after", ["0", "5 seconds", "1", "10 seconds", "2", "15 seconds"],
            ShowWhenKey: VoiceDetect, ShowWhenRaws: [1]),
        new(SirenDetect, GroupNoise, "Siren detect", OnOff, Help: "Lets in ambient sound when a siren is heard."),

        new(TouchHoldLeft, GroupTouch, "Touch and hold: left", TouchHold),
        new(TouchHoldRight, GroupTouch, "Touch and hold: right", TouchHold),
        new(CycleLeft, GroupTouch, "Left switches between", Cycle, ShowWhenKey: TouchHoldLeft, ShowWhenRaws: [2]),
        new(CycleRight, GroupTouch, "Right switches between", Cycle, ShowWhenKey: TouchHoldRight, ShowWhenRaws: [2]),
        new(Gestures, GroupTouch, "Gestures", Editable: false, Help: "Shown only: changing gestures from a PC can upset the buds' gesture setup."),

        new(EqPreset, GroupSound, "Equaliser", ["0", "Normal", "1", "Bass boost", "2", "Soft", "3", "Dynamic", "4", "Clear", "5", "Treble boost"]),
        new(AdaptiveEq, GroupSound, "Adaptive EQ", OnOff),
        new(Balance, GroupSound, "Left/right balance", Min: 0, Max: 32, Help: "16 is centred."),
        new(Audio360, GroupSound, "360 Audio", OnOff),
        new(HeadTracking, GroupSound, "Head tracking", OnOff, ShowWhenKey: Audio360, ShowWhenRaws: [1]),
        new(AdaptiveVolume, GroupSound, "Adaptive volume", OnOff, Editable: false, Help: "Shown only: the setting does not take reliably from a PC yet."),

        new(AmbientCustom, GroupAmbient, "Customise ambient sound", OnOff),
        new(AmbientCustomLeft, GroupAmbient, "Left amplification", Min: 0, Max: 4, ShowWhenKey: AmbientCustom, ShowWhenRaws: [1]),
        new(AmbientCustomRight, GroupAmbient, "Right amplification", Min: 0, Max: 4, ShowWhenKey: AmbientCustom, ShowWhenRaws: [1]),
        new(AmbientCustomTone, GroupAmbient, "Ambient tone", ["0", "Soft", "1", "Softer", "2", "Neutral", "3", "Clearer", "4", "Clear"],
            ShowWhenKey: AmbientCustom, ShowWhenRaws: [1]),

        new(Sidetone, GroupCalls, "Hear my voice in calls", OnOff),
        new(InEarCalls, GroupCalls, "Switch calls to earbuds when worn", OnOff),

        new(Seamless, GroupConnection, "Auto switch to other devices", OnOff, Help: "Samsung devices on the same account."),
        new(AutoPause, GroupConnection, "Pause media when an earbud is removed", OnOff),
    ];

    public static int IndexOf(string key)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Key == key) return i;
        return -1;
    }

    public static BudsSettingDef? Find(string key) => IndexOf(key) is var i and >= 0 ? All[i] : null;
}
