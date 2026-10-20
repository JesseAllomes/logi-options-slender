using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>The battery line Settings shows for a device: level, charging and the days-left estimate the tray published.</summary>
public static class BatteryLine
{
    public static string Text(DeviceState? state)
    {
        if (state is not { Connected: true }) return "Not connected";
        if (state.BatteryPercent is not { } percent) return state.BatteryText ?? "Level not reported";
        string head = state.Charging ? $"{percent}% · charging" : $"{percent}%";
        return string.IsNullOrWhiteSpace(state.BatteryEstimate) || state.Charging ? head : $"{head} · {state.BatteryEstimate}";
    }
}
