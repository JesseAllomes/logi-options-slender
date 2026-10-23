using Slender.Yealink;

namespace Slender.Tests.Yealink;

/// <summary>m61: the preview ring's frames (compared with the bytes that rang a real base), its session and the module's gate.</summary>
public class YealinkPreviewRingCodecTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Padded(string hex)
    {
        var frame = new byte[64];
        Hex(hex).CopyTo(frame, 0);
        return frame;
    }

    [Fact]
    public void Registration_is_three_fragments_of_a_160_byte_body()
    {
        var first = new byte[64];
        var middle = new byte[64];
        var last = new byte[64];
        Array.Fill(first, (byte)0x5A);
        VendorRequests.BuildSoftphoneAdd(first, middle, last);

        Assert.Equal(Padded("C8 01 3C 00 37 A0 01 00 00 00 53 6C 65 6E 64 65 72"), first[..17].Concat(new byte[47]).ToArray());
        Assert.Equal(0x76, first[10 + 32]); // "v1" sits at body offset 32
        Assert.Equal(0x31, first[10 + 33]);
        Assert.Equal(Hex("C8 00 3C 04"), middle[..4]);
        Assert.All(middle[4..], b => Assert.Equal(0, b));
        Assert.Equal(Hex("C8 02 2E 08"), last[..4]);
        Assert.All(last[4..], b => Assert.Equal(0, b));
        Assert.Equal(0x37, first[4]);
        Assert.Equal(60 + 60 + 46, first[2] + middle[2] + last[2]);
    }

    [Fact]
    public void Alloc_for_session_4_is_the_live_frame()
    {
        var frame = new byte[64];
        Assert.Equal(64, VendorRequests.BuildAllocCid(4, frame));
        Assert.Equal(Padded("C8 03 06 00 3A 00 04 00 32 00"), frame);
    }

    [Fact]
    public void Ring_on_for_session_4_call_151_is_the_live_frame()
    {
        var frame = new byte[64];
        Array.Fill(frame, (byte)0x5A);
        VendorRequests.BuildCallState(4, 151, true, frame);
        Assert.Equal(Padded("C8 03 3C 00 3C 36 04 00 97 00 04 00 97 00 05 00 00 02"), frame);
    }

    [Fact]
    public void Ring_off_differs_only_in_the_state()
    {
        var frame = new byte[64];
        VendorRequests.BuildCallState(4, 151, false, frame);
        Assert.Equal(Padded("C8 03 3C 00 3C 36 04 00 97 00 04 00 97 00 06 00 00 02"), frame);
    }

    [Fact]
    public void Quit_for_session_4_ends_with_the_session_in_p2()
    {
        var frame = new byte[64];
        VendorRequests.BuildQuit(4, frame);
        Assert.Equal(Padded("C8 03 06 00 38 00 00 00 04 00"), frame);
    }

    [Fact]
    public void The_generic_build_refuses_the_four_preview_commands()
    {
        foreach (byte id in new byte[] { 0x37, 0x38, 0x3A, 0x3C })
        {
            var frame = new byte[64];
            Assert.False(VendorRequests.IsAllowed(id));
            Assert.Throws<ArgumentOutOfRangeException>(() => VendorRequests.Build(id, "", frame));
            Assert.All(frame, b => Assert.Equal(0, b));
        }
    }

    [Fact]
    public void Replies_decode_with_status_p2_and_the_first_call_id()
    {
        var d = new VendorDecoder();
        var got = new List<(byte, ushort, ushort, ushort)>();
        d.CallReply += (c, s, p, cid) => got.Add((c, s, p, cid));
        d.Message(VendorFrame.StreamCommand, Hex("37 A0 00 00 04 00"));
        d.Message(VendorFrame.StreamCommand, Hex("3A 04 00 00 32 00 97 00 C8 00"));
        d.Message(VendorFrame.StreamCommand, Hex("3C 36 FF FF 00 00"));
        d.Message(VendorFrame.StreamCommand, Hex("38 00 00 00 04 00"));
        Assert.Equal([((byte)0x37, (ushort)0, (ushort)4, (ushort)0), (0x3A, 0, 0x32, 151), (0x3C, 0xFFFF, 0, 0), (0x38, 0, 4, 0)], got);
    }
}

public class YealinkPreviewRingSessionTests
{
    private readonly List<byte[]> _sent = [];
    private readonly List<string> _log = [];
    private readonly YealinkVendorSession _s;
    private readonly Dictionary<string, string> _device = new() { ["keypad_tone.enable"] = "0" };
    private int _changes;

    private static readonly int Keypad = Slender.Engine.Devices.YealinkSettingsCatalog.IndexOf("keypad_tone.enable");

    public YealinkPreviewRingSessionTests()
    {
        _s = new YealinkVendorSession(r => { _sent.Add(r.ToArray()); return true; }, _log.Add);
        _s.Changed += () => _changes++;
    }

    private static byte[] Call(byte cmd, ushort status = 0, ushort p2 = 0, params byte[] payload) =>
        [cmd, (byte)payload.Length, (byte)status, (byte)(status >> 8), (byte)p2, (byte)(p2 >> 8), .. payload];

    private void Reply(byte[] message)
    {
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamCommand, message)) _s.OnReport(f);
    }

    /// <summary>Names what was last sent: add is its last fragment (type 02), the call state by its state byte.</summary>
    private string Last() => _sent[^1] switch
    {
        { Length: > 1 } r when r[1] == 0x02 => "add",
        { } r when r[4] == 0x00 => "hello",
        { } r when r[4] == 0x3A => "alloc",
        { } r when r[4] == 0x3C => r[14] == 5 ? "on" : "off",
        { } r when r[4] == 0x38 => "quit",
        { } r => "other" + r[4].ToString("X2"),
    };

    private List<string> Names()
    {
        var names = new List<string>();
        foreach (var r in _sent)
            names.Add(r[1] == 0x01 ? "add" : r[1] is 0x00 or 0x02 ? "" : r[4] switch { 0x00 => "hello", 0x3A => "alloc", 0x3C => r[14] == 5 ? "on" : "off", 0x38 => "quit", 0x04 or 0x23 => "set", 0x22 or 0x03 => "get", _ => "other" });
        names.RemoveAll(n => n.Length == 0);
        return names;
    }

    private void Answer(ushort sid = 4, ushort cid = 151, ushort addStatus = 0, ushort onStatus = 0)
    {
        var req = _sent[^1];
        switch (Last())
        {
            case "hello": Reply(VendorVectors.HelloReply()); break;
            case "add": Reply(Call(0x37, addStatus, sid)); break;
            case "alloc": Reply(Call(0x3A, 0, 50, (byte)cid, (byte)(cid >> 8), (byte)(cid + 49), (byte)((cid + 49) >> 8))); break;
            case "on": Reply(Call(0x3C, onStatus)); break;
            case "off": Reply(Call(0x3C)); break;
            case "quit": Reply(Call(0x38, 0, sid)); break;
            default:
                if (req[4] == 0x23) { _device["keypad_tone.enable"] = ((char)req[10 + req[6] + 0]).ToString(); Reply(VendorVectors.EnvLong("keypad_tone.enable", "1", 0x23)); }
                else if (req[4] == 0x22) Reply(VendorVectors.EnvLong("keypad_tone.enable", _device["keypad_tone.enable"]));
                break;
        }
    }

    private void RunToRing()
    {
        while (Last() != "on") Answer();
        Answer();
    }

    [Fact]
    public void A_preview_registers_asks_for_a_call_id_rings_waits_then_stops_and_quits()
    {
        Assert.True(_s.PreviewRing());
        RunToRing();

        Assert.Equal(["hello", "add", "alloc", "on"], Names());
        Assert.Equal(3, _sent.Count(r => r[1] is 0x01 or 0x00 or 0x02));
        Assert.Equal([0x04, 0x00, 0x97, 0x00], _sent[^1][10..14]); // sid 4, cid 151
        Assert.True(_s.IsRunning);

        _s.Expire(); // the 1.5 s are up
        Assert.Equal("off", Last());
        Answer();
        Assert.Equal("quit", Last());
        Assert.Equal([0x04], _sent[^1][8..9]);
        Answer();

        Assert.False(_s.IsRunning);
        Assert.Null(_s.WriteProblem);
        Assert.Equal(["hello", "add", "alloc", "on", "off", "quit"], Names());
    }

    [Fact]
    public void A_second_preview_skips_the_handshake()
    {
        _s.PreviewRing();
        RunToRing();
        _s.Expire();
        Answer();
        Answer();
        _sent.Clear();

        Assert.True(_s.PreviewRing());
        Assert.Equal(["add"], Names());
    }

    [Fact]
    public void A_preview_while_one_runs_is_refused()
    {
        _s.PreviewRing();
        Assert.False(_s.PreviewRing());
    }

    [Fact]
    public void A_refused_ring_on_still_ends_the_session_without_a_ring_off()
    {
        _s.PreviewRing();
        while (Last() != "on") Answer();
        Answer(onStatus: 0xFFFF);

        Assert.Equal("quit", Last());
        Assert.Equal("The base did not play the preview ring.", _s.WriteProblem);
        Answer();
        Assert.False(_s.IsRunning);
        Assert.DoesNotContain("off", Names());
    }

    [Fact]
    public void A_ring_on_that_gets_no_answer_is_switched_off_and_the_session_ended()
    {
        _s.PreviewRing();
        while (Last() != "on") Answer();
        _s.Expire();
        Assert.Equal("off", Last());
        Answer();
        Assert.Equal("quit", Last());
        Answer();
        Assert.False(_s.IsRunning);
        Assert.NotNull(_s.WriteProblem);
    }

    [Fact]
    public void A_ring_off_that_gets_no_answer_still_sends_the_quit()
    {
        _s.PreviewRing();
        RunToRing();
        _s.Expire(); // ring over, off sent
        _s.Expire(); // off unanswered
        Assert.Equal("quit", Last());
        Assert.NotNull(_s.WriteProblem);
        _s.Expire(); // quit unanswered: done
        Assert.False(_s.IsRunning);
        Assert.Equal(1, _sent.Count(r => r[4] == 0x38 && r[1] == 0x03));
    }

    [Fact]
    public void A_failure_after_registration_sends_the_quit()
    {
        _s.PreviewRing();
        while (Last() != "alloc") Answer();
        _s.Expire();
        Assert.Equal("quit", Last());
        Answer();
        Assert.False(_s.IsRunning);
        Assert.DoesNotContain("on", Names());
    }

    [Fact]
    public void A_refused_registration_sends_nothing_more()
    {
        _s.PreviewRing();
        while (Last() != "add") Answer();
        Answer(addStatus: 0xFFFF);
        Assert.False(_s.IsRunning);
        Assert.NotNull(_s.WriteProblem);
        Assert.Equal(["hello", "add"], Names());
    }

    [Fact]
    public void A_preview_asked_for_during_a_pass_waits_and_runs_after_the_pending_writes()
    {
        _s.Begin(VendorRead.Status);
        Answer(); // hello
        _s.PreviewRing();
        _s.Set(Keypad, "1");
        int before = _sent.Count;
        Assert.DoesNotContain("add", Names());

        for (int guard = 0; guard < 50 && Last() != "on"; guard++)
        {
            if (Last() == "other01") Reply(VendorVectors.SimpleReply(0x01));
            else if (Last() == "other79") Reply(VendorVectors.Dect(true, 7, 100, 2, true, true));
            else Answer();
        }

        var names = Names();
        Assert.True(names.IndexOf("set") >= 0 && names.IndexOf("set") < names.IndexOf("add"));
        Assert.True(names.IndexOf("get") < names.IndexOf("add"));
        Assert.True(_sent.Count > before);
    }

    [Fact]
    public void Dispose_while_ringing_switches_the_ring_off_and_ends_the_session()
    {
        _s.PreviewRing();
        RunToRing();
        _sent.Clear();

        _s.Dispose();

        Assert.Equal(["off", "quit"], Names());
        Assert.False(_s.PreviewRing());
    }

    [Fact]
    public void A_refused_channel_is_a_problem_and_nothing_stays_running()
    {
        var s = new YealinkVendorSession(_ => false, _log.Add);
        Assert.True(s.PreviewRing());
        Assert.NotNull(s.WriteProblem);
        Assert.False(s.IsRunning);
    }
}

public class YealinkPreviewRingModuleTests
{
    private readonly FakeTransport _transport = new();
    private readonly Dictionary<string, string> _env = new() { ["wireless.range"] = "2" };
    private bool _standDown;
    private ushort _sid = 7;

    private YealinkModule Make(bool vendor = true)
    {
        _transport.VendorReply = r => r switch
        {
            _ when r[1] == 0x02 => Call(0x37, 0, _sid),
            _ when r[1] == 0x03 && r[4] == 0x3A => Call(0x3A, 0, 50, 0x97, 0x00, 0xC8, 0x00),
            _ when r[1] == 0x03 && r[4] == 0x3C => Call(0x3C, 0, 0),
            _ when r[1] == 0x03 && r[4] == 0x38 => Call(0x38, 0, _sid),
            _ => r[1] == 0x03 ? VendorVectors.ReplyFor(r, _env) : null,
        };
        return new YealinkModule(_transport, _ => { }, () => _standDown, tick: () => 1_000_000) { VendorReadsEnabled = vendor };
    }

    private static byte[] Call(byte cmd, ushort status, ushort p2, params byte[] payload) =>
        [cmd, (byte)payload.Length, (byte)status, (byte)(status >> 8), (byte)p2, (byte)(p2 >> 8), .. payload];

    private static bool IsPreview(byte[] r) => r[1] is 0x00 or 0x01 or 0x02 || r[4] is 0x3A or 0x3C or 0x38;

    [Fact]
    public void A_preview_registers_and_rings_and_stopping_the_module_switches_it_off()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        link.VendorWrites.Clear();

        m.PreviewRing();

        var sent = link.VendorWrites.Where(IsPreview).ToList();
        Assert.Equal(3, sent.Count(r => r[1] is 0x01 or 0x00 or 0x02));
        Assert.Equal([0x3A, 0x3C], sent.Where(r => r[1] == 0x03).Select(r => r[4]));
        Assert.Equal(7, link.VendorWrites[^1][10]); // ring on carries the sid the base gave
        Assert.Null(m.GetHeadset()!.WriteProblem);

        m.Stop();
        Assert.Equal([0x3C, 0x38], link.VendorWrites.Skip(link.VendorWrites.Count - 2).Select(r => r[4]));
        Assert.Equal(6, link.VendorWrites[^2][14]);
    }

    [Fact]
    public void The_bursts_a_preview_causes_do_not_count_as_a_call_ringing()
    {
        var m = Make();
        m.BaseRingEnabled = true;
        m.Start();
        var link = _transport.Link!;
        var got = new List<bool>();
        m.BaseRingChanged += got.Add;
        void Burst(byte code)
        {
            for (int i = 0; i < 12; i++)
            {
                link.Feed(YealinkCollection.ConsumerKeys, 1, code);
                link.Feed(YealinkCollection.ConsumerKeys, 1);
            }
        }

        Burst(2);
        Assert.Equal([true], got); // a real ring is heard

        m.PreviewRing();
        Burst(1);
        Burst(2);

        Assert.Equal([true], got);
    }

    [Fact]
    public void Off_hook_refuses_with_a_reason_and_sends_nothing()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        link.Feed(YealinkCollection.Telephony, 4, 0x01, 0); // off hook
        int sent = link.VendorWrites.Count;
        int changed = 0;
        m.HeadsetChanged += () => changed++;

        m.PreviewRing();

        Assert.Equal(sent, link.VendorWrites.Count);
        Assert.Equal("Finish the call before previewing the ring.", m.GetHeadset()!.WriteProblem);
        Assert.True(changed > 0);
    }

    [Fact]
    public void With_vendor_reads_off_nothing_is_sent()
    {
        var m = Make(vendor: false);
        m.Start();
        m.PreviewRing();
        Assert.Empty(_transport.Link!.VendorWrites);
        Assert.Contains("Talk to the base", m.GetHeadset()!.WriteProblem);
    }

    [Fact]
    public void While_usb_connect_runs_nothing_is_sent()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        int sent = link.VendorWrites.Count;
        _standDown = true;

        m.PreviewRing();

        Assert.Equal(sent, link.VendorWrites.Count);
        Assert.Contains("YealinkUSBConnect", m.GetHeadset()!.WriteProblem);
    }
}
