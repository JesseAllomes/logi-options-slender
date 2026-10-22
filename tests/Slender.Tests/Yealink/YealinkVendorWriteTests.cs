using System.Text;
using Slender.Engine.Devices;
using Slender.Yealink;

namespace Slender.Tests.Yealink;

/// <summary>The EnvSet request: byte-exact frames, and nothing but a catalogued, editable setting with a listed value can be built.</summary>
public class YealinkVendorWriteCodecTests
{
    private static YealinkSettingDef Def(string key) => YealinkSettingsCatalog.All[YealinkSettingsCatalog.IndexOf(key)];

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    [Fact]
    public void A_long_key_is_set_with_the_confirmed_frame()
    {
        var frame = new byte[64];
        int len = VendorRequests.BuildEnvSet(Def("keypad_tone.enable"), "1", frame);
        Assert.Equal(64, len);
        var expected = Hex("C8 03 1A 00 23 14 12 00 01 00 6B 65 79 70 61 64 5F 74 6F 6E 65 2E 65 6E 61 62 6C 65 31 00");
        Assert.Equal(expected, frame[..expected.Length]);
        Assert.All(frame[expected.Length..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void A_short_key_is_set_with_the_get_layout_and_the_value_after_the_name_field()
    {
        var frame = new byte[64];
        VendorRequests.BuildEnvSet(Def("sidetone.level"), "4", frame);
        var expected = new byte[64];
        Hex("C8 03 3C 00 04 36 00 00 00 00").CopyTo(expected, 0);
        Encoding.ASCII.GetBytes("sidetone.level").CopyTo(expected, 10);
        expected[26] = (byte)'4';
        Assert.Equal(expected, frame);
    }

    [Fact]
    public void A_stale_buffer_is_cleared()
    {
        var frame = new byte[64];
        Array.Fill(frame, (byte)0x5A);
        VendorRequests.BuildEnvSet(Def("sidetone.level"), "4", frame);
        Assert.Equal(0, frame[27]);
        Assert.Equal(0, frame[63]);
    }

    [Fact]
    public void Numbers_inside_the_range_and_listed_choices_build()
    {
        var frame = new byte[64];
        VendorRequests.BuildEnvSet(Def("sidetone.level"), "1", frame);
        VendorRequests.BuildEnvSet(Def("sidetone.level"), "15", frame);
        VendorRequests.BuildEnvSet(Def("wireless.range"), "3", frame);
        VendorRequests.BuildEnvSet(Def("platform.mode"), "UC", frame);
    }

    [Theory]
    [InlineData("sidetone.level", "0")]
    [InlineData("sidetone.level", "16")]
    [InlineData("sidetone.level", "-1")]
    [InlineData("sidetone.level", "04")]
    [InlineData("sidetone.level", "4 ")]
    [InlineData("sidetone.level", "")]
    [InlineData("sidetone.level", "abc")]
    [InlineData("keypad_tone.enable", "2")]
    [InlineData("keypad_tone.enable", "")]
    [InlineData("wireless.range", "0")]
    [InlineData("platform.mode", "teams")]
    public void Values_outside_the_range_or_the_listed_choices_are_refused(string key, string raw)
    {
        Assert.Throws<ArgumentException>(() => VendorRequests.BuildEnvSet(Def(key), raw, new byte[64]));
    }

    [Fact]
    public void A_setting_that_is_not_editable_is_refused()
    {
        var frame = new byte[64];
        Assert.Throws<ArgumentException>(() => VendorRequests.BuildEnvSet(Def("features.bluetooth_adapter_name"), "x", frame));
        Assert.All(frame, b => Assert.Equal(0, b));
    }

    [Fact]
    public void A_key_that_is_not_in_the_catalog_is_refused_even_with_a_listed_looking_value()
    {
        var frame = new byte[64];
        Assert.Throws<ArgumentException>(() => VendorRequests.BuildEnvSet(new YealinkSettingDef("factory.reset", YealinkTarget.Base, "General", "Reset", ["0", "No", "1", "Yes"]), "1", frame));
        Assert.Throws<ArgumentException>(() => VendorRequests.BuildEnvSet(Def("keypad_tone.enable") with { Key = "wireless.range" }, "1", frame));
        Assert.Throws<ArgumentException>(() => VendorRequests.BuildEnvSet(Def("sidetone.level") with { Max = 99 }, "50", frame));
        Assert.All(frame, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Every_settable_catalog_value_builds_in_one_report()
    {
        var frame = new byte[64];
        foreach (var def in YealinkSettingsCatalog.All.Where(d => d.CanSet))
            for (long v = def.IsChoice ? 0 : def.Min; v < (def.IsChoice ? def.ChoiceCount : def.Max + 1); v++)
            {
                VendorRequests.BuildEnvSet(def, def.RawFor(v)!, frame);
                Assert.True(frame[2] <= VendorFrame.MaxChunk);
            }
    }

    [Fact]
    public void The_generic_build_still_refuses_both_set_commands()
    {
        foreach (byte id in new byte[] { 0x04, 0x23 })
        {
            var frame = new byte[64];
            Assert.False(VendorRequests.IsAllowed(id));
            Assert.Throws<ArgumentOutOfRangeException>(() => VendorRequests.Build(id, "keypad_tone.enable", frame));
            Assert.All(frame, b => Assert.Equal(0, b));
        }
    }

    [Fact]
    public void Set_replies_decode_with_the_key_and_value_like_the_get_replies()
    {
        var d = new VendorDecoder();
        var got = new List<(byte, string?, string?)>();
        d.Reply += (c, k, v) => got.Add((c, k, v));
        d.Message(VendorFrame.StreamCommand, VendorVectors.EnvShort("sidetone.level", "4", 0x04));
        d.Message(VendorFrame.StreamCommand, VendorVectors.EnvLong("keypad_tone.enable", "1", 0x23));
        Assert.Equal([((byte)0x04, "sidetone.level", "4"), ((byte)0x23, "keypad_tone.enable", "1")], got);
    }
}

public class YealinkVendorWriteSessionTests
{
    private readonly List<byte[]> _sent = [];
    private readonly List<string> _log = [];
    private readonly YealinkVendorSession _s;
    private readonly Dictionary<string, string?> _device = new() { ["sidetone.level"] = "2", ["keypad_tone.enable"] = "0", ["voice_announcements.enable"] = "0" };
    private bool _ignoreWrites;
    private int _changes;

    private static readonly int Sidetone = YealinkSettingsCatalog.IndexOf("sidetone.level");
    private static readonly int Keypad = YealinkSettingsCatalog.IndexOf("keypad_tone.enable");
    private static readonly int Announce = YealinkSettingsCatalog.IndexOf("voice_announcements.enable");

    public YealinkVendorWriteSessionTests()
    {
        _s = new YealinkVendorSession(r => { _sent.Add(r.ToArray()); return true; }, _log.Add);
        _s.Changed += () => _changes++;
    }

    private static string Ascii(byte[] b, int at, int len) => Encoding.ASCII.GetString(b, at, len).TrimEnd('\0');

    private void Reply(byte[] message)
    {
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamCommand, message)) _s.OnReport(f);
    }

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
                string key = Ascii(req, 10, 16);
                Reply(VendorVectors.EnvShort(key, _device.GetValueOrDefault(key)));
                break;
            }
            case 0x22:
            {
                string key = Ascii(req, 10, req[6]);
                Reply(VendorVectors.EnvLong(key, _device.GetValueOrDefault(key) ?? ""));
                break;
            }
            case 0x04:
            {
                string key = Ascii(req, 10, 16), value = Ascii(req, 26, 38);
                if (!_ignoreWrites) _device[key] = value;
                Reply(VendorVectors.EnvShort(key, value, 0x04));
                break;
            }
            case 0x23:
            {
                string key = Ascii(req, 10, req[6]), value = Ascii(req, 10 + req[6], req[8]);
                if (!_ignoreWrites) _device[key] = value;
                Reply(VendorVectors.EnvLong(key, value, 0x23));
                break;
            }
        }
    }

    private void AnswerAll()
    {
        for (int guard = 0; _s.IsRunning && guard < 200; guard++) Answer();
    }

    [Fact]
    public void A_set_does_the_handshake_first_when_it_never_ran_then_writes_and_reads_back()
    {
        Assert.True(_s.Set(Keypad, "1"));
        AnswerAll();

        Assert.Equal([(byte)0x00, 0x23, 0x22], _sent.Select(r => r[4]));
        Assert.False(_s.IsRunning);
        Assert.Null(_s.WriteProblem);
        Assert.Contains(_s.Settings, s => s is { Key: "keypad_tone.enable", Raw: "1", Value: "On" });
        Assert.True(_changes > 0);
    }

    [Fact]
    public void After_a_handshake_a_short_key_goes_straight_to_the_write()
    {
        _s.Begin(VendorRead.Status);
        AnswerAll();
        _sent.Clear();

        _s.Set(Sidetone, "4");
        Assert.Equal(0x04, _sent.Single()[4]);
        AnswerAll();
        Assert.Equal([(byte)0x04, 0x03], _sent.Select(r => r[4]));
        Assert.Equal("4", _device["sidetone.level"]);
        Assert.Contains(_s.Settings, s => s is { Key: "sidetone.level", Raw: "4" });
        Assert.Null(_s.WriteProblem);
    }

    [Fact]
    public void A_read_back_that_differs_sets_the_problem_and_the_next_good_write_clears_it()
    {
        _ignoreWrites = true;
        _s.Set(Keypad, "1");
        AnswerAll();
        Assert.Equal("The base did not take the change to Keypad tone.", _s.WriteProblem);
        Assert.False(_s.IsRunning);

        _ignoreWrites = false;
        _s.Set(Keypad, "1");
        AnswerAll();
        Assert.Null(_s.WriteProblem);
    }

    [Fact]
    public void A_write_that_gets_no_answer_sets_the_problem_and_stops_the_timer()
    {
        _s.Set(Keypad, "1");
        Answer(); // hello
        _s.Expire();
        Assert.Equal("The base did not take the change to Keypad tone.", _s.WriteProblem);
        Assert.False(_s.IsRunning);
        Assert.Equal(2, _sent.Count);
        _s.Expire(); // nothing running: nothing happens
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public void A_read_back_that_gets_no_answer_is_a_problem_too()
    {
        _s.Begin(VendorRead.Status);
        AnswerAll();
        _s.Set(Sidetone, "4");
        Answer(); // set echoed, read-back asked
        Assert.Equal(0x03, _sent[^1][4]);
        _s.Expire();
        Assert.NotNull(_s.WriteProblem);
        Assert.False(_s.IsRunning);
    }

    [Fact]
    public void A_set_during_a_pass_waits_for_it_and_then_runs()
    {
        _s.Begin(VendorRead.All);
        Answer(); // hello
        int before = _sent.Count;
        _s.Set(Keypad, "1");
        Assert.Equal(before, _sent.Count);

        AnswerAll();

        var cmds = _sent.Select(r => r[4]).ToList();
        int write = cmds.IndexOf(0x23);
        Assert.True(write > 3 + YealinkSettingsCatalog.All.Count - 1);
        Assert.Equal(0x22, cmds[write + 1]);
        Assert.Equal(write + 2, cmds.Count);
        Assert.Equal("1", _device["keypad_tone.enable"]);
        Assert.False(_s.IsRunning);
    }

    [Fact]
    public void Every_waiting_setting_is_written_after_the_pass()
    {
        _s.Begin(VendorRead.Status);
        Answer();
        _s.Set(Keypad, "1");
        _s.Set(Announce, "1");
        _s.Set(Sidetone, "9");
        AnswerAll();

        Assert.Equal("1", _device["keypad_tone.enable"]);
        Assert.Equal("1", _device["voice_announcements.enable"]);
        Assert.Equal("9", _device["sidetone.level"]);
        Assert.Equal(3, _sent.Count(r => r[4] is 0x04 or 0x23));
    }

    [Fact]
    public void A_write_in_flight_keeps_the_next_one_waiting()
    {
        _s.Begin(VendorRead.Status);
        AnswerAll();
        _s.Set(Sidetone, "4");
        _s.Set(Announce, "1");
        AnswerAll();

        Assert.Equal("4", _device["sidetone.level"]);
        Assert.Equal("1", _device["voice_announcements.enable"]);
        Assert.Equal(2, _sent.Count(r => r[4] is 0x04 or 0x23));
    }

    [Fact]
    public void A_bad_index_and_a_disposed_session_send_nothing()
    {
        Assert.False(_s.Set(-1, "1"));
        Assert.False(_s.Set(YealinkSettingsCatalog.All.Count, "1"));
        _s.Dispose();
        Assert.False(_s.Set(Keypad, "1"));
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_value_the_catalog_refuses_is_never_written()
    {
        _s.Begin(VendorRead.Status);
        AnswerAll();
        _sent.Clear();
        _s.Set(Sidetone, "99");
        Assert.Empty(_sent);
        Assert.NotNull(_s.WriteProblem);
        Assert.False(_s.IsRunning);
    }

    [Fact]
    public void A_refused_channel_is_a_problem()
    {
        var s = new YealinkVendorSession(_ => false, _log.Add);
        s.Set(Keypad, "1");
        Assert.NotNull(s.WriteProblem);
        Assert.False(s.IsRunning);
    }
}

/// <summary>The module's write gate: refusals say why in the headset state and write nothing.</summary>
public class YealinkVendorWriteModuleTests
{
    private readonly FakeTransport _transport = new();
    private readonly List<string> _log = [];
    private readonly Dictionary<string, string> _env = new() { ["wireless.range"] = "2", ["keypad_tone.enable"] = "0", ["platform.mode"] = "Teams" };
    private bool _standDown;

    private static readonly int Keypad = YealinkSettingsCatalog.IndexOf("keypad_tone.enable");
    private static readonly int Platform = YealinkSettingsCatalog.IndexOf("platform.mode");

    private YealinkModule Make(bool vendor = true)
    {
        _transport.VendorReply = r => r[4] switch
        {
            0x23 => Write(Encoding.ASCII.GetString(r, 10, r[6]), Encoding.ASCII.GetString(r, 10 + r[6], r[8]), VendorVectors.EnvLong),
            0x04 => Write(Encoding.ASCII.GetString(r, 10, 16).TrimEnd('\0'), Encoding.ASCII.GetString(r, 26, 38).TrimEnd('\0'), VendorVectors.EnvShort),
            _ => VendorVectors.ReplyFor(r, _env),
        };
        return new YealinkModule(_transport, _log.Add, () => _standDown) { VendorReadsEnabled = vendor };
    }

    private byte[] Write(string key, string value, Func<string, string, byte, byte[]> echo)
    {
        _env[key] = value;
        return echo(key, value, key.Length > 16 ? (byte)0x23 : (byte)0x04);
    }

    private static int Writes(FakeLink link) => link.VendorWrites.Count(r => r[4] is 0x04 or 0x23);

    [Fact]
    public void A_catalogued_change_is_written_read_back_and_reaches_the_tray()
    {
        var m = Make();
        m.Start();
        int changed = 0;
        m.HeadsetChanged += () => changed++;

        m.SetSetting(Keypad, 1);

        var link = _transport.Link!;
        Assert.Equal(1, Writes(link));
        Assert.Equal("1", _env["keypad_tone.enable"]);
        var h = m.GetHeadset()!;
        Assert.Null(h.WriteProblem);
        Assert.Contains(h.Settings!, s => s is { Key: "keypad_tone.enable", Raw: "1", Value: "On" });
        Assert.True(changed > 0);
        Assert.Contains(_log, l => l.Contains("keypad_tone.enable=1"));
    }

    [Fact]
    public void A_value_outside_the_catalog_is_refused_and_nothing_is_written()
    {
        var m = Make();
        m.Start();
        m.SetSetting(Keypad, 2);
        m.SetSetting(-1, 0);
        m.SetSetting(YealinkSettingsCatalog.IndexOf("features.bluetooth_adapter_name"), 0);
        Assert.Equal(0, Writes(_transport.Link!));
        Assert.NotNull(m.GetHeadset()!.WriteProblem);
    }

    [Fact]
    public void With_vendor_reads_off_it_is_refused_with_words_and_nothing_is_written()
    {
        var m = Make(vendor: false);
        m.Start();
        int changed = 0;
        m.HeadsetChanged += () => changed++;
        m.SetSetting(Keypad, 1);
        Assert.Empty(_transport.Link!.VendorWrites);
        Assert.Contains("Read headset status", m.GetHeadset()!.WriteProblem);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void While_usb_connect_runs_it_is_refused()
    {
        var m = Make();
        m.Start();
        _standDown = true;
        m.SetSetting(Keypad, 1);
        Assert.Equal(0, Writes(_transport.Link!));
        Assert.Contains(YealinkModule.YealinkConnectProcess, m.GetHeadset()!.WriteProblem);
    }

    [Fact]
    public void Without_an_open_channel_it_is_refused()
    {
        var m = Make();
        m.SetSetting(Keypad, 1);
        Assert.Null(_transport.Link);
        Assert.Contains("not connected", m.GetHeadset()!.WriteProblem);
    }

    [Fact]
    public void A_caution_setting_is_refused_while_off_hook_and_allowed_once_the_call_ends()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        link.Feed(YealinkCollection.Telephony, 4, 0x01, 0); // off hook

        m.SetSetting(Platform, 1);
        Assert.Equal(0, Writes(link));
        Assert.Contains("call", m.GetHeadset()!.WriteProblem);

        m.SetSetting(Keypad, 1); // no caution: fine during a call
        Assert.Equal(1, Writes(link));
        Assert.Null(m.GetHeadset()!.WriteProblem);

        link.Feed(YealinkCollection.Telephony, 4, 0x08, 0); // hooked again
        m.SetSetting(Platform, 1);
        Assert.Equal(2, Writes(link));
        Assert.Equal("UC", _env["platform.mode"]);
    }
}

/// <summary>Waiting writes are kept per setting (r58); a failed write always republishes and a new attempt clears the old warning (r60).</summary>
public class YealinkVendorPendingAndFailureTests
{
    private readonly List<byte[]> _sent = [];
    private readonly YealinkVendorSession _s;
    private readonly Dictionary<string, string?> _device = new() { ["sidetone.level"] = "2", ["keypad_tone.enable"] = "0", ["voice_announcements.enable"] = "0" };
    private int _changes;

    private static readonly int Sidetone = YealinkSettingsCatalog.IndexOf("sidetone.level");
    private static readonly int Keypad = YealinkSettingsCatalog.IndexOf("keypad_tone.enable");
    private static readonly int Announce = YealinkSettingsCatalog.IndexOf("voice_announcements.enable");

    public YealinkVendorPendingAndFailureTests()
    {
        _s = new YealinkVendorSession(r => { _sent.Add(r.ToArray()); return true; }, _ => { });
        _s.Changed += () => _changes++;
    }

    private static string Ascii(byte[] b, int at, int len) => Encoding.ASCII.GetString(b, at, len).TrimEnd('\0');

    private void Reply(byte[] message)
    {
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamCommand, message)) _s.OnReport(f);
    }

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
                string key = Ascii(req, 10, 16);
                Reply(VendorVectors.EnvShort(key, _device.GetValueOrDefault(key)));
                break;
            }
            case 0x22:
            {
                string key = Ascii(req, 10, req[6]);
                Reply(VendorVectors.EnvLong(key, _device.GetValueOrDefault(key) ?? ""));
                break;
            }
            case 0x04:
            {
                string key = Ascii(req, 10, 16), value = Ascii(req, 26, 38);
                _device[key] = value;
                Reply(VendorVectors.EnvShort(key, value, 0x04));
                break;
            }
            case 0x23:
            {
                string key = Ascii(req, 10, req[6]), value = Ascii(req, 10 + req[6], req[8]);
                _device[key] = value;
                Reply(VendorVectors.EnvLong(key, value, 0x23));
                break;
            }
        }
    }

    private void AnswerAll()
    {
        for (int guard = 0; _s.IsRunning && guard < 200; guard++) Answer();
    }

    [Fact]
    public void Writes_to_different_settings_during_a_pass_are_all_sent()
    {
        _s.Begin(VendorRead.Status);
        Answer(); // hello
        _s.Set(Keypad, "1");
        _s.Set(Announce, "1");
        AnswerAll();

        var sets = _sent.Where(r => r[4] is 0x04 or 0x23).ToList();
        Assert.Equal(2, sets.Count);
        Assert.Contains(sets, r => Ascii(r, 10, r[6]) == "keypad_tone.enable");
        Assert.Contains(sets, r => Ascii(r, 10, r[6]) == "voice_announcements.enable");
        Assert.Equal("1", _device["keypad_tone.enable"]);
        Assert.Equal("1", _device["voice_announcements.enable"]);
    }

    [Fact]
    public void Two_writes_to_the_same_setting_during_a_pass_send_only_the_latest()
    {
        _s.Begin(VendorRead.Status);
        Answer(); // hello
        _s.Set(Sidetone, "4");
        _s.Set(Sidetone, "5");
        AnswerAll();

        var set = Assert.Single(_sent, r => r[4] is 0x04 or 0x23);
        Assert.Equal("5", Ascii(set, 26, 38));
        Assert.Equal("5", _device["sidetone.level"]);
    }

    [Fact]
    public void The_same_failure_twice_raises_changed_each_time_and_a_new_attempt_clears_the_warning()
    {
        _s.Set(Keypad, "1");
        Answer(); // hello
        int before = _changes;
        _s.Expire();
        Assert.True(_changes > before);
        Assert.NotNull(_s.WriteProblem);

        _s.Set(Keypad, "1");
        Assert.Null(_s.WriteProblem); // cleared as the new attempt starts
        before = _changes;
        _s.Expire();
        Assert.True(_changes > before);
        Assert.NotNull(_s.WriteProblem);
    }
}
