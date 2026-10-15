using Slender.Hidpp;
using Slender.Hidpp.Inventory;

namespace Slender.Tests.Hidpp;

public class InventoryTests
{
    private static readonly FeatureTypeFlags[] FlagSamples =
    [
        new(0), new(0x20), new(0x10), new(0x30), new(0x40), new(0x80), new(0x08), new(0xFF),
    ];

    [Fact]
    public void Table_and_never_call_do_not_overlap()
    {
        foreach (var g in InventoryGetters.Table)
            Assert.DoesNotContain(g.FeatureId, InventoryGetters.NeverCall);
    }

    [Fact]
    public void Never_call_set_has_the_required_ids()
    {
        ushort[] required = [0x00C2, 0x00C3, 0x00D0, 0x00D1, 0x1802, 0x1803, 0x1805, 0x1806, 0x1814, 0x1815, 0x1E00, 0x1F1F, 0x18A1, 0x8100, 0x8110];
        foreach (var id in required) Assert.Contains(id, InventoryGetters.NeverCall);
        for (ushort id = 0x1E01; id <= 0x1E22; id++) Assert.Contains(id, InventoryGetters.NeverCall);
    }

    [Fact]
    public void Planner_never_schedules_outside_the_table_or_inside_never_call()
    {
        // A device could report any feature id with any flags; check every id.
        var replies = new Dictionary<byte, IReadOnlyList<byte[]>>();
        for (byte fn = 0; fn < 16; fn++) replies[fn] = [Enumerable.Repeat((byte)3, 16).ToArray()];

        for (int id = 0; id <= 0xFFFF; id++)
        {
            foreach (var flags in FlagSamples)
            {
                var plan = InventoryPlanner.Plan((ushort)id, flags);
                if (InventoryGetters.NeverCall.Contains((ushort)id) || flags.EngineeringOrManufacturing)
                {
                    Assert.Equal(PlanDisposition.ListedOnly, plan.Disposition);
                    Assert.Empty(plan.Getters);
                    continue;
                }
                foreach (var g in plan.Getters)
                {
                    Assert.Equal((ushort)id, g.FeatureId);
                    Assert.True(InventoryGetters.IsInTable(g.FeatureId, g.Function));
                    Assert.DoesNotContain(g.FeatureId, InventoryGetters.NeverCall);
                    foreach (var call in InventoryPlanner.Expand(g, replies))
                        InventoryGetters.EnsureAllowed(g.FeatureId, call.Getter.Function, flags); // must not throw
                }
            }
        }
    }

    [Fact]
    public void Guard_blocks_never_call_flagged_and_unlisted_functions()
    {
        foreach (var id in InventoryGetters.NeverCall)
            for (byte fn = 0; fn < 16; fn++)
                Assert.Throws<InventoryBlockedException>(() => InventoryGetters.EnsureAllowed(id, fn));

        Assert.Throws<InventoryBlockedException>(() => InventoryGetters.EnsureAllowed(0x1B04, 3)); // setCidReporting is not a getter
        Assert.Throws<InventoryBlockedException>(() => InventoryGetters.EnsureAllowed(0x2201, 3)); // setSensorDpi
        Assert.Throws<InventoryBlockedException>(() => InventoryGetters.EnsureAllowed(0x1C00, 4)); // setMapping
        Assert.Throws<InventoryBlockedException>(() => InventoryGetters.EnsureAllowed(0x9999, 0));
        Assert.Throws<InventoryBlockedException>(() => InventoryGetters.EnsureAllowed(0x1000, 0, new FeatureTypeFlags(0x20)));
        InventoryGetters.EnsureAllowed(0x1000, 0);
    }

    [Theory]
    [InlineData((byte)0x80, "obsolete")]
    [InlineData((byte)0x40, "hidden")]
    [InlineData((byte)0x20, "engineering")]
    [InlineData((byte)0x10, "manufacturing-deactivatable")]
    [InlineData((byte)0x08, "compliance-deactivatable")]
    public void Type_flag_bits_decode(byte raw, string expected)
    {
        Assert.Equal([expected], new FeatureTypeFlags(raw).Names());
    }

    [Fact]
    public void Engineering_and_manufacturing_are_listed_only_flags()
    {
        Assert.True(new FeatureTypeFlags(0x20).EngineeringOrManufacturing);
        Assert.True(new FeatureTypeFlags(0x10).EngineeringOrManufacturing);
        Assert.False(new FeatureTypeFlags(0xC8).EngineeringOrManufacturing);
        Assert.Empty(new FeatureTypeFlags(0).Names());
    }

    [Fact]
    public void Control_flags_decode()
    {
        Assert.Equal(["divertable", "persistent-divert", "raw-xy", "analytics-key-events"], InventoryGetters.DecodeKeyFlags(0x20 | 0x40 | 0x100 | 0x400));
        Assert.Equal(["divertable", "persistent-divert", "virtual", "force-raw-xy"], InventoryGetters.DecodeKeyFlags(0x20 | 0x40 | 0x80 | 0x200));
        Assert.Equal(["diverted", "raw-xy-diverted", "analytics-key-events-reporting"], InventoryGetters.DecodeMappingFlags(0x01 | 0x10 | 0x100));
    }

    [Fact]
    public void Options_plus_slots_label_controls_and_settings()
    {
        var m = new OptionsPlusMetadata();
        m.ParseJson("""
            {"a":[{"slotId":"ergo-k860-6b359_c199","slotName":"SLOT_NAME_BRIGHTNESS_DOWN"},
                  {"slotId":"ergo-k860-6b359_haptic_settings","slotName":"SLOT_NAME_X"},
                  {"slotId":"other-device_c5","slotName":"SLOT_NAME_NOPE"}]}
            """, "ergo-k860-6b359");
        Assert.Equal("Brightness Down", m.CidLabels[199]);
        Assert.Single(m.CidLabels);
        Assert.Equal("ergo-k860-6b359_haptic_settings", m.FeatureSlots[0x19B0]);
    }

    // ---------------------------------------------------------------- scripted device

    private sealed class ScriptedDevice
    {
        // index -> feature id, type flags, version
        public readonly (ushort Id, byte Flags, byte Version)[] Features =
        [
            (0x0001, 0, 2),      // 1
            (0x0003, 0, 3),      // 2
            (0x1B04, 0, 4),      // 3
            (0x00D0, 0, 1),      // 4 DFU: never call
            (0x1814, 0, 1),      // 5 change host: never call
            (0x8100, 0, 1),      // 6 onboard profiles: never call
            (0x1000, 0x20, 1),   // 7 battery flagged engineering: listed only
            (0x9999, 0, 1),      // 8 unknown id
            (0x1E05, 0, 1),      // 9 engineering block
        ];

        public readonly List<byte[]> Requests = [];

        public IEnumerable<byte[]> Handle(byte[] w)
        {
            lock (Requests) Requests.Add(w);
            byte dev = w[1], idx = w[2], fn = (byte)(w[3] >> 4), sw = (byte)(w[3] & 0xF);
            byte[] p = w[4..];
            if (idx == 0 && fn == 1)
            {
                // A reply meant for the tray (SwId 0x0B) arrives first and must be ignored.
                return [HidppMessage.Encode(dev, 0, 1, 0x0B, [4, 2, p[2]]), HidppMessage.Encode(dev, 0, 1, sw, [4, 2, p[2]])];
            }
            if (idx == 0 && fn == 0)
            {
                ushort id = (ushort)((p[0] << 8) | p[1]);
                int pos = Array.FindIndex(Features, f => f.Id == id);
                return [HidppMessage.Encode(dev, 0, 0, sw, [(byte)(id == 0x0001 ? 1 : pos + 1), 0, 1])];
            }
            if (idx == 1)
            {
                if (fn == 0) return [HidppMessage.Encode(dev, 1, 0, sw, [(byte)Features.Length])];
                var f = Features[p[0] - 1];
                return [HidppMessage.Encode(dev, 1, 1, sw, [(byte)(f.Id >> 8), (byte)f.Id, f.Flags, f.Version])];
            }
            if (idx == 2)
            {
                if (fn == 0) return [HidppMessage.Encode(dev, 2, 0, sw, [1])];
                return [HidppMessage.Encode(dev, 2, 1, sw, [0, (byte)'A', (byte)'B', (byte)'C', 0x12, 0x34, 0x00, 0x56, 1])];
            }
            if (idx == 3)
            {
                if (fn == 0) return [HidppMessage.Encode(dev, 3, 0, sw, [2])];
                if (fn == 1)
                    return [p[0] == 0
                        ? HidppMessage.Encode(dev, 3, 1, sw, [0x00, 0x52, 0x00, 0x38, 0x70, 1, 1, 1, 0x01])
                        : HidppMessage.Encode(dev, 3, 1, sw, [0x00, 0xC3, 0x00, 0x9C, 0x30, 2, 1, 1, 0x00])];
                if (fn == 2)
                {
                    if (p[1] == 0xC3) return [Error(dev, 3, 2, sw, 0x05)];
                    return [HidppMessage.Encode(dev, 3, 2, sw, [p[0], p[1], 0x01, 0, 0, 0])];
                }
            }
            return []; // anything else would time out: a request to a never-call feature fails the test via Requests anyway
        }

        private static byte[] Error(byte dev, byte feature, byte fn, byte sw, byte code)
        {
            var b = new byte[20];
            b[0] = 0x11;
            b[1] = dev;
            b[2] = 0xFF;
            b[3] = feature;
            b[4] = (byte)((fn << 4) | sw);
            b[5] = code;
            return b;
        }
    }

    private static async Task<(DeviceInventory Inv, ScriptedDevice Dev)> RunScriptedAsync()
    {
        var dev = new ScriptedDevice();
        var transport = new FakeTransport { OnWrite = dev.Handle };
        using var channel = new HidppChannel(transport, new HidppChannelOptions
        {
            SwId = InventoryOptions.InventorySwId,
            RequestTimeout = TimeSpan.FromMilliseconds(100),
            MaxRetries = 0,
            ReadPollMs = 20,
        });
        channel.Start();
        var inv = await new InventoryRunner(channel, 0xFF, new InventoryOptions { RequestTimeout = TimeSpan.FromMilliseconds(100), MaxRetries = 0 })
            .RunAsync("scripted", CancellationToken.None);
        return (inv, dev);
    }

    [Fact]
    public async Task Scripted_device_lists_never_call_features_without_sending_to_them()
    {
        var (inv, dev) = await RunScriptedAsync();

        // Only indices 0 (IRoot), 1 (IFeatureSet), 2 (0x0003) and 3 (0x1B04) were ever addressed, always with SwId 0x0C.
        lock (dev.Requests)
        {
            Assert.All(dev.Requests, w =>
            {
                Assert.True(w[2] <= 3, $"request to feature index {w[2]}");
                Assert.Equal(0x0C, w[3] & 0x0F);
            });
        }

        Assert.Equal("4.2", inv.ProtocolVersion);
        Assert.Equal(9, inv.FeatureCount);
        Assert.Equal(10, inv.Features.Count); // IRoot + 9

        foreach (ushort id in new ushort[] { 0x00D0, 0x1814, 0x8100, 0x1E05, 0x1000, 0x9999 })
        {
            var f = inv.Features.Single(x => x.Id == id);
            Assert.False(f.Called);
            Assert.Equal("listed-only", f.Disposition);
            Assert.Empty(f.Calls);
            Assert.NotEmpty(f.Reason);
        }
        Assert.Contains("engineering", inv.Features.Single(x => x.Id == 0x1000).Flags);

        var md = InventoryReport.ToMarkdown(inv);
        Assert.Contains("listed-only", md);
        Assert.Contains("0x00D0", md);
        Assert.Contains("0x1814", md);
        Assert.Contains("0x8100", md);
        Assert.Contains("never-call list", md);
        Assert.Contains("flagged engineering/manufacturing", md);
        Assert.Contains("no vetted read-only getter", md);
        Assert.Contains("\"disposition\": \"listed-only\"", InventoryReport.ToJson(inv));
    }

    [Fact]
    public async Task Scripted_device_firmware_and_controls_decode_and_errors_do_not_stop_the_run()
    {
        var (inv, _) = await RunScriptedAsync();

        var fw = Assert.Single(inv.Firmware);
        Assert.Equal("firmware", fw.Kind);
        Assert.Equal("ABC", fw.Name);
        Assert.Equal("12.34", fw.Version);
        Assert.True(fw.Active);

        Assert.Equal(2, inv.Cids.Count);
        var middle = inv.Cids.Single(c => c.Cid == 0x52);
        Assert.Equal("Middle Button", middle.Name);
        Assert.Contains("divertable", middle.FlagNames);
        Assert.Contains("raw-xy", middle.FlagNames);
        Assert.NotNull(middle.Reporting);

        var gesture = inv.Cids.Single(c => c.Cid == 0xC3);
        Assert.Null(gesture.Reporting); // getCidReporting answered with a HID++ error
        var reprog = inv.Features.Single(f => f.Id == 0x1B04);
        var failed = reprog.Calls.Single(c => c.Status == "hid-error");
        Assert.Equal(0x05, failed.ErrorCode);
        Assert.Equal(1, inv.ErrorCount);
        Assert.Null(inv.Aborted);
    }
}
