namespace Slender.Yealink;

public enum YealinkEventKind { HookChanged, MutePressed, FlashPressed, RejectPressed, TeamsButtonPressed }

/// <summary>Value is the new hook state for HookChanged (true = off hook) and true otherwise.</summary>
public delegate void YealinkEventSink(YealinkEventKind kind, bool value);

/// <summary>
/// Turns successive input reports into edge events. Telephony reports (the COL02 private copy) and standard reports (COL01, where
/// this base sends mute, hook and reject as telephony input report 2) are decoded through the usage slots found in each
/// collection's caps; anything with another report id (the C8 vendor channel, say) is ignored. The Teams collection sends 9B 01
/// 00 on its button. Both telephony sources feed one hook state, so the same change seen twice raises one event, and a mute,
/// flash or reject press that one source reports within <see cref="DedupMs"/> of the other's is dropped. No allocation per report.
/// </summary>
public sealed class YealinkInputDecoder
{
    public const byte TeamsReportId = 0x9B;

    /// <summary>A press the other collection reported this recently counts as the same press.</summary>
    public const int DedupMs = 150;

    private sealed class Sheet(ICollectionCaps? caps)
    {
        public readonly UsageSlot? Hook = Find(caps, YealinkUsages.HookSwitch);
        public readonly UsageSlot? Mute = Find(caps, YealinkUsages.PhoneMute);
        public readonly UsageSlot? Flash = Find(caps, YealinkUsages.Flash);
        public readonly UsageSlot? Reject = Find(caps, YealinkUsages.Reject);
        public bool MutePressed, FlashPressed, RejectPressed;
    }

    private readonly Sheet _telephony, _standard;
    private readonly Func<long> _clock;
    private readonly long[] _lastPress = new long[3];
    private readonly Sheet?[] _lastSheet = new Sheet?[3];
    private bool _hookState, _teamsPressed;

    public YealinkInputDecoder(ICollectionCaps? telephony, ICollectionCaps? standard = null, Func<long>? clock = null)
    {
        _telephony = new Sheet(telephony);
        _standard = new Sheet(standard);
        _clock = clock ?? (static () => Environment.TickCount64);
    }

    private static UsageSlot? Find(ICollectionCaps? caps, ushort usage) => caps != null && caps.TryGetInput(usage, out var s) ? s : null;

    public void DecodeTelephony(ReadOnlySpan<byte> report, YealinkEventSink sink) => Decode(_telephony, report, sink);

    /// <summary>A COL01 input report (read-only listener; nothing is ever written to that collection).</summary>
    public void DecodeStandard(ReadOnlySpan<byte> report, YealinkEventSink sink) => Decode(_standard, report, sink);

    private void Decode(Sheet sheet, ReadOnlySpan<byte> report, YealinkEventSink sink)
    {
        if (report.IsEmpty) return;

        if (sheet.Hook is { } h && Bit(report, h) is { } hook && hook != _hookState)
        {
            _hookState = hook;
            sink(YealinkEventKind.HookChanged, hook);
        }
        Edge(sheet, sheet.Mute, report, ref sheet.MutePressed, YealinkEventKind.MutePressed, 0, sink);
        Edge(sheet, sheet.Flash, report, ref sheet.FlashPressed, YealinkEventKind.FlashPressed, 1, sink);
        Edge(sheet, sheet.Reject, report, ref sheet.RejectPressed, YealinkEventKind.RejectPressed, 2, sink);
    }

    public void DecodeTeams(ReadOnlySpan<byte> report, YealinkEventSink sink)
    {
        if (report.Length < 2 || report[0] != TeamsReportId) return;
        bool down = (report[1] & 0x01) != 0;
        if (down && !_teamsPressed) sink(YealinkEventKind.TeamsButtonPressed, true);
        _teamsPressed = down;
    }

    private void Edge(Sheet sheet, UsageSlot? slot, ReadOnlySpan<byte> report, ref bool state, YealinkEventKind kind, int index, YealinkEventSink sink)
    {
        if (slot is not { } s || Bit(report, s) is not { } now) return;
        bool press = now && !state;
        state = now;
        if (!press) return;

        long at = _clock();
        bool other = _lastSheet[index] is { } last && last != sheet;
        bool repeat = other && at - _lastPress[index] < DedupMs;
        _lastSheet[index] = sheet;
        _lastPress[index] = at;
        if (!repeat) sink(kind, true);
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
