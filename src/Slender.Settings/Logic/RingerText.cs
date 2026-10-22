using System.Globalization;
using Slender.Engine.Ringer;

namespace Slender.Settings.Logic;

/// <summary>The words of the Haptics page's 3CX ringer section. Pure, so they are tested.</summary>
public static class RingerText
{
    public const string Steps =
        "1. In Edge open edge://extensions\n2. Turn on Developer mode\n3. Choose Load unpacked and pick the folder Slender opened";

    public const string AddressTip = "The web address you open the 3CX web client at, for example https://pbx.example.com (https only, no path).";

    /// <summary>"Not set up", "Set up - waiting for the first call", or the last ring signal in the user's local time.</summary>
    public static string Status(bool setUp, DateTimeOffset? lastSignal, string? source)
    {
        if (!setUp) return "Not set up";
        if (lastSignal is not { } at) return "Set up — waiting for the first call";
        string time = at.ToLocalTime().ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture);
        return $"Last ring signal: {time} ({(source == "notification" ? "notification" : "headset")})";
    }

    /// <summary>Why the address box cannot be used yet; null when it is a good address.</summary>
    public static string? AddressProblem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Type your 3CX web address first, for example https://pbx.example.com";
        return RingerOrigin.Normalize(text) is null ? "Use the address as https://your-pbx-address, with no path after it." : null;
    }

    /// <summary>What to tell the user after Set up or Remove ran.</summary>
    public static string Describe(RingerInstallResult result) => result.Message;
}
