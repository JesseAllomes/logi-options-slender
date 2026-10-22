using Slender.Engine.Devices;
using Slender.Yealink;

namespace Slender.Tests.Yealink;

/// <summary>Test fixtures: the Genesys bit layout (unverified on the WH64) as a caps fake. Product code never hard-codes it.</summary>
internal static class GenesysCaps
{
    public const byte ReportId = 0x04;

    public static CollectionCaps Create(bool withMuteLed = true)
    {
        var inputs = new Dictionary<ushort, UsageSlot>
        {
            [YealinkUsages.HookSwitch] = new(ReportId, 8),
            [YealinkUsages.PhoneMute] = new(ReportId, 10),
            [YealinkUsages.Flash] = new(ReportId, 11),
            [YealinkUsages.Reject] = new(ReportId, 14),
        };
        var outputs = new Dictionary<ushort, UsageSlot>
        {
            [(ushort)YealinkLed.OffHook] = new(ReportId, 8),
            [(ushort)YealinkLed.Ring] = new(ReportId, 10),
            [(ushort)YealinkLed.Hold] = new(ReportId, 11),
        };
        if (withMuteLed) outputs[(ushort)YealinkLed.Mute] = new(ReportId, 9);
        return new CollectionCaps(3, 2, inputs, outputs);
    }
}

public class YealinkCodecTests
{
    private static List<(YealinkEventKind Kind, bool Value)> Run(YealinkInputDecoder d, params byte[][] reports)
    {
        var events = new List<(YealinkEventKind, bool)>();
        foreach (var r in reports) d.DecodeTelephony(r, (k, v) => events.Add((k, v)));
        return events;
    }

    [Fact]
    public void Catalog_lists_the_headset_without_link_reporting()
    {
        var d = DeviceCatalog.All.Single(x => x.Id == "yealink-wh64");
        Assert.Equal("Yealink WH64", d.Name);
        Assert.False(d.ReportsLink);
        Assert.True(d.Matches(new HidDeviceKey(0x6993, 0xB0AE)));
        Assert.False(d.Matches(new HidDeviceKey(0x6993, 0xB0AF)));
    }

    [Fact]
    public void Hook_reports_only_changes()
    {
        var d = new YealinkInputDecoder(GenesysCaps.Create());
        var e = Run(d, [4, 0x00, 0], [4, 0x01, 0], [4, 0x01, 0], [4, 0x00, 0]);
        Assert.Equal([(YealinkEventKind.HookChanged, true), (YealinkEventKind.HookChanged, false)], e);
    }

    [Fact]
    public void Mute_flash_and_reject_fire_on_the_press_edge_only()
    {
        var d = new YealinkInputDecoder(GenesysCaps.Create());
        var e = Run(d, [4, 0x04, 0], [4, 0x04, 0], [4, 0x00, 0], [4, 0x04, 0], [4, 0x08, 0], [4, 0x40, 0]);
        Assert.Equal(
            [YealinkEventKind.MutePressed, YealinkEventKind.MutePressed, YealinkEventKind.FlashPressed, YealinkEventKind.RejectPressed],
            e.Select(x => x.Kind));
    }

    [Fact]
    public void Other_report_ids_and_short_reports_are_ignored()
    {
        var d = new YealinkInputDecoder(GenesysCaps.Create());
        var e = Run(d, [0xC8, 0x3B, 0xFF, 0xFF], [0xC8, 0x03, 0x01], [0x02, 0xFF, 0xFF], [4], []);
        Assert.Empty(e);
    }

    [Fact]
    public void Teams_button_fires_once_per_press()
    {
        var d = new YealinkInputDecoder(null);
        var n = new List<YealinkEventKind>();
        foreach (var r in new byte[][] { [0x9B, 0x01, 0x00], [0x9B, 0x01, 0x00], [0x9B, 0x00, 0x00], [0x9B, 0x01, 0x00], [0xC8, 0x01, 0x00], [0x9B] })
            d.DecodeTeams(r, (k, _) => n.Add(k));
        Assert.Equal([YealinkEventKind.TeamsButtonPressed, YealinkEventKind.TeamsButtonPressed], n);
    }

    [Fact]
    public void Missing_usages_are_skipped_not_guessed()
    {
        var d = new YealinkInputDecoder(new CollectionCaps(3, 2, new Dictionary<ushort, UsageSlot>(), new Dictionary<ushort, UsageSlot>()));
        Assert.Empty(Run(d, [4, 0xFF, 0xFF]));
    }

    [Fact]
    public void Led_builder_sets_each_allowed_led_on_the_output_report()
    {
        var b = YealinkLedBuilder.TryCreate(GenesysCaps.Create())!;
        Assert.Equal([4, 0], b.Report.ToArray());
        b.Set(YealinkLed.Mute, true);
        Assert.Equal([4, 0x02], b.Report.ToArray());
        b.Set(YealinkLed.OffHook, true);
        b.Set(YealinkLed.Ring, true);
        b.Set(YealinkLed.Hold, true);
        Assert.Equal([4, 0x0F], b.Report.ToArray());
        b.Set(YealinkLed.Mute, false);
        Assert.Equal([4, 0x0D], b.Report.ToArray());
        Assert.False(b.Get(YealinkLed.Mute));
        Assert.True(b.Get(YealinkLed.Ring));
    }

    [Theory]
    [InlineData(0x2F)]
    [InlineData(0x9E)] // telephony ringer
    [InlineData(0x21)] // microphone LED / flash
    [InlineData(0x0000)]
    [InlineData(0xFFFA)]
    public void Led_builder_refuses_usages_off_the_allowlist(int usage)
    {
        var b = YealinkLedBuilder.TryCreate(GenesysCaps.Create())!;
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Set((ushort)usage, true));
    }

    [Fact]
    public void Led_builder_ignores_slots_on_another_report_id_and_needs_a_mute_led()
    {
        Assert.Null(YealinkLedBuilder.TryCreate(GenesysCaps.Create(withMuteLed: false)));

        var outputs = new Dictionary<ushort, UsageSlot>
        {
            [(ushort)YealinkLed.Mute] = new(4, 9),
            [(ushort)YealinkLed.Ring] = new(7, 10),
        };
        var b = YealinkLedBuilder.TryCreate(new CollectionCaps(3, 2, new Dictionary<ushort, UsageSlot>(), outputs))!;
        Assert.Throws<InvalidOperationException>(() => b.Set(YealinkLed.Ring, true));
        Assert.Equal([4, 0], b.Report.ToArray());
    }
}
