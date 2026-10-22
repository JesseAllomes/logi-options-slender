using Slender.Engine;

namespace Slender.Engine.Devices;

/// <summary>
/// Remove device (m53): the config with one device's Slender settings back to defaults. Pure. App-level settings and anything
/// shared (Ring, Excel, Macros, Sync, MyShortcuts, LaunchAtLogin) are never touched: the keyboard can open the ring too.
/// </summary>
public static class DeviceSettingsReset
{
    public static SlenderConfig Forget(SlenderConfig config, string deviceId)
    {
        var d = SlenderConfig.Default;
        switch (deviceId)
        {
            case DeviceCatalog.MxMaster4:
                return config with
                {
                    Pointer = d.Pointer,
                    Haptics = d.Haptics,
                    BatteryAlerts = config.BatteryAlerts with { MouseThresholdPercent = d.BatteryAlerts.MouseThresholdPercent },
                    Profiles = Reset(config.Profiles, p => p with
                    {
                        Bindings = new Dictionary<ushort, ActionSpec>(),
                        GestureSwipes = new Dictionary<GestureDirection, ActionSpec>(),
                        ThumbWheelAlt = null,
                        SideScrollSpeed = null,
                        WheelSpeed = null,
                        ThumbFlick = null,
                        Dpi = null,
                    }),
                };
            case DeviceCatalog.ErgoK860:
                return config with
                {
                    Keyboard = d.Keyboard,
                    OnboardRemaps = d.OnboardRemaps,
                    BatteryAlerts = config.BatteryAlerts with { KeyboardThresholdPercent = d.BatteryAlerts.KeyboardThresholdPercent },
                    Profiles = Reset(config.Profiles, p => p with { KeyBindings = new Dictionary<ushort, ActionSpec>() }),
                };
            default:
                return config;
        }
    }

    /// <summary>
    /// Applies the reset to each profile; a per-app profile the reset itself left empty is dropped (one that was already empty is the
    /// user's own and stays), and the default profile never is.
    /// </summary>
    private static IReadOnlyList<Profile> Reset(IReadOnlyList<Profile> profiles, Func<Profile, Profile> reset)
    {
        var result = new List<Profile>(profiles.Count);
        foreach (Profile old in profiles)
        {
            Profile next = reset(old);
            if (old.AppProcess is not null && !IsEmpty(old) && IsEmpty(next)) continue;
            result.Add(next);
        }

        return result;
    }

    private static bool IsEmpty(Profile p) =>
        p.Bindings.Count == 0 && p.KeyBindings.Count == 0 && p.GestureSwipes.Count == 0 && p.RingSlots is null
        && p.ThumbWheelAlt is null && p.SideScrollSpeed is null && p.WheelSpeed is null && p.ThumbFlick is null && p.Dpi is null;
}
