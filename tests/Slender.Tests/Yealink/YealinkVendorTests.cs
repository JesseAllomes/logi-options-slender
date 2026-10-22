using System.Text;
using Slender.Yealink;
using Slender.Engine.Devices;

namespace Slender.Tests.Yealink;

/// <summary>
/// Vendor-channel vectors. Every message is made up in the shape the research notes describe (values, names and ids are not from
/// any real unit); stale padding after each chunk is filled with 0x5A to prove it is ignored.
/// </summary>
internal static class VendorVectors
{
    public const byte Stale = 0x5A;

    public static List<byte[]> Fragment(byte stream, byte[] message)
    {
        var frames = new List<byte[]>();
        int count = Math.Max(1, (message.Length + VendorFrame.MaxChunk - 1) / VendorFrame.MaxChunk);
        for (int i = 0; i < count; i++)
        {
            var frame = new byte[VendorFrame.Length];
            Array.Fill(frame, Stale);
            int offset = i * VendorFrame.MaxChunk, len = Math.Min(VendorFrame.MaxChunk, message.Length - offset);
            byte pos = count == 1 ? VendorFrame.Single : i == 0 ? VendorFrame.First : i == count - 1 ? VendorFrame.Last : VendorFrame.Middle;
            frame[0] = VendorFrame.ReportId;
            frame[1] = (byte)(stream | pos);
            frame[2] = (byte)len;
            frame[3] = (byte)(i * 4);
            Array.Copy(message, offset, frame, 4, len);
            frames.Add(frame);
        }
        return frames;
    }

    public static byte[] HelloReply()
    {
        var m = new byte[38];
        m[1] = 0x20;
        Encoding.ASCII.GetBytes(VendorRequests.HelloText, m.AsSpan(6));
        return m;
    }

    public static byte[] SimpleReply(byte cmd) => [cmd, 0, 0, 0, 0, 0];

    public static byte[] EnvShort(string key, string? value, byte cmd = 0x03)
    {
        var m = new byte[60];
        m[0] = cmd;
        if (value == null) return [cmd, 0, 0, 0, 0, 0];
        m[1] = 0x36;
        Encoding.ASCII.GetBytes(key, m.AsSpan(6, 16));
        Encoding.ASCII.GetBytes(value, m.AsSpan(22));
        return m;
    }

    public static byte[] EnvLong(string key, string value, byte cmd = 0x22)
    {
        var m = new byte[6 + key.Length + value.Length];
        m[0] = cmd;
        m[1] = (byte)(key.Length + 1);
        m[2] = (byte)key.Length;
        m[4] = (byte)value.Length;
        Encoding.ASCII.GetBytes(key, m.AsSpan(6));
        Encoding.ASCII.GetBytes(value, m.AsSpan(6 + key.Length));
        return m;
    }

    /// <summary>A 475-byte GetDect reply with a made-up base block (zeros) and handset 1 in the first slot.</summary>
    public static byte[] Dect(bool registered, byte signal, byte level, byte state, bool docked, bool alive, string? model = null)
    {
        var m = new byte[475];
        m[0] = 0x79;
        new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }.CopyTo(m, 10);
        int hs = 131;
        m[hs] = (byte)(registered ? 1 : 0);
        m[hs + 1] = 2;
        m[hs + 2] = signal;
        m[hs + 3] = level;
        m[hs + 4] = state;
        m[hs + 5] = (byte)(docked ? 1 : 0);
        m[hs + 6] = (byte)(alive ? 1 : 0);
        if (model != null) Encoding.ASCII.GetBytes(model, m.AsSpan(hs + 44, 32));
        return m;
    }

    /// <summary>What a base would answer to one of our requests (the made-up unit in the tests); null for a request it does not know.</summary>
    public static byte[]? ReplyFor(byte[] request, IReadOnlyDictionary<string, string> env) => request[4] switch
    {
        0x00 => HelloReply(),
        0x01 => SimpleReply(0x01),
        0x79 => Dect(true, 7, 100, 2, true, true),
        0x03 => EnvReply(Encoding.ASCII.GetString(request, 10, 16).TrimEnd('\0'), env),
        0x22 => EnvReply(Encoding.ASCII.GetString(request, 10, request[6]), env),
        _ => null,
    };

    private static byte[] EnvReply(string key, IReadOnlyDictionary<string, string> env) =>
        env.TryGetValue(key, out var v) ? (key.Length <= 16 ? EnvShort(key, v) : EnvLong(key, v)) : (key.Length <= 16 ? EnvShort(key, null) : EnvLong(key, ""));

    /// <summary>A DECT-channel notification: ch 0D, 00, length 6, then event, handset, a, b, 00, 00.</summary>
    public static byte[] DectEvent(byte ev, byte handset, byte a, byte b = 0) => [0x0D, 0x00, 0x06, 0x00, ev, handset, a, b, 0, 0];

    public static byte[] LogLine(string text)
    {
        var body = Encoding.ASCII.GetBytes(text);
        var m = new byte[4 + 2 + body.Length];
        m[0] = 0x1F;
        m[2] = (byte)(2 + body.Length);
        m[4] = 0xC0;
        body.CopyTo(m, 6);
        return m;
    }
}

public class YealinkVendorCodecTests
{
    private static byte[] Build(byte cmd, string arg = "")
    {
        var frame = new byte[VendorFrame.Length];
        VendorRequests.Build(cmd, arg, frame);
        return frame;
    }

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    [Fact]
    public void Hello_request_is_the_sdk_frame()
    {
        // C8 03 26 00 | 00 20 00 00 00 00 | "hello RC8-MCU device!" | zeros
        var expected = new byte[64];
        Hex("C8 03 26 00 00 20 00 00 00 00").CopyTo(expected, 0);
        Encoding.ASCII.GetBytes("hello RC8-MCU device!").CopyTo(expected, 10);
        Assert.Equal(expected, Build((byte)VendorCommand.Hello));
    }

    [Fact]
    public void Header_only_requests_have_a_six_byte_body()
    {
        var expected = new byte[64];
        Hex("C8 03 06 00 01 00 00 00 00 00").CopyTo(expected, 0);
        Assert.Equal(expected, Build((byte)VendorCommand.GetVersion));
        expected[4] = 0x79;
        Assert.Equal(expected, Build((byte)VendorCommand.GetDect));
    }

    [Fact]
    public void Short_env_get_is_the_sdk_frame_for_mac()
    {
        var expected = new byte[64];
        Hex("C8 03 3C 00 03 36 00 00 00 00 6D 61 63").CopyTo(expected, 0);
        Assert.Equal(expected, Build((byte)VendorCommand.EnvGet, "mac"));
    }

    [Fact]
    public void Long_env_get_carries_the_name_length_and_the_name()
    {
        var f = Build((byte)VendorCommand.EnvGetLong, "auto_answer.enable"); // 18 characters
        Assert.Equal(Hex("C8 03 19 00 22 13 12 00 00 00"), f[..10]);
        Assert.Equal("auto_answer.enable", Encoding.ASCII.GetString(f, 10, 18));
        Assert.All(f[28..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Env_get_picks_the_form_by_key_length()
    {
        var frame = new byte[64];
        VendorRequests.BuildEnvGet("syslog.log_level", frame); // exactly 16
        Assert.Equal(0x03, frame[4]);
        VendorRequests.BuildEnvGet("syslog.log_level2", frame); // 17
        Assert.Equal(0x22, frame[4]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this.key.is.far.too.long.for.a.single.request.report.at.all.really")]
    [InlineData("bad\nkey")]
    public void Bad_keys_are_refused(string key)
    {
        var frame = new byte[64];
        Assert.Throws<ArgumentException>(() => VendorRequests.BuildEnvGet(key, frame));
    }

    [Fact]
    public void A_short_key_cannot_use_the_long_command_and_a_short_buffer_is_refused()
    {
        var frame = new byte[64];
        Assert.Throws<ArgumentException>(() => VendorRequests.Build((byte)VendorCommand.EnvGetLong, "mac", frame));
        Assert.Throws<ArgumentException>(() => VendorRequests.Build((byte)VendorCommand.Hello, "", new byte[32]));
    }

    [Fact]
    public void Only_the_five_allowlisted_commands_can_be_built()
    {
        var allowed = new HashSet<byte> { 0x00, 0x01, 0x03, 0x22, 0x79 };
        for (int id = 0; id <= 255; id++)
        {
            var frame = new byte[64];
            if (allowed.Contains((byte)id)) Assert.True(VendorRequests.IsAllowed((byte)id));
            else
            {
                Assert.False(VendorRequests.IsAllowed((byte)id));
                Assert.Throws<ArgumentOutOfRangeException>(() => VendorRequests.Build((byte)id, "mac", frame));
                Assert.All(frame, b => Assert.Equal(0, b)); // nothing was written into the buffer
            }
        }
    }

    [Theory]
    [InlineData(0x296, "Poweroff")]
    [InlineData(0x2CB, "ResetEx")]
    [InlineData(0x207, "EnvSet")]
    [InlineData(0x27E, "EnvSetMini")]
    [InlineData(0x240, "EnvSetLarge")]
    [InlineData(0x28A, "RccSetEnable")]
    [InlineData(0x2C5, "DeviceLedCtrl")]
    [InlineData(0x22E, "SetVirtualKey")]
    public void Dangerous_and_writing_commands_cannot_be_built_by_any_id_form(int rc8Id, string name)
    {
        // The 16-bit id itself, its low byte, and the C8 frame byte for the softphone family (id - 0x249) are all refused.
        var frame = new byte[64];
        byte low = (byte)rc8Id, derived = (byte)(rc8Id - 0x249);
        Assert.False(VendorRequests.IsAllowed(low), name);
        Assert.False(VendorRequests.IsAllowed(derived), name);
        Assert.Throws<ArgumentOutOfRangeException>(() => VendorRequests.Build(low, "", frame));
        Assert.Throws<ArgumentOutOfRangeException>(() => VendorRequests.Build(derived, "", frame));
    }

    [Theory]
    [InlineData(0x37)] // app registration
    [InlineData(0x3A)] // call id allocation
    [InlineData(0x3C)] // call sync
    [InlineData(0x3D)]
    [InlineData(0x38)] // session release
    [InlineData(0x43)]
    [InlineData(0x1F)] // GetSettings bitmap: not needed
    [InlineData(0x7A)] // SetDevTime
    [InlineData(0x41)] // SetRccStdMode
    public void Other_ids_seen_on_the_wire_are_not_sent(int id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build((byte)id));
    }

    // ---- reassembly ----

    private static List<(byte Stream, byte[] Message)> Feed(params byte[][] reports)
    {
        var got = new List<(byte, byte[])>();
        var r = new VendorReassembler();
        foreach (var report in reports) r.Feed(report, (s, m) => got.Add((s, m.ToArray())));
        return got;
    }

    [Fact]
    public void A_single_frame_message_is_delivered_without_the_padding()
    {
        var msg = VendorVectors.HelloReply();
        var frames = VendorVectors.Fragment(VendorFrame.StreamCommand, msg);
        Assert.Single(frames);
        Assert.Equal(msg, Feed(frames[0]).Single().Message);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(475)]
    [InlineData(500)]
    public void Multi_fragment_messages_reassemble(int size)
    {
        var msg = new byte[size];
        for (int i = 0; i < size; i++) msg[i] = (byte)(i * 7 + 1);
        var frames = VendorVectors.Fragment(VendorFrame.StreamCommand, msg);
        Assert.True(frames.Count >= 2);
        var got = Feed([.. frames]).Single();
        Assert.Equal(msg, got.Message);
    }

    [Fact]
    public void A_gap_or_missing_first_fragment_drops_the_message_and_the_next_one_still_works()
    {
        var big = new byte[200];
        var frames = VendorVectors.Fragment(VendorFrame.StreamCommand, big);
        var ok = VendorVectors.HelloReply();
        var got = Feed(frames[0], frames[2], frames[3], // out of order: seq 8 after 0
            frames[1], frames[3],                       // no open message
            VendorVectors.Fragment(VendorFrame.StreamCommand, ok)[0]);
        Assert.Equal(ok, got.Single().Message);
    }

    [Fact]
    public void Too_big_a_message_is_dropped()
    {
        var huge = new byte[VendorReassembler.Capacity + 100];
        Assert.Empty(Feed([.. VendorVectors.Fragment(VendorFrame.StreamCommand, huge)]));
    }

    [Fact]
    public void Bad_reports_are_ignored()
    {
        var f = VendorVectors.Fragment(VendorFrame.StreamCommand, VendorVectors.HelloReply())[0];
        var wrongId = (byte[])f.Clone(); wrongId[0] = 0x04;
        var tooLong = (byte[])f.Clone(); tooLong[2] = 0x3D;
        Assert.Empty(Feed(wrongId, tooLong, f[..3], []));
    }

    [Fact]
    public void A_notification_between_command_fragments_does_not_break_the_command()
    {
        var cmd = new byte[130];
        cmd[0] = 0x79;
        var frames = VendorVectors.Fragment(VendorFrame.StreamCommand, cmd);
        var note = VendorVectors.Fragment(VendorFrame.StreamNotify, VendorVectors.DectEvent(0x09, 1, 50, 2))[0];
        var got = Feed(frames[0], note, frames[1], frames[2]);
        Assert.Equal([VendorFrame.StreamNotify, VendorFrame.StreamCommand], got.Select(g => g.Stream));
        Assert.Equal(cmd, got[1].Message);
    }

    [Fact]
    public void A_long_notification_is_delivered_once_from_its_first_fragment()
    {
        var log = VendorVectors.LogLine(new string('x', 400));
        var frames = VendorVectors.Fragment(VendorFrame.StreamNotify, log);
        Assert.True(frames.Count > 3);
        var got = Feed([.. frames]);
        Assert.Single(got);
        Assert.Equal(0x1F, got[0].Message[0]);
        Assert.Equal(VendorFrame.MaxChunk, got[0].Message.Length);
    }
}

public class YealinkVendorDecoderTests
{
    private readonly VendorDecoder _d = new();
    private readonly List<HeadsetStatus> _changes = [];
    private readonly List<(byte Cmd, string? Key, string? Value)> _replies = [];

    public YealinkVendorDecoderTests()
    {
        _d.StatusChanged += _changes.Add;
        _d.Reply += (c, k, v) => _replies.Add((c, k, v));
    }

    private void Cmd(byte[] message) => _d.Message(VendorFrame.StreamCommand, message);

    private void Note(byte[] message) => _d.Message(VendorFrame.StreamNotify, message);

    [Fact]
    public void Hello_and_version_replies_are_reported_as_commands()
    {
        Cmd(VendorVectors.HelloReply());
        Cmd(VendorVectors.SimpleReply(0x01));
        Assert.Equal([((byte)0, (string?)null, (string?)null), (1, null, null)], _replies);
    }

    [Fact]
    public void Short_env_reply_has_the_key_and_value()
    {
        Cmd(VendorVectors.EnvShort("call.priority", "2"));
        Assert.Equal(((byte)0x03, (string?)"call.priority", (string?)"2"), _replies.Single());
    }

    [Fact]
    public void A_sixteen_character_key_has_no_terminator()
    {
        Cmd(VendorVectors.EnvShort("syslog.log_level", "4"));
        Assert.Equal("syslog.log_level", _replies.Single().Key);
        Assert.Equal("4", _replies.Single().Value);
    }

    [Fact]
    public void An_empty_short_reply_means_the_key_is_not_set()
    {
        Cmd([0x03, 0, 0, 0, 0, 0]);
        Assert.Equal(((byte)0x03, (string?)null, (string?)null), _replies.Single());
    }

    [Fact]
    public void Long_env_reply_has_the_key_and_value_without_terminators()
    {
        Cmd(VendorVectors.EnvLong("auto_answer.enable", "0"));
        Assert.Equal(((byte)0x22, (string?)"auto_answer.enable", (string?)"0"), _replies.Single());
    }

    [Fact]
    public void A_truncated_long_reply_is_ignored()
    {
        var m = VendorVectors.EnvLong("auto_answer.enable", "0");
        Cmd(m[..^3]);
        Assert.Empty(_replies);
    }

    [Fact]
    public void Dect_reply_gives_registered_battery_signal_docked_and_range()
    {
        Cmd(VendorVectors.Dect(registered: true, signal: 5, level: 83, state: 0, docked: false, alive: true));
        Assert.Equal(new HeadsetStatus(true, 83, false, false, 5, true), _d.Status);

        Cmd(VendorVectors.Dect(true, 7, 100, 2, true, true));
        Assert.Equal(new HeadsetStatus(true, 100, true, true, 7, true), _d.Status);
        Assert.Equal(2, _changes.Count);
    }

    [Fact]
    public void Dect_reply_carries_the_handset_model_name_and_events_keep_it()
    {
        Cmd(VendorVectors.Dect(true, 5, 83, 0, false, true, model: "Example Headset Dual"));
        Assert.Equal("Example Headset Dual", _d.Status.Model);
        Note(VendorVectors.DectEvent(0x09, 1, 80, 0));
        Assert.Equal("Example Headset Dual", _d.Status.Model);
        Assert.Equal(80, _d.Status.BatteryPercent);
    }

    [Fact]
    public void An_unregistered_handset_clears_the_rest()
    {
        Cmd(VendorVectors.Dect(true, 5, 50, 0, false, true));
        Cmd(VendorVectors.Dect(false, 0, 0, 0, false, false));
        Assert.Equal(new HeadsetStatus(false, null, null, null, null, null), _d.Status);
    }

    [Fact]
    public void A_short_dect_reply_is_ignored()
    {
        Cmd(VendorVectors.Dect(true, 5, 50, 0, false, true)[..100]);
        Assert.Empty(_changes);
    }

    [Fact]
    public void Battery_event_sets_percent_and_charging()
    {
        Note(VendorVectors.DectEvent(0x09, 1, 85, 2));
        Assert.Equal(new HeadsetStatus(true, 85, true, true, null, null), _d.Status);
        Note(VendorVectors.DectEvent(0x09, 1, 84, 0));
        Assert.Equal(new HeadsetStatus(true, 84, false, false, null, null), _d.Status);
    }

    [Fact]
    public void Signal_event_sets_signal_and_range()
    {
        Note(VendorVectors.DectEvent(0x04, 1, 7));
        Assert.Equal(7, _d.Status.Signal);
        Assert.True(_d.Status.InRange);
        Note(VendorVectors.DectEvent(0x04, 1, 0));
        Assert.False(_d.Status.InRange);
    }

    [Fact]
    public void A_repeated_event_raises_nothing_new()
    {
        Note(VendorVectors.DectEvent(0x09, 1, 85, 2));
        Note(VendorVectors.DectEvent(0x09, 1, 85, 2));
        Assert.Single(_changes);
    }

    [Fact]
    public void Other_handsets_bad_levels_unknown_events_and_the_debug_log_change_nothing()
    {
        Note(VendorVectors.DectEvent(0x09, 2, 85, 2));
        Note(VendorVectors.DectEvent(0x09, 1, 101, 2));
        Note(VendorVectors.DectEvent(0x77, 1, 5));
        Note(VendorVectors.LogLine("Hs:1,MiddleVBat:4338,batlevel:100%,batst:(2)"));
        Note([0x0D, 0, 6, 0]);
        Assert.Empty(_changes);
        Assert.Empty(_replies);
    }

    [Fact]
    public void Reassembled_frames_decode_end_to_end()
    {
        var r = new VendorReassembler();
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamCommand, VendorVectors.Dect(true, 6, 60, 0, false, true)))
            r.Feed(f, _d.Message);
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamNotify, VendorVectors.DectEvent(0x09, 1, 59, 0)))
            r.Feed(f, _d.Message);
        Assert.Equal(59, _d.Status.BatteryPercent);
        Assert.Equal(6, _d.Status.Signal);
    }
}

public class YealinkYealinkSettingsCatalogTests
{
    [Fact]
    public void Keys_are_unique_and_every_label_and_group_is_filled()
    {
        Assert.Equal(YealinkSettingsCatalog.All.Count, YealinkSettingsCatalog.All.Select(d => d.Key).Distinct().Count());
        Assert.All(YealinkSettingsCatalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Label));
            Assert.False(string.IsNullOrWhiteSpace(d.Group));
            Assert.True(d.Values is null || d.Values.Length % 2 == 0);
        });
        Assert.Equal(["Sound", "Prompts and mute reminder", "Ringing", "Calls", "Busylight", "Hearing protection", "Bluetooth and sleep"],
            YealinkSettingsCatalog.All.Where(d => d.Target == YealinkTarget.Headset).Select(d => d.Group).Distinct());
        Assert.Equal(["Base ringer", "Calls", "Radio", "Shared headsets", "Plug-in busylight"],
            YealinkSettingsCatalog.All.Where(d => d.Target == YealinkTarget.Base).Select(d => d.Group).Distinct());
        foreach (YealinkTarget page in Enum.GetValues<YealinkTarget>())
        {
            var labels = YealinkSettingsCatalog.All.Where(d => d.Target == page).Select(d => d.Label).ToList();
            Assert.Equal(labels.Count, labels.Distinct().Count());
        }
        Assert.All(YealinkSettingsCatalog.All.Where(d => d.ShowWhenKey is not null), d => Assert.True(YealinkSettingsCatalog.IndexOf(d.ShowWhenKey) >= 0));
    }

    [Fact]
    public void Values_map_to_text_and_unknown_ones_stay_raw()
    {
        var range = YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("wireless.range")];
        Assert.Equal("Long (150 m)", range.Text("1"));
        Assert.Equal("9", range.Text("9"));
        Assert.Equal("Off", YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("keypad_tone.enable")].Text("0"));
        Assert.Equal("3", YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf("sidetone.level")].Text("3"));
        Assert.Equal(-1, YealinkSettingsCatalog.IndexOf("no.such.key"));
    }

    [Fact]
    public void No_catalog_key_is_longer_than_a_request_can_carry()
    {
        var frame = new byte[64];
        Assert.All(YealinkSettingsCatalog.All, d => VendorRequests.BuildEnvGet(d.Key, frame));
    }
}

public class YealinkVendorSessionTests
{
    private readonly List<byte[]> _sent = [];
    private readonly List<string> _log = [];
    private YealinkVendorSession _s = null!;
    private bool _writable = true;
    private readonly Dictionary<string, string?> _device = new()
    {
        ["wireless.range"] = "1",
        ["call.priority"] = "2",
        ["auto_answer.enable"] = "0",
        ["language.prompt"] = "0",
    };

    private YealinkVendorSession Make()
    {
        _s = new YealinkVendorSession(r => { if (!_writable) return false; _sent.Add(r.ToArray()); return true; }, _log.Add);
        return _s;
    }

    private static string Label(byte[] request) => request[4] switch
    {
        0x03 => Encoding.ASCII.GetString(request, 10, 16).TrimEnd('\0'),
        0x22 => Encoding.ASCII.GetString(request, 10, request[6]),
        _ => $"cmd{request[4]:X2}",
    };

    private void Reply(byte[] message)
    {
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamCommand, message)) _s.OnReport(f);
    }

    /// <summary>Answers the request in flight as the base would.</summary>
    private void Answer()
    {
        var req = _sent[^1];
        switch (req[4])
        {
            case 0x00: Reply(VendorVectors.HelloReply()); break;
            case 0x01: Reply(VendorVectors.SimpleReply(0x01)); break;
            case 0x79: Reply(VendorVectors.Dect(true, 7, 100, 2, true, true)); break;
            case 0x03:
            {
                string key = Label(req);
                Reply(_device.TryGetValue(key, out var v) && v != null ? VendorVectors.EnvShort(key, v) : [0x03, 0, 0, 0, 0, 0]);
                break;
            }
            case 0x22:
            {
                string key = Label(req);
                Reply(_device.TryGetValue(key, out var v) && v != null ? VendorVectors.EnvLong(key, v) : VendorVectors.EnvLong(key, ""));
                break;
            }
        }
    }

    private void AnswerAll()
    {
        int guard = 0;
        while (_s.IsRunning && guard++ < 200)
        {
            int before = _sent.Count;
            Answer();
            if (_sent.Count == before && !_s.IsRunning) break;
        }
    }

    [Fact]
    public void Nothing_is_sent_until_a_pass_begins()
    {
        Make();
        Assert.Empty(_sent);
        Assert.False(_s.IsRunning);
    }

    [Fact]
    public void A_full_pass_runs_hello_version_dect_then_every_catalog_key_in_order_one_at_a_time()
    {
        Make();
        Assert.True(_s.Begin(VendorRead.All));
        Assert.Single(_sent);
        Assert.Equal(0x00, _sent[0][4]);

        AnswerAll();

        Assert.False(_s.IsRunning);
        var labels = _sent.Select(Label).ToList();
        Assert.Equal(["cmd00", "cmd01", "cmd79"], labels.Take(3));
        Assert.Equal(YealinkSettingsCatalog.All.Select(d => d.Key), labels.Skip(3));
        Assert.All(_sent, r => Assert.True(VendorRequests.IsAllowed(r[4])));
        Assert.All(_sent, r => Assert.Equal(VendorFrame.Length, r.Length));
    }

    [Fact]
    public void The_pass_fills_status_and_settings_with_value_text()
    {
        var changes = 0;
        Make().Changed += () => changes++;
        _s.Begin(VendorRead.All);
        AnswerAll();

        Assert.Equal(new HeadsetStatus(true, 100, true, true, 7, true), _s.Status);
        var settings = _s.Settings;
        Assert.Equal(4, settings.Count);
        Assert.Contains(new Slender.Engine.HeadsetSetting("Radio", "Wireless range", "Long (150 m)", "wireless.range", "1"), settings);
        Assert.Contains(new Slender.Engine.HeadsetSetting("Calls", "When a second call comes in", "Switch to the new call", "call.priority", "2"), settings);
        Assert.Contains(new Slender.Engine.HeadsetSetting("Calls", "Answer when lifted from the base", "Off", "auto_answer.enable", "0"), settings);
        Assert.Contains(new Slender.Engine.HeadsetSetting("Prompts and mute reminder", "Voice prompt language", "English", "language.prompt", "0"), settings);
        Assert.Equal(new[] { "Calls", "Calls", "Prompts and mute reminder", "Radio" }, settings.Select(s => s.Group).Order());
        Assert.True(changes >= 5);
    }

    [Fact]
    public void A_status_pass_asks_only_for_the_handset_and_never_repeats_hello()
    {
        Make();
        _s.Begin(VendorRead.All);
        AnswerAll();
        _sent.Clear();

        Assert.True(_s.Begin(VendorRead.Status));
        Assert.Equal(["cmd79"], _sent.Select(Label));
        Answer();
        Assert.False(_s.IsRunning);
    }

    [Fact]
    public void A_status_pass_before_hello_starts_with_the_handshake()
    {
        Make();
        _s.Begin(VendorRead.Status);
        AnswerAll();
        Assert.Equal(["cmd00", "cmd01", "cmd79"], _sent.Select(Label));
    }

    [Fact]
    public void A_second_begin_while_running_sends_nothing()
    {
        Make();
        Assert.True(_s.Begin(VendorRead.All));
        Assert.False(_s.Begin(VendorRead.All));
        Assert.Single(_sent);
    }

    [Fact]
    public void A_reply_to_something_else_does_not_advance_the_pass()
    {
        Make();
        _s.Begin(VendorRead.All);
        Reply(VendorVectors.SimpleReply(0x79)); // not the Hello in flight
        Assert.Single(_sent);
        Reply(VendorVectors.HelloReply());
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public void A_stale_env_reply_for_another_key_is_ignored_but_still_recorded()
    {
        Make();
        _s.Begin(VendorRead.All);
        AnswerAll();
        // Start another full pass and let it stop on the first key, then feed a reply for a different key.
        _s.Begin(VendorRead.All);
        Answer(); // dect
        int sent = _sent.Count;
        Reply(VendorVectors.EnvLong("auto_answer.enable", "1")); // first key in flight is bluetooth
        Assert.Equal(sent, _sent.Count);
        Assert.Contains(_s.Settings, s => s.Label == "Answer when lifted from the base" && s.Value == "On");
    }

    [Fact]
    public void A_setting_that_times_out_is_skipped_and_the_pass_continues()
    {
        Make();
        _s.Begin(VendorRead.All);
        Answer(); Answer(); Answer(); // hello, version, dect
        Assert.Equal(4, _sent.Count);
        _s.Expire();                  // first key: no answer
        Assert.Equal(5, _sent.Count);
        Assert.Equal(YealinkSettingsCatalog.All[1].Key, Label(_sent[^1]));
        Answer();
        Assert.Equal(6, _sent.Count);
    }

    [Fact]
    public void A_silent_hello_ends_the_pass_and_the_next_begin_tries_the_handshake_again()
    {
        Make();
        _s.Begin(VendorRead.All);
        _s.Expire();
        Assert.False(_s.IsRunning);
        Assert.Contains(_log, l => l.Contains("did not answer"));
        Assert.True(_s.Begin(VendorRead.All));
        Assert.Equal(["cmd00", "cmd00"], _sent.Select(Label));
    }

    [Fact]
    public void Three_timeouts_in_a_row_end_the_pass()
    {
        Make();
        _s.Begin(VendorRead.All);
        Answer(); Answer(); Answer();
        _s.Expire(); _s.Expire();
        Assert.True(_s.IsRunning);
        _s.Expire();
        Assert.False(_s.IsRunning);
        Assert.Contains(_log, l => l.Contains("stopped answering"));
    }

    [Fact]
    public void An_answer_resets_the_timeout_count()
    {
        Make();
        _s.Begin(VendorRead.All);
        Answer(); Answer(); Answer();
        _s.Expire(); _s.Expire();
        Answer();
        _s.Expire(); _s.Expire();
        Assert.True(_s.IsRunning);
    }

    [Fact]
    public void A_refused_write_stops_the_pass()
    {
        _writable = false;
        Make();
        Assert.True(_s.Begin(VendorRead.All));
        Assert.False(_s.IsRunning);
        Assert.Contains(_log, l => l.Contains("not writable"));
    }

    [Fact]
    public void An_empty_reply_clears_a_setting_the_base_no_longer_has()
    {
        Make();
        _s.Begin(VendorRead.All);
        AnswerAll();
        Assert.Contains(_s.Settings, s => s.Label == "Wireless range");

        _device["wireless.range"] = null;
        _s.Begin(VendorRead.All);
        AnswerAll();
        Assert.DoesNotContain(_s.Settings, s => s.Label == "Wireless range");
    }

    [Fact]
    public void Pushed_events_update_the_status_without_any_request()
    {
        Make();
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamNotify, VendorVectors.DectEvent(0x09, 1, 42, 0))) _s.OnReport(f);
        Assert.Equal(42, _s.Status.BatteryPercent);
        Assert.Empty(_sent);
    }

    [Fact]
    public void Dispose_stops_a_running_pass_and_refuses_new_ones()
    {
        Make();
        _s.Begin(VendorRead.All);
        _s.Dispose();
        Assert.False(_s.IsRunning);
        Assert.False(_s.Begin(VendorRead.All));
    }
}
