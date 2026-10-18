using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Tests.Engine;

public sealed class ThumbWheelModesTests
{
    [Theory]
    [InlineData("msedge")]
    [InlineData("chrome.exe")]
    [InlineData("excel")]
    [InlineData(null)]
    public void Nothing_is_set_by_default(string? process) =>
        Assert.Null(ThumbWheelModes.SecondFor(SlenderConfig.Default, process));

    [Fact]
    public void App_setting_beats_default_profile_and_off_means_off()
    {
        var zoom = new ThumbWheelSecond(true, new ActionSpec.Keys("Ctrl+Plus"), new ActionSpec.Keys("Ctrl+Minus"));
        var vol = new ThumbWheelSecond(true, new ActionSpec.Keys("VolumeUp"), new ActionSpec.Keys("VolumeDown"));
        var off = new ThumbWheelSecond(false, new ActionSpec.None(), new ActionSpec.None());
        SlenderConfig config = SlenderConfig.Default with
        {
            Profiles =
            [
                new Profile(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = vol },
                new Profile("msedge", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = zoom },
                new Profile("island", new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = off },
            ],
        };
        Assert.Equal(zoom, ThumbWheelModes.SecondFor(config, "msedge"));
        Assert.Equal(vol, ThumbWheelModes.SecondFor(config, "chrome")); // no setting of its own: the default profile's
        Assert.Null(ThumbWheelModes.SecondFor(config, "island"));       // turned off for this app
    }

    [Fact]
    public void Second_role_round_trips()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-thumb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var second = new ThumbWheelSecond(true, new ActionSpec.Launch("calc.exe"), new ActionSpec.Keys("Ctrl+Shift+Tab"));
            SlenderConfig config = SlenderConfig.Default with
            {
                Profiles =
                [
                    new Profile(null, new Dictionary<ushort, ActionSpec>()),
                    new Profile("msedge", new Dictionary<ushort, ActionSpec> { [Slender.Hidpp.ControlId.ModeShift] = new ActionSpec.Keys("Ctrl+C") })
                    {
                        ThumbWheelAlt = second,
                    },
                ],
            };
            var store = new JsonConfigStore(dir);
            store.Save(config);
            Profile edge = store.Load().Profiles.Single(p => p.AppProcess == "msedge");
            Assert.Equal(new ActionSpec.Keys("Ctrl+C"), edge.Bindings[Slender.Hidpp.ControlId.ModeShift]);
            Assert.Equal(second, edge.ThumbWheelAlt);
            Assert.Null(store.Load().Profiles.Single(p => p.AppProcess is null).ThumbWheelAlt);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
