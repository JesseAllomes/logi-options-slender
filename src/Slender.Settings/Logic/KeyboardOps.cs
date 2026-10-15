using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Settings.Logic;

/// <summary>Pure edits behind the keyboard rows of Settings (kept apart from the window so they are testable).</summary>
public static class KeyboardOps
{
    /// <summary>The five keys the K860 can switch off (0x4521), with the text shown in Settings.</summary>
    public static readonly (DisabledKeys Key, string Name)[] DisableChoices =
    [
        (DisabledKeys.CapsLock, "Caps Lock"),
        (DisabledKeys.NumLock, "Num Lock"),
        (DisabledKeys.ScrollLock, "Scroll Lock"),
        (DisabledKeys.Insert, "Insert"),
        (DisabledKeys.Windows, "Windows key"),
    ];

    public static bool IsDisabled(KeyboardSettings k, DisabledKeys key) => ((DisabledKeys)k.DisabledKeys & key) != 0;

    public static KeyboardSettings SetDisabled(KeyboardSettings k, DisabledKeys key, bool off)
    {
        var mask = (DisabledKeys)k.DisabledKeys;
        mask = off ? mask | key : mask & ~key;
        return k with { DisabledKeys = (byte)(mask & DisabledKeys.All) };
    }

    /// <summary>Choice texts for a channel's Fn lock; index 0 is "leave alone" (null).</summary>
    public static readonly string[] ChannelChoices = ["Leave as it is", "F-keys first", "Media keys first"];

    public static int ChoiceIndex(bool? value) => value is null ? 0 : value.Value ? 1 : 2;

    public static bool? FromChoiceIndex(int index) => index switch { 1 => true, 2 => false, _ => null };

    /// <summary>"Channel 2 (in use now)" for the channel the keyboard is on, else "Channel 2".</summary>
    public static string ChannelTitle(int channel, int? current) =>
        current == channel ? $"Channel {channel + 1} (in use now)" : $"Channel {channel + 1}";
}
