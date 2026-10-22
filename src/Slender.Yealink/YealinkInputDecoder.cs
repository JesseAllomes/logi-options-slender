namespace Slender.Yealink;

public enum YealinkEventKind { HookChanged, MutePressed, FlashPressed, RejectPressed, TeamsButtonPressed }

/// <summary>Value is the new hook state for HookChanged (true = off hook) and true otherwise.</summary>
public delegate void YealinkEventSink(YealinkEventKind kind, bool value);

/// <summary>
/// Turns successive input reports into edge events. Telephony reports (the COL02 private copy) are decoded through the usage
/// slots found in the caps; anything with another report id (the C8 vendor channel, say) is ignored. The Teams collection
/// sends 9B 01 00 on its button. No allocation per report.
/// </summary>
public sealed class YealinkInputDecoder
{
    public const byte TeamsReportId = 0x9B;

    private readonly UsageSlot? _hook, _mute, _flash, _reject;
    private bool _hookState, _mutePressed, _flashPressed, _rejectPressed, _teamsPressed;

    public YealinkInputDecoder(ICollectionCaps? telephony)
    {
        if (telephony == null) return;
        _hook = Find(telephony, YealinkUsages.HookSwitch);
        _mute = Find(telephony, YealinkUsages.PhoneMute);
        _flash = Find(telephony, YealinkUsages.Flash);
        _reject = Find(telephony, YealinkUsages.Reject);
    }

    private static UsageSlot? Find(ICollectionCaps caps, ushort usage) => caps.TryGetInput(usage, out var s) ? s : null;

    public void DecodeTelephony(ReadOnlySpan<byte> report, YealinkEventSink sink)
    {
        if (report.IsEmpty) return;

        if (_hook is { } h && Bit(report, h) is { } hook && hook != _hookState)
        {
            _hookState = hook;
            sink(YealinkEventKind.HookChanged, hook);
        }
        Edge(report, _mute, ref _mutePressed, YealinkEventKind.MutePressed, sink);
        Edge(report, _flash, ref _flashPressed, YealinkEventKind.FlashPressed, sink);
        Edge(report, _reject, ref _rejectPressed, YealinkEventKind.RejectPressed, sink);
    }

    public void DecodeTeams(ReadOnlySpan<byte> report, YealinkEventSink sink)
    {
        if (report.Length < 2 || report[0] != TeamsReportId) return;
        bool down = (report[1] & 0x01) != 0;
        if (down && !_teamsPressed) sink(YealinkEventKind.TeamsButtonPressed, true);
        _teamsPressed = down;
    }

    private static void Edge(ReadOnlySpan<byte> report, UsageSlot? slot, ref bool state, YealinkEventKind kind, YealinkEventSink sink)
    {
        if (slot is not { } s || Bit(report, s) is not { } now) return;
        if (now && !state) sink(kind, true);
        state = now;
    }

    /// <summary>The usage's bit, or null when the report is another report id or too short to hold it.</summary>
    private static bool? Bit(ReadOnlySpan<byte> report, UsageSlot slot)
    {
        if (report[0] != slot.ReportId) return null;
        int index = slot.Bit >> 3;
        if (index >= report.Length) return null;
        return (report[index] >> (slot.Bit & 7) & 1) != 0;
    }
}
