using Slender.Engine.Devices;
using Slender.GalaxyBuds;

namespace Slender.Tests.GalaxyBuds;

/// <summary>1.1.58 read-only additions: battery cycles (206), multipoint info (118), noise-cancelling strength (ESU[24]), and the
/// siren-detect write (222) checked against the captured frames.</summary>
public sealed class BudsInfoTests
{
    private static BudsFrame Decode(byte[] bytes)
    {
        var frames = new List<BudsFrame>();
        var reader = new BudsFrameReader();
        reader.Feed(bytes, frames);
        Assert.Equal(0, reader.Dropped);
        return Assert.Single(frames);
    }

    private static string HexOf(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    [Fact]
    public void ReadRequestsMatchTheCapturedFrames()
    {
        Assert.Equal(BudsTestData.MultipointRequestFrameHex, HexOf(BudsCodec.Encode(BudsIds.MultipointInfo, [])));
        Assert.Equal(BudsTestData.CyclesRequestFrameHex, HexOf(BudsCodec.Encode(BudsIds.BatteryCycles, [])));
    }

    [Fact]
    public void AnswersWithTheRealHeaderFlagsDecode()
    {
        var mp = Decode(BudsTestData.FrameWithHeader(BudsTestData.MultipointHeader, 118, BudsTestData.Hex(BudsTestData.MultipointPayloadHex)));
        Assert.Equal(BudsIds.MultipointInfo, mp.Id);
        Assert.Equal(8, mp.Payload.Length);
        var cy = Decode(BudsTestData.FrameWithHeader(BudsTestData.CyclesHeader, 206, BudsTestData.Hex(BudsTestData.CyclesPayloadHex)));
        Assert.Equal(BudsIds.BatteryCycles, cy.Id);
        Assert.Equal(16, cy.Payload.Length);
    }

    [Fact]
    public void BatteryCyclesAreTwoBigEndianCountsInTenThousandths()
    {
        var m = new BudsModel();
        Assert.True(BudsStatusParser.Apply(m, new BudsFrame(BudsIds.BatteryCycles, BudsTestData.Hex(BudsTestData.CyclesPayloadHex))));
        Assert.Equal(2.62, m.CyclesLeft!.Value, 6);
        Assert.Equal(20.39, m.CyclesRight!.Value, 6);
        var s = m.ToState(true, false, null);
        Assert.Equal(2.62, s.BatteryCyclesLeft!.Value, 6);
        Assert.Equal(20.39, s.BatteryCyclesRight!.Value, 6);
    }

    [Fact]
    public void BatteryCyclesOfTheWrongLengthOrOutOfRangeAreIgnored()
    {
        var m = new BudsModel();
        BudsStatusParser.Apply(m, new BudsFrame(BudsIds.BatteryCycles, BudsTestData.Hex(BudsTestData.CyclesPayloadHex)[..15]));
        Assert.Null(m.CyclesLeft);
        Assert.Null(m.CyclesRight);

        var bad = new byte[16];
        Array.Fill(bad, (byte)0xFF, 0, 8);             // left: -1
        bad[8] = 0x7F;                                  // right: huge
        BudsStatusParser.Apply(m, new BudsFrame(BudsIds.BatteryCycles, bad));
        Assert.Null(m.CyclesLeft);
        Assert.Null(m.CyclesRight);
    }

    [Fact]
    public void MultipointRevisionOneIsRead()
    {
        var m = new BudsModel();
        Assert.True(BudsStatusParser.Apply(m, new BudsFrame(BudsIds.MultipointInfo, BudsTestData.Hex(BudsTestData.MultipointPayloadHex))));
        Assert.True(m.MultipointSupported);
        Assert.Equal(0, m.MultipointStreaming);

        BudsStatusParser.Apply(m, new BudsFrame(BudsIds.MultipointInfo, [1, 1, 0, 0, 3, 1, 0, 0]));
        Assert.Equal(3, m.ToState(true, false, null).MultipointStreaming);
        Assert.True(m.ToState(true, false, null).MultipointSupported);
    }

    [Fact]
    public void MultipointOtherRevisionOrShortIsUnknown()
    {
        var m = new BudsModel();
        BudsStatusParser.Apply(m, new BudsFrame(BudsIds.MultipointInfo, BudsTestData.Hex(BudsTestData.MultipointPayloadHex)));
        BudsStatusParser.Apply(m, new BudsFrame(BudsIds.MultipointInfo, [2, 1, 0, 0, 1, 0, 0, 0]));
        Assert.Null(m.MultipointSupported);
        Assert.Null(m.MultipointStreaming);
        BudsStatusParser.Apply(m, new BudsFrame(BudsIds.MultipointInfo, [1, 1, 0, 0]));
        Assert.Null(m.MultipointSupported);
    }

    [Fact]
    public void NoiseCancellingStrengthIsEsuByte24()
    {
        var baseline = new BudsModel();
        BudsStatusParser.Apply(baseline, new BudsFrame(97, BudsTestData.Hex(BudsTestData.EsuBaselinePayloadHex)));
        Assert.Equal(4, baseline.Get(GalaxyBudsSettingsCatalog.NoiseCancellingStrength));

        var after = new BudsModel();
        BudsStatusParser.Apply(after, new BudsFrame(97, BudsTestData.Hex(BudsTestData.EsuAnc3PayloadHex)));
        Assert.Equal(3, after.Get(GalaxyBudsSettingsCatalog.NoiseCancellingStrength));

        var setting = after.ToState(true, false, null).Settings!.Single(x => x.Key == GalaxyBudsSettingsCatalog.NoiseCancellingStrength);
        Assert.Equal("3", setting.Value);
        Assert.Equal(GalaxyBudsSettingsCatalog.GroupNoise, setting.Group);
    }

    [Fact]
    public void NoiseCancellingStrengthIsNeverWritten()
    {
        var m = BudsTestData.ModelFromEsu();
        var def = GalaxyBudsSettingsCatalog.Find(GalaxyBudsSettingsCatalog.NoiseCancellingStrength)!;
        Assert.False(def.CanSet);
        for (int raw = -1; raw <= 5; raw++)
            Assert.False(BudsCommands.TryBuild(def.Key, raw, m, out _, out _, out _));
        for (int len = 0; len <= 4; len++)
            Assert.False(BudsAllowlist.IsAllowed(131, len));
    }

    [Fact]
    public void SirenDetectWritesId222AndReadsEsuByte52()
    {
        var m = BudsTestData.ModelFromEsu();
        Assert.True(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.SirenDetect, 1, m, out byte id, out byte[] payload, out _));
        Assert.Equal(BudsTestData.SirenOnFrameHex, HexOf(BudsCodec.Encode(id, payload)));
        Assert.True(BudsAllowlist.IsAllowed(id, payload.Length));
        Assert.True(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.SirenDetect, 0, m, out id, out payload, out _));
        Assert.Equal(BudsTestData.SirenOffFrameHex, HexOf(BudsCodec.Encode(id, payload)));

        // The captured acks name 222 first, which is what the module waits for.
        Assert.Equal(222, BudsTestData.Hex(BudsTestData.SirenOnAckPayloadHex)[0]);
        Assert.Equal(222, BudsTestData.Hex(BudsTestData.SirenOffAckPayloadHex)[0]);

        var on = new BudsModel();
        BudsStatusParser.Apply(on, new BudsFrame(97, BudsTestData.Hex(BudsTestData.EsuSirenOnPayloadHex)));
        Assert.Equal(1, on.Get(GalaxyBudsSettingsCatalog.SirenDetect));
        var off = new BudsModel();
        BudsStatusParser.Apply(off, new BudsFrame(97, BudsTestData.Hex(BudsTestData.EsuBaselinePayloadHex)));
        Assert.Equal(0, off.Get(GalaxyBudsSettingsCatalog.SirenDetect));
    }

    [Fact]
    public void ReadRequestsAreAllowedOnlyEmptyAndTheResetNever()
    {
        foreach (byte id in new[] { BudsIds.MultipointInfo, BudsIds.BatteryCycles })
        {
            Assert.True(BudsAllowlist.IsAllowed(id, 0));
            Assert.True(BudsAllowlist.IsReadRequest(id));
            for (int len = 1; len <= 16; len++) Assert.False(BudsAllowlist.IsAllowed(id, len));
        }

        for (int len = 0; len <= 16; len++) Assert.False(BudsAllowlist.IsAllowed(207, len));
        Assert.False(BudsAllowlist.IsReadRequest(207));
        Assert.False(BudsAllowlist.IsReadRequest(BudsIds.SirenDetect));
    }

    [Fact]
    public void CatalogIndexesShippedIn1157AreUnchangedAndStrengthIsAppended()
    {
        string[] shipped =
        [
            "noise_mode", "ambient_level", "noise_one_bud", "voice_detect", "voice_detect_timeout", "siren_detect",
            "touch_hold_left", "touch_hold_right", "cycle_left", "cycle_right", "gestures",
            "eq_preset", "adaptive_eq", "balance", "audio_360", "head_tracking", "adaptive_volume",
            "ambient_custom", "ambient_custom_left", "ambient_custom_right", "ambient_custom_tone",
            "sidetone", "in_ear_calls", "seamless", "auto_pause",
        ];
        for (int i = 0; i < shipped.Length; i++)
            Assert.Equal(shipped[i], GalaxyBudsSettingsCatalog.All[i].Key);
        Assert.Equal(shipped.Length + 1, GalaxyBudsSettingsCatalog.All.Count);
        Assert.Equal(GalaxyBudsSettingsCatalog.All.Count - 1, GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.NoiseCancellingStrength));
        var def = GalaxyBudsSettingsCatalog.All[^1];
        Assert.Equal(GalaxyBudsSettingsCatalog.GroupNoise, def.Group);
        Assert.False(def.Editable);
        Assert.Contains("Shown only", def.Help);
    }
}
