using Slender.Yealink;
using Slender.Yealink.Windows;

namespace Slender.Tests.Yealink;

internal sealed class FakeLink(ICollectionCaps? caps, YealinkReportHandler handler, ICollectionCaps? standard = null, bool vendor = false, Func<byte[], byte[]?>? vendorReply = null) : IYealinkLink
{
    public ICollectionCaps? Telephony { get; } = caps;
    public ICollectionCaps? Standard { get; } = standard;
    public bool IsFaulted { get; set; }
    public bool Started, Disposed;
    public readonly List<byte[]> Writes = [];
    public readonly List<byte[]> VendorWrites = [];

    public void Start() => Started = true;
    public bool Write(ReadOnlySpan<byte> report) { Writes.Add(report.ToArray()); return true; }
    public bool WriteVendor(ReadOnlySpan<byte> report)
    {
        if (!vendor) return false;
        var request = report.ToArray();
        VendorWrites.Add(request);
        if (vendorReply?.Invoke(request) is { } reply)
            foreach (var frame in VendorVectors.Fragment(VendorFrame.StreamCommand, reply)) handler(YealinkCollection.Vendor, frame);
        return true;
    }
    public void Dispose() => Disposed = true;
    public void Feed(YealinkCollection c, params byte[] report) => handler(c, report);
}

internal sealed class FakeTransport : IYealinkTransport
{
    public bool Present = true;
    public bool WriteRequested, VendorRequested;
    public int Opens;
    public FakeLink? Link;

    /// <summary>What the next link offers: the standard collection's caps, and a writable vendor channel when it was asked for.</summary>
    public ICollectionCaps? StandardCaps;

    /// <summary>Answers each vendor request the module writes (synchronously, as the read thread would); null = the base is silent.</summary>
    public Func<byte[], byte[]?>? VendorReply;

    public IYealinkLink? Open(bool writeLeds, bool vendorReads, YealinkReportHandler onReport, Action<string> log)
    {
        Opens++;
        WriteRequested = writeLeds;
        VendorRequested = vendorReads;
        if (!Present) return null;
        return Link = new FakeLink(GenesysCaps.Create(), onReport, StandardCaps, vendorReads, VendorReply);
    }
}

public class YealinkModuleTests
{
    private readonly FakeTransport _transport = new();
    private readonly List<string> _log = [];
    private bool _standDown;

    private YealinkModule Make() => new(_transport, _log.Add, () => _standDown);

    [Fact]
    public void Device_id_matches_the_catalog()
    {
        Assert.Equal("yealink-wh64", Make().DeviceId);
    }

    [Fact]
    public void Nothing_is_opened_until_start_and_stop_releases_it()
    {
        var m = Make();
        Assert.Equal(0, _transport.Opens);
        Assert.False(m.IsOpen);

        m.Start();
        Assert.True(m.IsOpen);
        Assert.True(_transport.Link!.Started);

        var link = _transport.Link;
        m.Stop();
        Assert.False(m.IsOpen);
        Assert.True(link.Disposed);

        // Reports that arrive after Stop go nowhere.
        link.Feed(YealinkCollection.Telephony, 4, 0x04, 0);
    }

    [Fact]
    public void Events_come_through_from_both_collections()
    {
        var m = Make();
        var got = new List<string>();
        m.HookChanged += v => got.Add($"hook:{v}");
        m.MutePressed += () => got.Add("mute");
        m.FlashPressed += () => got.Add("flash");
        m.RejectPressed += () => got.Add("reject");
        m.TeamsButtonPressed += () => got.Add("teams");
        m.Start();
        var l = _transport.Link!;

        l.Feed(YealinkCollection.Telephony, 4, 0x01, 0);
        l.Feed(YealinkCollection.Telephony, 4, 0x05, 0);
        l.Feed(YealinkCollection.Telephony, 4, 0x08, 0);
        l.Feed(YealinkCollection.Telephony, 4, 0x40, 0);
        l.Feed(YealinkCollection.Telephony, 0xC8, 0x3B, 0, 0);
        l.Feed(YealinkCollection.Teams, 0x9B, 0x01, 0);

        Assert.Equal(["hook:True", "mute", "hook:False", "flash", "reject", "teams"], got);
    }

    [Fact]
    public void A_throwing_handler_is_logged_not_propagated()
    {
        var m = Make();
        m.MutePressed += () => throw new InvalidOperationException("boom");
        m.Start();
        _transport.Link!.Feed(YealinkCollection.Telephony, 4, 0x04, 0);
        Assert.Contains(_log, x => x.Contains("boom"));
    }

    [Fact]
    public void Leds_are_never_written_by_default()
    {
        var m = Make();
        m.Start();
        m.SetMuteLed(true);
        Assert.False(_transport.WriteRequested);
        Assert.Empty(_transport.Link!.Writes);
    }

    [Fact]
    public void Mute_led_is_written_once_per_change_when_enabled()
    {
        var m = Make();
        m.LedWritesEnabled = true;
        m.Start();
        Assert.True(_transport.WriteRequested);

        m.SetMuteLed(true);
        m.SetMuteLed(true);
        m.SetMuteLed(false);

        Assert.Equal([[4, 0x02], [4, 0x00]], _transport.Link!.Writes);
    }

    [Fact]
    public void Stands_down_when_yealink_connect_is_running_and_rechecks_on_wake()
    {
        _standDown = true;
        var m = Make();
        m.Start();
        Assert.Equal(0, _transport.Opens);
        Assert.False(m.IsOpen);
        Assert.Contains(_log, x => x.Contains("YealinkUSBConnect"));

        m.Wake();
        Assert.Equal(0, _transport.Opens);

        _standDown = false;
        m.Wake();
        Assert.True(m.IsOpen);
    }

    [Fact]
    public void Missing_device_is_logged_and_wake_retries()
    {
        _transport.Present = false;
        var m = Make();
        m.Start();
        Assert.False(m.IsOpen);
        Assert.Contains(_log, x => x.Contains("not found"));

        _transport.Present = true;
        m.Wake();
        Assert.True(m.IsOpen);
    }

    [Fact]
    public void Wake_replaces_a_faulted_link_but_leaves_a_healthy_one()
    {
        var m = Make();
        m.Start();
        var first = _transport.Link!;
        m.Wake();
        Assert.Equal(1, _transport.Opens);

        first.IsFaulted = true;
        m.Wake();
        Assert.Equal(2, _transport.Opens);
        Assert.True(first.Disposed);
        Assert.NotSame(first, _transport.Link);
    }

    [Fact]
    public void Wake_and_start_do_nothing_when_stopped_or_already_started()
    {
        var m = Make();
        m.Wake();
        Assert.Equal(0, _transport.Opens);
        m.Start();
        m.Start();
        Assert.Equal(1, _transport.Opens);
    }

    [Fact]
    public void An_open_failure_is_logged_and_leaves_the_module_closed()
    {
        var m = new YealinkModule(new ThrowingTransport(), _log.Add, () => false);
        m.Start();
        Assert.False(m.IsOpen);
        Assert.Contains(_log, x => x.Contains("open failed"));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_6993&pid_b0ae&mi_03&col02#7&1&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}", YealinkCollection.Telephony)]
    [InlineData(@"\\?\hid#vid_6993&pid_b0ae&mi_03&col04#7&1&0&0003#{4d1e55b2-f16f-11cf-88cb-001111000030}", YealinkCollection.Teams)]
    [InlineData(@"\\?\hid#vid_6993&pid_b0ae&mi_03&col01#7&1&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", YealinkCollection.Standard)]
    [InlineData(@"\\?\hid#vid_6993&pid_b0ae&mi_03&col05#7&1&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}", YealinkCollection.Vendor)]
    public void Wanted_interfaces_are_classified_from_the_path(string path, YealinkCollection expected)
    {
        Assert.Equal(expected, WindowsYealinkTransport.Classify(path));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_6993&pid_b0ae&mi_03&col03#7&1&0&0002#{x}")]
    [InlineData(@"\\?\hid#vid_6993&pid_b0ae&mi_02&col02#7&1&0&0000#{x}")]
    [InlineData(@"\\?\hid#vid_6993&pid_b0af&mi_03&col02#7&1&0&0000#{x}")]
    [InlineData(@"\\?\hid#vid_046d&pid_b0ae&mi_03&col02#7&1&0&0000#{x}")]
    public void Other_collections_and_devices_are_never_wanted(string path)
    {
        Assert.Null(WindowsYealinkTransport.Classify(path));
    }

    private sealed class ThrowingTransport : IYealinkTransport
    {
        public IYealinkLink? Open(bool writeLeds, bool vendorReads, YealinkReportHandler onReport, Action<string> log) => throw new IOException("denied");
    }
}
