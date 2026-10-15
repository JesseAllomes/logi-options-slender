namespace Slender.Tray;

/// <summary>Decides when a low-battery notification fires: once per discharge, re-armed by charging or a level above the threshold.</summary>
public sealed class BatteryAlertState
{
    private readonly Dictionary<string, bool> _fired = new();

    /// <summary>True when a notification should be shown for this reading.</summary>
    public bool ShouldNotify(string deviceId, int? percent, bool charging, bool connected, int threshold, bool enabled = true)
    {
        if (!connected || percent is not { } level)
        {
            return false;
        }

        if (charging || level > threshold)
        {
            _fired[deviceId] = false;
            return false;
        }

        if (!enabled || _fired.GetValueOrDefault(deviceId))
        {
            return false;
        }

        _fired[deviceId] = true;
        return true;
    }
}
