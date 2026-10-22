namespace Slender.Engine.Devices;

/// <summary>Which Slender page a base setting belongs on: what it acts on, per the research table (docs/devices/yealink-wh64.md).</summary>
public enum YealinkTarget
{
    Headset,
    Base,
}

/// <summary>What changing a setting does beyond the setting itself. Anything but None asks the user first and is refused during a call.</summary>
public enum YealinkCaution
{
    None,

    /// <summary>The base restarts about 20 s after the change (voice language).</summary>
    Restarts,

    /// <summary>The base switches between Teams and UC behaviour (platform).</summary>
    ModeSwitch,
}

/// <summary>
/// One WH64 base setting: env key, page, USB Connect group, plain label, and what it may be set to. Values is a flat list of
/// raw/label pairs for a choice; Min..Max is the range for a number (Values null, Max &gt;= Min). A setting with neither, or with
/// Editable false, is shown read-only. Settings are always stored on the base, whichever page they appear on.
/// </summary>
public sealed record YealinkSettingDef(
    string Key,
    YealinkTarget Target,
    string Group,
    string Label,
    string[]? Values = null,
    int Min = 0,
    int Max = -1,
    bool Editable = true,
    YealinkCaution Caution = YealinkCaution.None)
{
    public bool IsChoice => Values is { Length: >= 2 };

    public bool IsRange => Values is null && Max >= Min;

    /// <summary>True when Slender may write it: editable and a choice or a range.</summary>
    public bool CanSet => Editable && (IsChoice || IsRange);

    public int ChoiceCount => IsChoice ? Values!.Length / 2 : 0;

    /// <summary>The label for a raw value; the raw text itself when the table has no entry (free text, ranges).</summary>
    public string Text(string raw)
    {
        if (Values is { } v)
            for (int i = 0; i + 1 < v.Length; i += 2)
                if (v[i] == raw) return v[i + 1];
        return raw;
    }

    public string ChoiceLabel(int index) => Values![index * 2 + 1];

    /// <summary>Index of a raw value among the choices, or -1.</summary>
    public int ChoiceIndex(string? raw)
    {
        if (raw is null || Values is not { } v) return -1;
        for (int i = 0; i + 1 < v.Length; i += 2)
            if (v[i] == raw) return i / 2;
        return -1;
    }

    /// <summary>
    /// The raw value for what the Settings app sent (<see cref="DeviceMessages.HeadsetSetName"/> lParam): the choice index for a
    /// choice, the number itself for a range. Null when the setting cannot be set or the value is out of range.
    /// </summary>
    public string? RawFor(long value)
    {
        if (!CanSet) return null;
        if (IsChoice) return value >= 0 && value < ChoiceCount ? Values![value * 2] : null;
        return value >= Min && value <= Max ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
}

/// <summary>
/// The WH64 base's settings Slender shows, in USB Connect's group order, taken from the research notes. Settings the base reports
/// but USB Connect hides (log level, softphone call control, the sidetone 0-100 alias) are left out. Free-text settings are read-only.
/// </summary>
public static class YealinkSettingsCatalog
{
    public const string General = "General", Sound = "Sound", PcSoftphone = "PC Softphone", DeskPhone = "Desk Phone", Calling = "Calling", Busylight = "Busylight";

    private const YealinkTarget H = YealinkTarget.Headset, B = YealinkTarget.Base;

    private static readonly string[] OnOff = ["0", "Off", "1", "On"];
    private static readonly string[] Sleep = ["0", "Never", "15", "15 min", "30", "30 min", "60", "1 hour", "120", "2 hours", "180", "3 hours", "240", "4 hours", "360", "6 hours", "480", "8 hours"];
    private static readonly string[] BtSleep = [.. Sleep, "960", "16 hours", "1440", "24 hours"];
    private static readonly string[] Rings = ["1", "Ring 1", "2", "Ring 2", "3", "Ring 3", "4", "Ring 4", "5", "Ring 5", "6", "Ring 6", "7", "Ring 7", "8", "Ring 8"];

    public static readonly IReadOnlyList<YealinkSettingDef> All =
    [
        new("features.bluetooth_enable", H, General, "Bluetooth", OnOff),
        new("features.bluetooth_adapter_name", H, General, "Bluetooth name", Editable: false),
        new("bluetooth.auto_connect.enable", H, General, "Bluetooth auto connect", OnOff),
        new("auto_sleep.time", H, General, "Headset auto sleep", Sleep),
        new("bt_auto_sleep.time", H, General, "Bluetooth auto sleep", BtSleep),
        new("wireless.range", B, General, "Wireless range", ["1", "Long (150 m)", "2", "Medium (30 m)", "3", "Short (10 m)"]),
        new("mute_reminder.enable", H, General, "Smart mute reminder", OnOff),
        new("voice.prompt_cycle_mute.cycle", H, General, "Mute reminder interval", ["10", "10 s", "20", "20 s", "30", "30 s", "60", "1 min", "120", "2 min", "300", "5 min", "600", "10 min", "1800", "30 min"]),
        new("base_audio.play", B, General, "USB computer audio", ["0", "Instant", "1", "Delayed", "2", "Never", "3", "Always"]),
        new("features.music_mode", B, General, "Music mode", OnOff),
        new("platform.mode", B, General, "Platform", ["Teams", "Teams", "UC", "UC"], Caution: YealinkCaution.ModeSwitch),

        new("2nd_device_play_first.enable", H, Sound, "Second device notification tone", OnOff),
        new("keypad_tone.enable", H, Sound, "Keypad tone", OnOff),
        new("wearing_preference.mode", H, Sound, "Wearing preference", ["0", "Boom arm on right", "1", "Boom arm on left"]),
        new("voice_announcements.enable", H, Sound, "Voice announcements", OnOff),
        new("language.prompt", H, Sound, "Voice guidance language", ["0", "English", "2", "Deutsch", "3", "Français", "4", "Español"], Caution: YealinkCaution.Restarts),
        new("sidetone.level", H, Sound, "Sidetone level", Min: 1, Max: 15),
        new("spectrum_call.mode", H, Sound, "Call equalizer", ["0", "Normal", "1", "Bass", "2", "Treble"]),
        new("soundguard.anti_startle.type", H, Sound, "Anti-startle", ["0", "Basic PeakStop protection", "1", "Australian G616"]),
        new("soundguard.daily_noise_exposure", H, Sound, "Daily noise limit", ["0", "No limit", "1", "80 dBA", "2", "85 dBA"]),
        new("soundguard.daily_noise_exposure.time", H, Sound, "Daily noise limit time", ["2", "2 hours", "4", "4 hours", "6", "6 hours", "8", "8 hours"]),

        new("usb_ringtone.enable", H, PcSoftphone, "Headset ringtone", OnOff),
        new("voice.ringtone.usb_ring_volume", B, PcSoftphone, "Base ringtone volume", Min: 0, Max: 15),
        new("voice.ringtone.usb_ring_type", H, PcSoftphone, "Ring type", Rings),

        new("phone_ringtone.enable", H, DeskPhone, "Headset ringtone", OnOff),
        new("voice.ringtone.phone_ring_volume", B, DeskPhone, "Base ringtone volume", Min: 0, Max: 15),
        new("phone_voice.ringtone.usb_ring_type", H, DeskPhone, "Ring type", Rings),

        new("call_device.priority", B, Calling, "Call device priority", ["1", "Desk phone priority", "2", "Soft phone priority"]),
        new("call_device_pc.priority", B, Calling, "PC call device", Editable: false),
        new("call.priority", B, Calling, "Call priority", ["1", "Current call", "2", "New call"]),
        new("predial_when_undocked.enable", H, Calling, "Auto off-hook", OnOff),
        new("feature_predial.enable", H, Calling, "Predial", OnOff),
        new("multiple_call_handling.mode", H, Calling, "Button on second call", ["0", "End current call only", "1", "Answer new incoming call"]),
        new("call_record.enable", B, Calling, "Call recording", OnOff),
        new("phone.type", B, Calling, "Phone type", ["0", "Normal phone", "1", "Unify"]),
        new("auto_answer.enable", H, Calling, "Auto answer when undocked", OnOff),
        new("sensor_auto_answer.enable", H, Calling, "Auto answer when boom drops", OnOff),
        new("training_mode.enable", B, Calling, "Training mode", OnOff),
        new("conf_permanent_mode.enable", B, Calling, "Permanent conference mode", OnOff),

        new("busylight_built_in.manual_enable", H, Busylight, "Focus mode", OnOff),
        new("busylight_built_in.sync_enable", H, Busylight, "Calling indication", OnOff),
        new("phone_setting.busy_light_custom_type", H, Busylight, "Busylight", ["0", "Sync", "1", "Available", "2", "Do not disturb", "3", "Off"]),
    ];

    /// <summary>Index in <see cref="All"/>, or -1. Linear: the list is short.</summary>
    public static int IndexOf(string? key)
    {
        if (key is null) return -1;
        for (int i = 0; i < All.Count; i++)
            if (All[i].Key == key) return i;
        return -1;
    }
}
