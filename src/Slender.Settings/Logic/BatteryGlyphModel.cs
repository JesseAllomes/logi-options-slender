using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>Which colour family the battery glyph uses.</summary>
public enum BatteryGlyphKind
{
    /// <summary>Discharging above the alert threshold: neutral fill.</summary>
    Normal,
    /// <summary>At or under the device's alert threshold and not charging: red fill.</summary>
    Low,
    /// <summary>On the charger and still filling: green fill and a bolt.</summary>
    Charging,
    /// <summary>On the charger and complete: full green, "Charged".</summary>
    Full,
    /// <summary>On the charger with a fault: warning outline, "Charge fault".</summary>
    Error,
    /// <summary>No level to show (not reported, asleep, disconnected): empty outline.</summary>
    Unknown,
}

/// <summary>
/// Every decision the battery glyph makes, so the control only draws. Fill is exactly proportional to the percent (no steps);
/// an unknown level is never drawn as a number.
/// </summary>
public sealed record BatteryGlyphModel(
    BatteryGlyphKind Kind,
    double FillFraction,
    bool ShowBolt,
    string Label,
    string CompactLabel,
    string AccessibleText,
    bool Dimmed)
{
    public const string Dash = "—";
    public const int HeadsetThreshold = 15;

    /// <summary>The alert threshold for a device id, from the config (the headset has none and uses 15).</summary>
    public static int ThresholdFor(string deviceId, BatteryAlertSettings alerts) => deviceId switch
    {
        DeviceIds.Mouse => alerts.MouseThresholdPercent,
        DeviceIds.Keyboard => alerts.KeyboardThresholdPercent,
        _ => HeadsetThreshold,
    };

    /// <summary>
    /// Builds the model. <paramref name="hidden"/> is a device that is greyed (disconnected, or the tray is down): the last known
    /// state is not shown, the outline is empty and dimmed. <paramref name="word"/> replaces the dash when there is no level
    /// ("Asleep", "Off").
    /// </summary>
    public static BatteryGlyphModel Create(int? percent, bool charging, string? chargeState, int threshold, bool hidden = false, string? word = null)
    {
        if (hidden)
        {
            string text = word ?? Dash;
            return new BatteryGlyphModel(BatteryGlyphKind.Unknown, 0, false, text, text, "Battery level unknown", true);
        }

        int? level = percent is { } p ? Math.Clamp(p, 0, 100) : null;
        double fraction = level is { } l ? l / 100.0 : 0;
        string state = chargeState?.Trim().ToLowerInvariant() ?? string.Empty;

        if (state == "error")
        {
            return new BatteryGlyphModel(BatteryGlyphKind.Error, fraction, false, "Charge fault", "Fault", "Battery charge fault", false);
        }

        if (state == "full" || (charging && level == 100))
        {
            return new BatteryGlyphModel(BatteryGlyphKind.Full, 1, false, "Charged", "Full", "Battery full, charging complete", false);
        }

        bool onCharger = charging || state == "charging";
        if (level is not { } known)
        {
            if (onCharger)
            {
                return new BatteryGlyphModel(BatteryGlyphKind.Charging, 0, true, Dash, Dash, "Battery level unknown, charging", false);
            }

            string text = word ?? Dash;
            return new BatteryGlyphModel(BatteryGlyphKind.Unknown, 0, false, text, text, "Battery level unknown", false);
        }

        string pct = $"{known}%";
        if (onCharger)
        {
            return new BatteryGlyphModel(BatteryGlyphKind.Charging, fraction, true, pct, pct, $"Battery {known} percent, charging", false);
        }

        return known <= threshold
            ? new BatteryGlyphModel(BatteryGlyphKind.Low, fraction, false, pct, pct, $"Battery {known} percent, low", false)
            : new BatteryGlyphModel(BatteryGlyphKind.Normal, fraction, false, pct, pct, $"Battery {known} percent", false);
    }

    /// <summary>The model for a device from the tray's status (the headset also reads its own base report).</summary>
    public static BatteryGlyphModel For(SlenderStatus? status, string deviceId, BatteryAlertSettings alerts)
    {
        DeviceState? state = status?.Devices.FirstOrDefault(d => d.Id == deviceId);
        bool hidden = state is null || DevicePresenceView.Dimmed(status, deviceId);
        int? percent = state?.BatteryPercent ?? state?.Headset?.BatteryPercent;
        bool charging = state?.Charging == true || state?.Headset?.Charging == true;
        return Create(percent, charging, state?.ChargeState, ThresholdFor(deviceId, alerts), hidden, DevicePresenceView.BatteryWord(status, deviceId));
    }

    /// <summary>True for the low-battery bar: a known level at or under the threshold, not charging.</summary>
    public static bool IsLow(DeviceState? state, int threshold) =>
        state is { BatteryPercent: { } p, Charging: false } && p <= threshold;
}
