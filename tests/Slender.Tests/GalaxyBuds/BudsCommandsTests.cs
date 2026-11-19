using Slender.Engine.Devices;
using Slender.GalaxyBuds;

namespace Slender.Tests.GalaxyBuds;

public sealed class BudsCommandsTests
{
    private static (byte Id, byte[] Payload) Build(string key, int raw, BudsModel? m = null)
    {
        Assert.True(BudsCommands.TryBuild(key, raw, m ?? BudsTestData.ModelFromEsu(), out byte id, out byte[] payload, out string? problem), problem);
        return (id, payload);
    }

    [Theory]
    [InlineData(GalaxyBudsSettingsCatalog.NoiseMode, 3, 120, 3)]
    [InlineData(GalaxyBudsSettingsCatalog.AmbientLevel, 2, 132, 2)]
    [InlineData(GalaxyBudsSettingsCatalog.NoiseOneBud, 1, 111, 1)]
    [InlineData(GalaxyBudsSettingsCatalog.VoiceDetect, 1, 122, 1)]
    [InlineData(GalaxyBudsSettingsCatalog.VoiceDetectTimeout, 2, 123, 2)]
    [InlineData(GalaxyBudsSettingsCatalog.SirenDetect, 1, 222, 1)]
    [InlineData(GalaxyBudsSettingsCatalog.EqPreset, 4, 134, 4)]
    [InlineData(GalaxyBudsSettingsCatalog.AdaptiveEq, 0, 218, 0)]
    [InlineData(GalaxyBudsSettingsCatalog.Balance, 20, 143, 20)]
    [InlineData(GalaxyBudsSettingsCatalog.Sidetone, 1, 139, 1)]
    [InlineData(GalaxyBudsSettingsCatalog.AutoPause, 1, 108, 1)]
    public void SingleByteSettings(string key, int raw, int id, int wire)
    {
        var (gotId, payload) = Build(key, raw);
        Assert.Equal(id, gotId);
        Assert.Equal(new[] { (byte)wire }, payload);
    }

    [Theory]
    [InlineData(GalaxyBudsSettingsCatalog.InEarCalls, 1, 110, 0)]
    [InlineData(GalaxyBudsSettingsCatalog.InEarCalls, 0, 110, 1)]
    [InlineData(GalaxyBudsSettingsCatalog.Seamless, 1, 175, 0)]
    [InlineData(GalaxyBudsSettingsCatalog.Seamless, 0, 175, 1)]
    public void InvertedSettingsGoOutInverted(string key, int raw, int id, int wire)
    {
        var (gotId, payload) = Build(key, raw);
        Assert.Equal(id, gotId);
        Assert.Equal(new[] { (byte)wire }, payload);
    }

    [Fact]
    public void TouchHoldKeepsTheOtherSide()
    {
        var m = BudsTestData.ModelFromEsu(); // 2 / 2
        Assert.Equal(new byte[] { 1, 2 }, Build(GalaxyBudsSettingsCatalog.TouchHoldLeft, 1, m).Payload);
        Assert.Equal(new byte[] { 2, 3 }, Build(GalaxyBudsSettingsCatalog.TouchHoldRight, 3, m).Payload);
        Assert.Equal(146, Build(GalaxyBudsSettingsCatalog.TouchHoldLeft, 1, m).Id);
    }

    [Fact]
    public void CycleKeepsTheOtherSide()
    {
        var m = BudsTestData.ModelFromEsu(); // 9 / 9
        Assert.Equal(new byte[] { 11, 9 }, Build(GalaxyBudsSettingsCatalog.CycleLeft, 11, m).Payload);
        Assert.Equal(new byte[] { 9, 12 }, Build(GalaxyBudsSettingsCatalog.CycleRight, 12, m).Payload);
        Assert.Equal(121, Build(GalaxyBudsSettingsCatalog.CycleLeft, 11, m).Id);
    }

    [Fact]
    public void SpatialAudioKeepsTheOtherToggle()
    {
        var m = BudsTestData.ModelFromEsu(); // 1 / 1
        Assert.Equal(new byte[] { 0, 1 }, Build(GalaxyBudsSettingsCatalog.Audio360, 0, m).Payload);
        Assert.Equal(new byte[] { 1, 0 }, Build(GalaxyBudsSettingsCatalog.HeadTracking, 0, m).Payload);
        Assert.Equal(124, Build(GalaxyBudsSettingsCatalog.Audio360, 0, m).Id);
    }

    [Fact]
    public void AmbientCustomKeepsEveryOtherField()
    {
        var m = BudsTestData.ModelFromEsu(); // on 0, volumes 1/1, tone 2
        Assert.Equal(new byte[] { 1, 1, 1, 2 }, Build(GalaxyBudsSettingsCatalog.AmbientCustom, 1, m).Payload);
        Assert.Equal(new byte[] { 0, 3, 1, 2 }, Build(GalaxyBudsSettingsCatalog.AmbientCustomLeft, 3, m).Payload);
        Assert.Equal(new byte[] { 0, 1, 4, 2 }, Build(GalaxyBudsSettingsCatalog.AmbientCustomRight, 4, m).Payload);
        Assert.Equal(new byte[] { 0, 1, 1, 4 }, Build(GalaxyBudsSettingsCatalog.AmbientCustomTone, 4, m).Payload);
        Assert.Equal(130, Build(GalaxyBudsSettingsCatalog.AmbientCustom, 1, m).Id);
    }

    [Fact]
    public void MultiFieldMessageNeedsTheCurrentState()
    {
        var empty = new BudsModel();
        Assert.False(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.CycleLeft, 11, empty, out _, out _, out string? problem));
        Assert.NotNull(problem);
        Assert.False(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.AmbientCustomTone, 1, empty, out _, out _, out _));
    }

    [Fact]
    public void EveryEditableCatalogKeyHasACommandThatIsAllowed()
    {
        var m = BudsTestData.ModelFromEsu();
        int built = 0;
        foreach (var def in GalaxyBudsSettingsCatalog.All)
        {
            int raw = def.IsChoice ? def.ChoiceRaw(0)!.Value : def.Min;
            bool ok = BudsCommands.TryBuild(def.Key, raw, m, out byte id, out byte[] payload, out _);
            Assert.Equal(def.CanSet, ok);
            if (!ok) continue;
            built++;
            Assert.True(BudsAllowlist.IsAllowed(id, payload.Length), def.Key);
        }

        Assert.Equal(GalaxyBudsSettingsCatalog.All.Count(d => d.CanSet), built);
    }

    [Fact]
    public void ReadOnlySettingsAndBadValuesAreRefused()
    {
        var m = BudsTestData.ModelFromEsu();
        Assert.False(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.Gestures, 0, m, out _, out _, out _));
        Assert.False(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.AdaptiveVolume, 1, m, out _, out _, out _));
        Assert.False(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.NoiseMode, 4, m, out _, out _, out _));
        Assert.False(BudsCommands.TryBuild(GalaxyBudsSettingsCatalog.Balance, 33, m, out _, out _, out _));
        Assert.False(BudsCommands.TryBuild("nope", 0, m, out _, out _, out _));
    }

    [Fact]
    public void FindAndStopAreAllowedEmpty()
    {
        Assert.True(BudsAllowlist.IsAllowed(BudsIds.FindStart, 0));
        Assert.True(BudsAllowlist.IsAllowed(BudsIds.FindStop, 0));
        Assert.False(BudsAllowlist.IsAllowed(BudsIds.FindStart, 1));
    }

    [Fact]
    public void KeepOutIdsAreNeverAllowed()
    {
        // The capability map's "Keep out" section: firmware (176-190), reset/reboot/power, pairing and profile control, factory and
        // test modes, core dump and log control, battery reset, Find-My keys, hearing data, touchpad lock, unsupported 72,
        // manager info and time, and the loud tests.
        var keepOut = new List<int> { 80, 82, 83, 114, 113, 112, 16, 18, 19, 21, 22, 23, 32, 86, 75, 171, 39, 48, 55, 58, 59, 60, 61, 207, 172, 173, 103, 204, 144, 72, 136, 167, 166, 157, 162 };
        keepOut.AddRange(Enumerable.Range(176, 15));
        foreach (int id in keepOut)
            for (int len = 0; len <= 16; len++)
                Assert.False(BudsAllowlist.IsAllowed((byte)id, len), $"id {id} len {len}");

        foreach (byte id in BudsAllowlist.AllowedIds)
            Assert.DoesNotContain((int)id, keepOut);
    }

    [Fact]
    public void AllowlistChecksTheLengthToo()
    {
        Assert.False(BudsAllowlist.IsAllowed(BudsIds.NoiseControls, 0));
        Assert.False(BudsAllowlist.IsAllowed(BudsIds.NoiseControls, 2));
        Assert.False(BudsAllowlist.IsAllowed(BudsIds.DebugSku, 1));
        Assert.True(BudsAllowlist.IsAllowed(BudsIds.DebugSku, 0));
    }
}
