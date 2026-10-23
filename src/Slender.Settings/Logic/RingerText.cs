using System.Globalization;
using Slender.Engine;
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
        bool fromBase = source == "base" && lastSignal is not null; // the base needs no extension, so it counts even when not set up
        if (!setUp && !fromBase) return "Not set up";
        if (lastSignal is not { } at) return "Set up — waiting for the first call";
        string time = at.ToLocalTime().ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture);
        return $"Last ring signal: {time} ({source switch { "notification" => "notification", "base" => "headset base", _ => "headset" }})";
    }

    public const string SourceHelp = "The headset base works with any phone app and needs no setup. 3CX in Edge needs the extension set up below.";

    /// <summary>The three choices for what starts the buzz, in RingSource order of the picker: base, Edge extension, either.</summary>
    public static readonly string[] SourceChoices = ["The headset base rings", "3CX rings in Edge", "Either one"];

    public static int SourceIndex(CallRingSource source) => source switch { CallRingSource.Base => 0, CallRingSource.Extension => 1, _ => 2 };

    public static CallRingSource SourceAt(int index) => index switch { 0 => CallRingSource.Base, 1 => CallRingSource.Extension, _ => CallRingSource.Either };

    public const string RingButtonsHelp = "While a 3CX call is ringing, this mouse button answers or rejects it from any app. Off leaves the button alone. "
        + "Outside a ringing call the button does what it always does. Needs the Ringer buzz on.";

    /// <summary>"Off", then the mouse buttons that can answer or reject a call, named as on the Mouse page.</summary>
    public static List<string> RingButtonChoices() =>
        ["Off", .. RingButtons.Choices.Select(id => ControlCatalog.MouseControls.First(c => c.Id == id).Name)];

    /// <summary>The picker row for a stored button id (0 or anything unknown = Off).</summary>
    public static int RingButtonIndex(ushort id)
    {
        int at = RingButtons.Choices.ToList().IndexOf(id);
        return at < 0 ? 0 : at + 1;
    }

    /// <summary>The button id for a picker row (0 = Off).</summary>
    public static ushort RingButtonAt(int index) => index >= 1 && index <= RingButtons.Choices.Count ? RingButtons.Choices[index - 1] : (ushort)0;

    /// <summary>Why the address box cannot be used yet; null when it is a good address.</summary>
    public static string? AddressProblem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Type your 3CX web address first, for example https://pbx.example.com";
        return RingerOrigin.Normalize(text) is null ? "Use the address as https://your-pbx-address, with no path after it." : null;
    }

    /// <summary>What to tell the user after Set up or Remove ran.</summary>
    public static string Describe(RingerInstallResult result) => result.Message;
}
