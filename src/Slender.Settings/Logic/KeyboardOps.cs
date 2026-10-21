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

    /// <summary>
    /// The Fn lock values of <paramref name="local"/> that the pending edit left as they were in <paramref name="baseline"/> are taken
    /// from <paramref name="file"/> (the tray saved Fn+Esc there); the ones the user just changed stay.
    /// </summary>
    public static KeyboardSettings AdoptFnLock(KeyboardSettings local, KeyboardSettings baseline, KeyboardSettings file) => local with
    {
        FnLock = local.FnLock == baseline.FnLock ? file.FnLock : local.FnLock,
        FnLockChannel1 = local.FnLockChannel1 == baseline.FnLockChannel1 ? file.FnLockChannel1 : local.FnLockChannel1,
        FnLockChannel2 = local.FnLockChannel2 == baseline.FnLockChannel2 ? file.FnLockChannel2 : local.FnLockChannel2,
        FnLockChannel3 = local.FnLockChannel3 == baseline.FnLockChannel3 ? file.FnLockChannel3 : local.FnLockChannel3,
    };

    /// <summary>Choice texts for a channel's Fn lock; index 0 is "leave alone" (null).</summary>
    public static readonly string[] ChannelChoices = ["Leave as it is", "F-keys first", "Media keys first"];

    public static int ChoiceIndex(bool? value) => value is null ? 0 : value.Value ? 1 : 2;

    public static bool? FromChoiceIndex(int index) => index switch { 1 => true, 2 => false, _ => null };

    /// <summary>"Channel 2 (in use now)" for the channel the keyboard is on, else "Channel 2".</summary>
    public static string ChannelTitle(int channel, int? current) =>
        current == channel ? $"Channel {channel + 1} (in use now)" : $"Channel {channel + 1}";

    /// <summary>"Channel 1 · Laptop · current" for a channel row (read-only Easy-Switch list). Unpaired slots say so; no name is skipped.</summary>
    public static string HostRowTitle(HostState host)
    {
        var parts = new List<string> { $"Channel {host.Index + 1}" };
        if (!host.Paired) parts.Add("not paired");
        else if (!string.IsNullOrWhiteSpace(host.Name)) parts.Add(host.Name!);
        if (host.Current) parts.Add("current");
        return string.Join(" · ", parts);
    }
}
