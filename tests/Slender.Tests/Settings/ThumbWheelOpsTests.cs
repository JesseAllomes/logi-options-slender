using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class ThumbWheelOpsTests
{
    private static readonly ThumbWheelSecond Zoom = new(true, new ActionSpec.Keys("Ctrl+Plus"), new ActionSpec.Keys("Ctrl+Minus"));

    private static SlenderConfig Cfg(params Profile[] profiles) => SlenderConfig.Default with { Profiles = profiles };

    private static Profile Def(ThumbWheelSecond? alt = null) => new(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = alt };

    private static Profile App(string app, ThumbWheelSecond? alt = null) => new(app, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = alt };

    [Fact]
    public void Nothing_set_means_no_second_role()
    {
        ThumbWheelView v = ThumbWheelOps.View(Cfg(Def(), App("excel")), "excel");
        Assert.Equal(ThumbWheelSource.None, v.Source);
        Assert.False(v.On);
        Assert.Null(ThumbWheelOps.InheritedText(v));
    }

    [Fact]
    public void Browsers_have_no_preset()
    {
        ThumbWheelView v = ThumbWheelOps.View(Cfg(Def(), App("msedge")), "msedge");
        Assert.Equal(ThumbWheelSource.None, v.Source);
        Assert.False(v.On);
    }

    [Fact]
    public void App_follows_all_apps()
    {
        SlenderConfig c = Cfg(Def(Zoom), App("msedge"), App("excel"));
        Assert.Equal(ThumbWheelSource.AllApps, ThumbWheelOps.View(c, "msedge").Source);
        Assert.Equal("Same as All apps", ThumbWheelOps.InheritedText(ThumbWheelOps.View(c, "excel")));
        Assert.Equal(Zoom, ThumbWheelOps.View(c, "excel").Second);
    }

    [Fact]
    public void Turn_off_for_an_app_stores_enabled_false_in_its_own_profile_only()
    {
        SlenderConfig c = ThumbWheelOps.TurnOffFor(Cfg(Def(Zoom), App("excel")), "excel");
        Assert.False(ThumbWheelOps.View(c, "excel").On);
        Assert.Equal(ThumbWheelSource.Own, ThumbWheelOps.View(c, "excel").Source);
        Assert.Null(ThumbWheelModes.SecondFor(c, "excel"));
        Assert.Equal(Zoom, c.Profiles.Single(p => p.AppProcess is null).ThumbWheelAlt);
    }

    [Fact]
    public void Turning_off_then_on_again_keeps_the_notches()
    {
        SlenderConfig off = ThumbWheelOps.TurnOffFor(Cfg(Def(), App("msedge", Zoom)), "msedge");
        Assert.Null(ThumbWheelModes.SecondFor(off, "msedge"));
        SlenderConfig on = ThumbWheelOps.SetEnabled(off, "msedge", true);
        Assert.Equal(Zoom, ThumbWheelModes.SecondFor(on, "msedge"));
    }

    [Fact]
    public void Setting_a_notch_copies_the_inherited_role_and_changes_one_side()
    {
        SlenderConfig c = ThumbWheelOps.SetForward(Cfg(Def(Zoom), App("msedge")), "msedge", new ActionSpec.Keys("Ctrl+PageDown"));
        ThumbWheelSecond s = ThumbWheelOps.View(c, "msedge").Second!;
        Assert.Equal(new ActionSpec.Keys("Ctrl+PageDown"), s.Forward);
        Assert.Equal(Zoom.Back, s.Back);
        Assert.Equal(ThumbWheelSource.Own, ThumbWheelOps.View(c, "msedge").Source);
    }

    [Fact]
    public void Enabling_an_app_with_nothing_starts_unassigned_and_creates_the_profile()
    {
        SlenderConfig c = ThumbWheelOps.SetEnabled(Cfg(Def()), "notepad", true);
        ThumbWheelView v = ThumbWheelOps.View(c, "notepad");
        Assert.True(v.On);
        Assert.IsType<ActionSpec.None>(v.Second!.Forward);
        Assert.IsType<ActionSpec.None>(v.Second.Back);
        Assert.Contains(c.Profiles, p => p.AppProcess == "notepad");
    }

    [Fact]
    public void Customise_and_follow_inherited_round_trip()
    {
        SlenderConfig c = ThumbWheelOps.Customise(Cfg(Def(Zoom), App("excel")), "excel");
        Assert.Equal(ThumbWheelSource.Own, ThumbWheelOps.View(c, "excel").Source);
        Assert.Equal(Zoom, ThumbWheelOps.View(c, "excel").Second);
        SlenderConfig back = ThumbWheelOps.FollowInherited(c, "excel");
        Assert.Equal(ThumbWheelSource.AllApps, ThumbWheelOps.View(back, "excel").Source);
        Assert.Same(back, ThumbWheelOps.FollowInherited(back, null));
    }

    [Fact]
    public void All_apps_edits_the_default_profile()
    {
        SlenderConfig c = ThumbWheelOps.SetBack(ThumbWheelOps.SetForward(Cfg(), null, new ActionSpec.Keys("VolumeUp")), null, new ActionSpec.Keys("VolumeDown"));
        Assert.Equal(ThumbWheelSource.Own, ThumbWheelOps.View(c, null).Source);
        Assert.Equal(new ActionSpec.Keys("VolumeDown"), c.Profiles.Single(p => p.AppProcess is null).ThumbWheelAlt!.Back);
    }
}
