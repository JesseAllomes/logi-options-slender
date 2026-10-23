using Slender.Engine.Ringer;

namespace Slender.Settings.Logic;

/// <summary>The words and device-id handling of the 3CX page's Ring sound section. Pure, so they are tested.</summary>
public static class RingSoundText
{
    public const string SoundHelp = "Plays while a call rings, until you answer or it stops ringing.";

    public const string DeviceHelp = "The headset only plays PC sound while it is off the base.";

    public const string HeadsetChoice = "Headset (Yealink WH64), if connected";

    public const string MissingDevice = "Unavailable playback device";

    /// <summary>"Off", then the built-in sounds by display name.</summary>
    public static List<string> SoundChoices() => ["Off", .. RingSounds.All.Select(s => s.Name)];

    public static int SoundIndex(string? id) => RingSounds.Find(id) is { } s ? RingSounds.All.ToList().IndexOf(s) + 1 : 0;

    public static string? SoundAt(int index) => index >= 1 && index <= RingSounds.All.Count ? RingSounds.All[index - 1].Id : null;

    /// <summary>
    /// The Core Audio endpoint id ("{0.0.0.00000000}.{guid}") inside a WinRT device id
    /// (\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{interface guid}), which is the form IMMDeviceEnumerator.GetDevice takes. An id already in
    /// the Core Audio form is returned as it is; anything else gives null.
    /// </summary>
    public static string? ToCoreAudioId(string? winRtId)
    {
        if (string.IsNullOrWhiteSpace(winRtId)) return null;
        string id = winRtId.Trim();
        const string marker = "#MMDEVAPI#";
        int at = id.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            id = id[(at + marker.Length)..];
            int end = id.IndexOf('#');
            if (end >= 0) id = id[..end];
        }

        return IsCoreAudioId(id) ? id : null;
    }

    private static bool IsCoreAudioId(string id) =>
        id.StartsWith("{0.0.", StringComparison.Ordinal) && id.Contains("}.{", StringComparison.Ordinal) && id.EndsWith('}')
        && id.IndexOfAny(['#', '\\', '?']) < 0;
}
