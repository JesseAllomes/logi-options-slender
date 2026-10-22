namespace Slender.Yealink;

/// <summary>One base setting that can be shown: its env key, USB Connect's group, a plain label and the allowed values as text.</summary>
public sealed record VendorSettingDef(string Key, string Group, string Label, string[]? Values = null)
{
    /// <summary>The label for a raw value; the raw text itself when the table has no entry (free text, sliders, volumes).</summary>
    public string Text(string raw)
    {
        if (Values is { } v)
            for (int i = 0; i + 1 < v.Length; i += 2)
                if (v[i] == raw) return v[i + 1];
        return raw;
    }
}

/// <summary>
/// The WH64 base's settings that are shown (read-only), taken from the research notes: key, group as USB Connect lays them out and
/// allowed values. Settings the base reports but USB Connect hides (log level, softphone call control, the sidetone alias) are left out.
/// </summary>
public static class VendorSettingsCatalog
{
    public const string General = "General", Sound = "Sound", PcSoftphone = "PC Softphone", DeskPhone = "Desk Phone", Calling = "Calling", Busylight = "Busylight";

    private static readonly string[] OnOff = ["0", "Off", "1", "On"];
    private static readonly string[] Sleep = ["0", "Never", "15", "15 min", "30", "30 min", "60", "1 hour", "120", "2 hours", "180", "3 hours", "240", "4 hours", "360", "6 hours", "480", "8 hours"];
    private static readonly string[] BtSleep = [.. Sleep, "960", "16 hours", "1440", "24 hours"];

    /// <summary>In display order: the groups as listed in USB Connect, settings in its order within each.</summary>
    public static readonly IReadOnlyList<VendorSettingDef> All =
    [
        new("features.bluetooth_enable", General, "Bluetooth", OnOff),
        new("features.bluetooth_adapter_name", General, "Bluetooth name"),
        new("bluetooth.auto_connect.enable", General, "Bluetooth auto connect", OnOff),
        new("auto_sleep.time", General, "Headset auto sleep", Sleep),
        new("bt_auto_sleep.time", General, "Bluetooth auto sleep", BtSleep),
        new("wireless.range", General, "Wireless range", ["1", "Long (150 m)", "2", "Medium (30 m)", "3", "Short (10 m)"]),
        new("mute_reminder.enable", General, "Smart mute reminder", OnOff),
        new("voice.prompt_cycle_mute.cycle", General, "Mute reminder interval", ["10", "10 s", "20", "20 s", "30", "30 s", "60", "1 min", "120", "2 min", "300", "5 min", "600", "10 min", "1800", "30 min"]),
        new("base_audio.play", General, "USB computer audio", ["0", "Instant", "1", "Delayed", "2", "Never", "3", "Always"]),
        new("features.music_mode", General, "Music mode", OnOff),
        new("platform.mode", General, "Platform"),

        new("2nd_device_play_first.enable", Sound, "Second device notification tone", OnOff),
        new("keypad_tone.enable", Sound, "Keypad tone", OnOff),
        new("wearing_preference.mode", Sound, "Wearing preference", ["0", "Boom arm on right", "1", "Boom arm on left"]),
        new("voice_announcements.enable", Sound, "Voice announcements", OnOff),
        new("language.prompt", Sound, "Voice guidance language", ["0", "English", "2", "Deutsch", "3", "Français", "4", "Español"]),
        new("sidetone.level", Sound, "Sidetone level"),
        new("spectrum_call.mode", Sound, "Call equalizer", ["0", "Normal", "1", "Bass", "2", "Treble"]),
        new("soundguard.anti_startle.type", Sound, "Anti-startle", ["0", "Basic PeakStop protection", "1", "Australian G616"]),
        new("soundguard.daily_noise_exposure", Sound, "Daily noise limit", ["0", "No limit", "1", "80 dBA", "2", "85 dBA"]),
        new("soundguard.daily_noise_exposure.time", Sound, "Daily noise limit time", ["2", "2 hours", "4", "4 hours", "6", "6 hours", "8", "8 hours"]),

        new("usb_ringtone.enable", PcSoftphone, "Headset ringtone", OnOff),
        new("voice.ringtone.usb_ring_volume", PcSoftphone, "Base ringtone volume"),
        new("voice.ringtone.usb_ring_type", PcSoftphone, "Ring type"),

        new("phone_ringtone.enable", DeskPhone, "Headset ringtone", OnOff),
        new("voice.ringtone.phone_ring_volume", DeskPhone, "Base ringtone volume"),
        new("phone_voice.ringtone.usb_ring_type", DeskPhone, "Ring type"),

        new("call_device.priority", Calling, "Call device priority", ["1", "Desk phone priority", "2", "Soft phone priority"]),
        new("call_device_pc.priority", Calling, "PC call device"),
        new("call.priority", Calling, "Call priority", ["1", "Current call", "2", "New call"]),
        new("predial_when_undocked.enable", Calling, "Auto off-hook", OnOff),
        new("feature_predial.enable", Calling, "Predial", OnOff),
        new("multiple_call_handling.mode", Calling, "Button on second call", ["0", "End current call only", "1", "Answer new incoming call"]),
        new("call_record.enable", Calling, "Call recording", OnOff),
        new("phone.type", Calling, "Phone type", ["0", "Normal phone", "1", "Unify"]),
        new("auto_answer.enable", Calling, "Auto answer when undocked", OnOff),
        new("sensor_auto_answer.enable", Calling, "Auto answer when boom drops", OnOff),
        new("training_mode.enable", Calling, "Training mode", OnOff),
        new("conf_permanent_mode.enable", Calling, "Permanent conference mode", OnOff),

        new("busylight_built_in.manual_enable", Busylight, "Focus mode", OnOff),
        new("busylight_built_in.sync_enable", Busylight, "Calling indication", OnOff),
        new("phone_setting.busy_light_custom_type", Busylight, "Busylight", ["0", "Sync", "1", "Available", "2", "Do not disturb", "3", "Off"]),
    ];

    /// <summary>Index in <see cref="All"/>, or -1. Linear: the list is short and is searched once per reply.</summary>
    public static int IndexOf(string key)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Key == key) return i;
        return -1;
    }
}
