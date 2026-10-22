using Slender.Yealink;

namespace Slender.Tests.Yealink;

/// <summary>The module's vendor reads: gating, timing and what reaches the tray; and the COL01 listener beside COL02.</summary>
public class YealinkVendorModuleTests
{
    private readonly FakeTransport _transport = new();
    private readonly List<string> _log = [];
    private readonly Dictionary<string, string> _env = new() { ["wireless.range"] = "2", ["keypad_tone.enable"] = "1" };
    private bool _standDown;
    private long _now = 1_000_000;

    private YealinkModule Make(bool vendor = true, bool respond = true)
    {
        if (respond) _transport.VendorReply = r => VendorVectors.ReplyFor(r, _env);
        return new YealinkModule(_transport, _log.Add, () => _standDown, tick: () => _now) { VendorReadsEnabled = vendor };
    }

    // ---- gating ----

    [Fact]
    public void Vendor_reads_are_on_by_default_and_start_with_the_handshake()
    {
        var m = Make(respond: false);
        m.Start();
        Assert.True(_transport.VendorRequested);
        Assert.Equal(0x00, _transport.Link!.VendorWrites.Single()[4]);
    }

    [Fact]
    public void With_vendor_reads_off_the_channel_is_not_requested_and_nothing_is_ever_sent()
    {
        var m = Make(vendor: false);
        m.Start();
        var link = _transport.Link!;

        m.Wake();
        m.RefreshSettings();
        _now += 60_000;
        m.RefreshSettings();
        m.SetMuteLed(true);
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamNotify, VendorVectors.DectEvent(0x09, 1, 50, 2)))
            link.Feed(YealinkCollection.Vendor, f);

        Assert.False(_transport.VendorRequested);
        Assert.False(_transport.WriteRequested);
        Assert.Empty(link.VendorWrites);
        Assert.Empty(link.Writes);
        Assert.Null(m.GetHeadset());
    }

    [Fact]
    public void While_stood_down_nothing_is_opened_so_nothing_is_sent()
    {
        _standDown = true;
        var m = Make();
        m.Start();
        m.Wake();
        m.RefreshSettings();
        Assert.Equal(0, _transport.Opens);
        Assert.Null(_transport.Link);
        Assert.Null(m.GetHeadset());
    }

    [Fact]
    public void If_usb_connect_starts_while_open_wake_and_refresh_send_nothing()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        int sent = link.VendorWrites.Count;

        _standDown = true;
        m.Wake();
        _now += 60_000;
        m.RefreshSettings();

        Assert.Equal(sent, link.VendorWrites.Count);
        Assert.Contains(_log, l => l.Contains("not reading the base"));
    }

    [Fact]
    public void Nothing_is_sent_when_the_module_is_stopped_or_not_started()
    {
        var m = Make();
        m.RefreshSettings();
        m.Start();
        var link = _transport.Link!;
        m.Stop();
        int sent = link.VendorWrites.Count;
        _now += 60_000;
        m.RefreshSettings();
        m.Wake();
        Assert.Equal(sent, link.VendorWrites.Count);
    }

    [Fact]
    public void Only_allowlisted_requests_are_ever_written()
    {
        var m = Make();
        m.Start();
        _now += 60_000;
        m.RefreshSettings();
        m.Wake();
        Assert.NotEmpty(_transport.Link!.VendorWrites);
        Assert.All(_transport.Link.VendorWrites, r => Assert.True(VendorRequests.IsAllowed(r[4])));
    }

    // ---- reads ----

    [Fact]
    public void Start_reads_the_base_and_hands_the_result_to_the_tray()
    {
        int changed = 0;
        var m = Make();
        m.HeadsetChanged += () => changed++;
        m.Start();

        var h = m.GetHeadset()!;
        Assert.Equal(100, h.BatteryPercent);
        Assert.True(h.Charging);
        Assert.True(h.Docked);
        Assert.Equal(7, h.Signal);
        Assert.True(h.InRange);
        Assert.True(h.Registered);
        Assert.Equal(2, h.Settings!.Count);
        Assert.Contains(h.Settings, s => s is { Group: "General", Label: "Wireless range", Value: "Medium (30 m)" });
        Assert.Contains(h.Settings, s => s is { Group: "Sound", Label: "Keypad tone", Value: "On" });
        Assert.True(changed > 0);
    }

    [Fact]
    public void Pushed_battery_and_signal_update_the_headset_without_a_request()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        int sent = link.VendorWrites.Count, changed = 0;
        m.HeadsetChanged += () => changed++;

        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamNotify, VendorVectors.DectEvent(0x09, 1, 64, 0))) link.Feed(YealinkCollection.Vendor, f);
        foreach (var f in VendorVectors.Fragment(VendorFrame.StreamNotify, VendorVectors.DectEvent(0x04, 1, 0))) link.Feed(YealinkCollection.Vendor, f);

        var h = m.GetHeadset()!;
        Assert.Equal(64, h.BatteryPercent);
        Assert.False(h.Charging);
        Assert.False(h.InRange);
        Assert.Equal(2, changed);
        Assert.Equal(sent, link.VendorWrites.Count);
    }

    [Fact]
    public void Wake_with_a_healthy_link_reads_only_the_handset_status()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        int sent = link.VendorWrites.Count;

        m.Wake();

        Assert.Equal(sent + 1, link.VendorWrites.Count);
        Assert.Equal(0x79, link.VendorWrites[^1][4]);
    }

    [Fact]
    public void Wake_that_reopens_reads_everything_again()
    {
        var m = Make();
        m.Start();
        var first = _transport.Link!;
        first.IsFaulted = true;
        m.Wake();
        Assert.NotSame(first, _transport.Link);
        Assert.Equal(0x00, _transport.Link!.VendorWrites[0][4]);
        Assert.True(_transport.Link.VendorWrites.Count > 40);
    }

    [Fact]
    public void Settings_page_refresh_is_limited_to_one_read_every_few_seconds()
    {
        var m = Make();
        m.Start();
        var link = _transport.Link!;
        int sent = link.VendorWrites.Count;

        _now += YealinkModule.RefreshMinMs - 1;
        m.RefreshSettings();
        Assert.Equal(sent, link.VendorWrites.Count);

        _now += 2;
        m.RefreshSettings();
        Assert.True(link.VendorWrites.Count > sent + 40); // handset status and every setting
        int afterRefresh = link.VendorWrites.Count;

        _now += 1000;
        m.RefreshSettings();
        Assert.Equal(afterRefresh, link.VendorWrites.Count);
    }

    [Fact]
    public void Stopping_clears_what_the_base_said_and_tells_the_tray()
    {
        int changed = 0;
        var m = Make();
        m.Start();
        Assert.NotNull(m.GetHeadset());
        m.HeadsetChanged += () => changed++;
        m.Stop();
        Assert.Null(m.GetHeadset());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Changing_the_flag_reopens_the_link_with_or_without_the_vendor_channel()
    {
        var m = Make();
        m.Start();
        Assert.True(_transport.VendorRequested);
        var first = _transport.Link!;

        m.ApplySettings(false, true, vendorReads: false);
        Assert.False(_transport.VendorRequested);
        Assert.True(first.Disposed);
        Assert.Null(m.GetHeadset());
        Assert.Empty(_transport.Link!.VendorWrites);

        m.ApplySettings(false, true, vendorReads: false);
        Assert.Equal(2, _transport.Opens);

        m.ApplySettings(false, true, vendorReads: true);
        Assert.True(_transport.VendorRequested);
        Assert.NotNull(m.GetHeadset());
    }

    [Fact]
    public void A_silent_base_leaves_the_module_open_and_the_headset_unknown()
    {
        var m = Make(respond: false);
        m.Start();
        Assert.True(m.IsOpen);
        Assert.Null(m.GetHeadset());
    }

    // ---- COL01 ----

    private static CollectionCaps StandardCaps() => new(3, 0, new Dictionary<ushort, UsageSlot>
    {
        [YealinkUsages.HookSwitch] = new(2, 8),
        [YealinkUsages.PhoneMute] = new(2, 9),
        [YealinkUsages.Reject] = new(2, 10),
    }, new Dictionary<ushort, UsageSlot>());

    [Fact]
    public void Standard_collection_reports_raise_the_same_events()
    {
        _transport.StandardCaps = StandardCaps();
        var m = Make(vendor: false);
        var got = new List<string>();
        m.HookChanged += v => got.Add($"hook:{v}");
        m.MutePressed += () => got.Add("mute");
        m.RejectPressed += () => got.Add("reject");
        m.Start();
        var l = _transport.Link!;

        l.Feed(YealinkCollection.Standard, 2, 0x01, 0);
        l.Feed(YealinkCollection.Standard, 2, 0x03, 0);
        l.Feed(YealinkCollection.Standard, 2, 0x01, 0);
        l.Feed(YealinkCollection.Standard, 2, 0x05, 0);
        l.Feed(YealinkCollection.Standard, 2, 0x00, 0);

        Assert.Equal(["hook:True", "mute", "reject", "hook:False"], got);
    }

    [Fact]
    public void The_same_press_on_both_collections_raises_one_event()
    {
        _transport.StandardCaps = StandardCaps();
        var m = Make(vendor: false);
        int mutes = 0, hooks = 0;
        m.MutePressed += () => mutes++;
        m.HookChanged += _ => hooks++;
        m.Start();
        var l = _transport.Link!;

        l.Feed(YealinkCollection.Telephony, 4, 0x01, 0);
        l.Feed(YealinkCollection.Standard, 2, 0x01, 0);
        l.Feed(YealinkCollection.Telephony, 4, 0x05, 0);
        l.Feed(YealinkCollection.Standard, 2, 0x03, 0);

        Assert.Equal(1, hooks);
        Assert.Equal(1, mutes);
    }

    [Fact]
    public void Standard_reports_are_ignored_when_that_collection_was_not_opened()
    {
        var m = Make(vendor: false);
        int events = 0;
        m.MutePressed += () => events++;
        m.Start();
        _transport.Link!.Feed(YealinkCollection.Standard, 2, 0x03, 0);
        Assert.Equal(0, events);
    }
}

public class YealinkStandardDecoderTests
{
    private static CollectionCaps StdCaps() => new(3, 0, new Dictionary<ushort, UsageSlot>
    {
        [YealinkUsages.HookSwitch] = new(2, 8),
        [YealinkUsages.PhoneMute] = new(2, 9),
        [YealinkUsages.Flash] = new(2, 10),
    }, new Dictionary<ushort, UsageSlot>());

    private long _now;

    private (YealinkInputDecoder D, List<(YealinkEventKind Kind, bool Value)> Events) Make(bool withStandard = true)
    {
        var d = new YealinkInputDecoder(GenesysCaps.Create(), withStandard ? StdCaps() : null, () => _now);
        return (d, []);
    }

    private static void Std(YealinkInputDecoder d, List<(YealinkEventKind, bool)> e, params byte[] report) =>
        d.DecodeStandard(report, (k, v) => e.Add((k, v)));

    private static void Tel(YealinkInputDecoder d, List<(YealinkEventKind, bool)> e, params byte[] report) =>
        d.DecodeTelephony(report, (k, v) => e.Add((k, v)));

    [Fact]
    public void Standard_report_two_decodes_hook_mute_and_flash_by_caps()
    {
        var (d, e) = Make();
        Std(d, e, 2, 0x01, 0);
        Std(d, e, 2, 0x03, 0);
        Std(d, e, 2, 0x07, 0);
        Std(d, e, 2, 0x00, 0);
        Assert.Equal(
            [(YealinkEventKind.HookChanged, true), (YealinkEventKind.MutePressed, true), (YealinkEventKind.FlashPressed, true), (YealinkEventKind.HookChanged, false)],
            e);
    }

    [Fact]
    public void A_standard_report_with_another_id_or_too_short_is_ignored()
    {
        var (d, e) = Make();
        Std(d, e, 4, 0x07, 0);
        Std(d, e, 2);
        Std(d, e);
        Assert.Empty(e);
    }

    [Fact]
    public void Without_standard_caps_standard_reports_do_nothing()
    {
        var (d, e) = Make(withStandard: false);
        Std(d, e, 2, 0x07, 0);
        Assert.Empty(e);
    }

    [Fact]
    public void A_mute_press_seen_on_both_collections_within_the_window_counts_once()
    {
        var (d, e) = Make();
        Tel(d, e, 4, 0x04, 0);
        _now += YealinkInputDecoder.DedupMs - 1;
        Std(d, e, 2, 0x02, 0);
        Assert.Equal([(YealinkEventKind.MutePressed, true)], e);
    }

    [Fact]
    public void A_press_on_the_other_collection_after_the_window_is_a_new_press()
    {
        var (d, e) = Make();
        Tel(d, e, 4, 0x04, 0);
        _now += YealinkInputDecoder.DedupMs;
        Std(d, e, 2, 0x02, 0);
        Assert.Equal(2, e.Count);
    }

    [Fact]
    public void Quick_presses_on_one_collection_are_never_dropped()
    {
        var (d, e) = Make();
        Std(d, e, 2, 0x02, 0);
        Std(d, e, 2, 0x00, 0);
        Std(d, e, 2, 0x02, 0);
        Assert.Equal(2, e.Count);
    }

    [Fact]
    public void A_hook_change_both_collections_report_is_raised_once()
    {
        var (d, e) = Make();
        Tel(d, e, 4, 0x01, 0);
        Std(d, e, 2, 0x01, 0);
        Tel(d, e, 4, 0x00, 0);
        Std(d, e, 2, 0x00, 0);
        Assert.Equal([(YealinkEventKind.HookChanged, true), (YealinkEventKind.HookChanged, false)], e);
    }
}
