namespace Slender.Engine.Devices;

/// <summary>Which Slender page a base setting belongs on: what it acts on, per the research table (docs/devices/yealink-wh64.md).</summary>
public enum YealinkTarget
{
    Headset,
    Base,
}

/// <summary>What changing a setting does beyond the setting itself. Anything but None asks the user first; Restarts and ModeSwitch are also refused during a call.</summary>
public enum YealinkCaution
{
    None,

    /// <summary>The base restarts about 20 s after the change (voice language).</summary>
    Restarts,

    /// <summary>The base switches between Teams and UC behaviour (platform).</summary>
    ModeSwitch,

    /// <summary>Changes the headset's hearing protection (anti-startle, daily noise limit). Asks first; allowed during a call.</summary>
    HearingProtection,
}

/// <summary>
/// One WH64 base setting: env key, page, USB Connect group, plain label, and what it may be set to. Values is a flat list of
/// raw/label pairs for a choice; Min..Max is the range for a number (Values null, Max &gt;= Min). A setting with neither, or with
/// Editable false, is shown read-only. Settings are always stored on the base, whichever page they appear on. Help is one plain line
/// under the label. ShowWhen hides the setting unless another setting (by key) holds one of the given raw values, as USB Connect does.
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
    YealinkCaution Caution = YealinkCaution.None,
    string? Help = null,
    string? ShowWhenKey = null,
    string[]? ShowWhenRaws = null)
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
    public const string Sound = "Sound", Prompts = "Prompts and mute reminder", Ringing = "Ringing", Calls = "Calls", Busylight = "Busylight",
        Hearing = "Hearing protection", Bluetooth = "Bluetooth and sleep", BaseRinger = "Base ringer", Radio = "Radio",
        SharedHeadsets = "Shared headsets", PlugInLight = "Plug-in busylight";

    private const YealinkTarget H = YealinkTarget.Headset, B = YealinkTarget.Base;

    private static readonly string[] OnOff = ["0", "Off", "1", "On"];
    private static readonly string[] On = ["1"];
    private static readonly string[] Sleep = ["0", "Never", "15", "15 min", "30", "30 min", "60", "1 hour", "120", "2 hours", "180", "3 hours", "240", "4 hours", "360", "6 hours", "480", "8 hours"];
    private static readonly string[] BtSleep = [.. Sleep, "960", "16 hours", "1440", "24 hours"];
    private static readonly string[] Rings = ["0", "Default", "1", "Ring 1", "2", "Ring 2"];

    /// <summary>In page order: groups appear in the order their first setting does. Labels follow Yealink USB Connect's own English strings.</summary>
    public static readonly IReadOnlyList<YealinkSettingDef> All =
    [
        new("wearing_preference.mode", H, Sound, "Wearing preference", ["0", "Boom arm on right", "1", "Boom arm on left"]),
        new("sidetone.level", H, Sound, "Sidetone", Min: 1, Max: 15, Help: "How much of your own voice you hear in the headset."),
        new("spectrum_call.mode", H, Sound, "Call equalizer", ["0", "Normal", "1", "Bass", "2", "Treble"]),
        new("features.music_mode", H, Sound, "Music mode", OnOff, Help: "Better sound for music. Calls are not affected."),
        new("base_audio.play", H, Sound, "PC audio in headset", ["0", "Instant", "1", "Delayed", "2", "Never", "3", "Always"],
            Help: "When sound from the PC that is not a call plays in the headset."),
        new("2nd_device_play_first.enable", H, Sound, "Hear second device", OnOff,
            Help: "Hear tones and music from the other connected device while one is streaming."),
        new("keypad_tone.enable", H, Sound, "Keypad tone", OnOff),

        new("voice_announcements.enable", H, Prompts, "Voice prompts", OnOff),
        new("language.prompt", H, Prompts, "Voice prompt language", ["0", "English", "2", "Deutsch", "3", "Français", "4", "Español"],
            Caution: YealinkCaution.Restarts, ShowWhenKey: "voice_announcements.enable", ShowWhenRaws: On),
        new("mute_reminder.enable", H, Prompts, "Smart mute reminder", OnOff, Help: "Reminds you that you are muted when you start talking."),
        new("voice.prompt_cycle_mute.cycle", H, Prompts, "Mute reminder interval", ["10", "10 s", "20", "20 s", "30", "30 s", "60", "1 min", "120", "2 min", "300", "5 min", "600", "10 min", "1800", "30 min"],
            ShowWhenKey: "mute_reminder.enable", ShowWhenRaws: On),

        new("usb_ringtone.enable", H, Ringing, "Ring in headset: PC calls", OnOff),
        new("voice.ringtone.usb_ring_type", H, Ringing, "Ring sound: PC calls", Rings,
            Help: "The headset's own ring. 3CX and Teams play their own ringtone instead."),
        new("phone_ringtone.enable", H, Ringing, "Ring in headset: desk phone calls", OnOff),
        new("phone_voice.ringtone.usb_ring_type", H, Ringing, "Ring sound: desk phone calls", Rings),

        new("auto_answer.enable", H, Calls, "Answer when lifted from the base", OnOff),
        new("sensor_auto_answer.enable", H, Calls, "Answer when the boom arm drops", OnOff),
        new("predial_when_undocked.enable", H, Calls, "Open line when undocked", OnOff, Help: "Lifting the headset from the base gets a line."),
        new("feature_predial.enable", H, Calls, "Call button opens line", OnOff, Help: "Pressing the call button when idle gets a line."),
        new("multiple_call_handling.mode", H, Calls, "Call button during a second call", ["0", "Ends the current call only", "1", "Answers the new call"],
            ShowWhenKey: "platform.mode", ShowWhenRaws: ["UC"]),

        new("busylight_built_in.manual_enable", H, Busylight, "Busylight on (manual)", OnOff, Help: "Keeps the headset's light on to show you are busy."),
        new("busylight_built_in.sync_enable", H, Busylight, "Busylight during calls", OnOff),

        new("soundguard.anti_startle.type", H, Hearing, "Anti-startle", ["0", "Peak block", "1", "Australian G616"],
            Caution: YealinkCaution.HearingProtection, Help: "Cuts sudden loud sounds."),
        new("soundguard.daily_noise_exposure", H, Hearing, "Daily noise limit", ["0", "No limit", "1", "80 dBA", "2", "85 dBA"],
            Caution: YealinkCaution.HearingProtection),
        new("soundguard.daily_noise_exposure.time", H, Hearing, "Hours on calls per day", ["2", "2 hours", "4", "4 hours", "6", "6 hours", "8", "8 hours"],
            Caution: YealinkCaution.HearingProtection, ShowWhenKey: "soundguard.daily_noise_exposure", ShowWhenRaws: ["1", "2"]),

        new("features.bluetooth_enable", H, Bluetooth, "Bluetooth", OnOff),
        new("features.bluetooth_adapter_name", H, Bluetooth, "Bluetooth name", Editable: false),
        new("bluetooth.auto_connect.enable", H, Bluetooth, "Bluetooth auto connect", OnOff),
        new("auto_sleep.time", H, Bluetooth, "Sleep when idle (DECT)", Sleep),
        new("bt_auto_sleep.time", H, Bluetooth, "Sleep when idle (Bluetooth)", BtSleep),

        new("voice.ringtone.usb_ring_volume", B, BaseRinger, "Ring volume: PC calls", Min: 0, Max: 15),
        new("voice.ringtone.phone_ring_volume", B, BaseRinger, "Ring volume: desk phone calls", Min: 0, Max: 15),

        new("call_device.priority", B, Calls, "Default call device", ["1", "Desk phone", "2", "Softphone"]),
        new("call_device_pc.priority", B, Calls, "Softphone in control", Editable: false),
        new("call.priority", B, Calls, "When a second call comes in", ["1", "Stay on the current call", "2", "Switch to the new call"]),
        new("phone.type", B, Calls, "Desk phone type", ["0", "Standard", "1", "Unify"]),
        new("call_record.enable", B, Calls, "Send calls to the PC for recording", OnOff),
        new("platform.mode", B, Calls, "Softphone platform", ["Teams", "Teams", "UC", "UC"], Caution: YealinkCaution.ModeSwitch),

        new("wireless.range", B, Radio, "Wireless range", ["1", "Long (150 m)", "2", "Medium (30 m)", "3", "Short (10 m)"]),

        new("training_mode.enable", B, SharedHeadsets, "Training mode", OnOff, Help: "Lets more than one headset join a call."),
        new("conf_permanent_mode.enable", B, SharedHeadsets, "Permanent conference mode", OnOff, Help: "Extra headsets stay joined after calls.",
            ShowWhenKey: "training_mode.enable", ShowWhenRaws: On),

        new("phone_setting.busy_light_custom_type", B, PlugInLight, "Plug-in busylight", ["0", "Follow calls", "1", "Available", "2", "Do not disturb", "3", "Off"],
            Help: "An optional BLT60 light plugged into the base."),
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
