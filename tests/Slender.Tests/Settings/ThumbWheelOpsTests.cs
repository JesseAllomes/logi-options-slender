using Slender.Engine;
using Slender.Hidpp;
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
    public void Browser_shows_the_preset_muted_text()
    {
        ThumbWheelView v = ThumbWheelOps.View(Cfg(Def(), App("msedge")), "msedge");
        Assert.Equal(ThumbWheelSource.Preset, v.Source);
        Assert.True(v.On);
        Assert.Equal("Browser preset: next tab / previous tab", ThumbWheelOps.InheritedText(v));
    }

    [Fact]
    public void App_follows_all_apps_before_the_preset()
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
    public void Turning_off_a_browser_overrides_the_preset_and_on_again_restores_it()
    {
        SlenderConfig off = ThumbWheelOps.TurnOffFor(Cfg(Def(), App("msedge")), "msedge");
        Assert.Null(ThumbWheelModes.SecondFor(off, "msedge"));
        SlenderConfig on = ThumbWheelOps.SetEnabled(off, "msedge", true);
        Assert.Equal(ThumbWheelModes.Tabs, ThumbWheelModes.SecondFor(on, "msedge"));
    }

    [Fact]
    public void Setting_a_notch_copies_the_inherited_role_and_changes_one_side()
    {
        SlenderConfig c = ThumbWheelOps.SetForward(Cfg(Def(), App("msedge")), "msedge", new ActionSpec.Keys("Ctrl+PageDown"));
        ThumbWheelSecond s = ThumbWheelOps.View(c, "msedge").Second!;
        Assert.Equal(new ActionSpec.Keys("Ctrl+PageDown"), s.Forward);
        Assert.Equal(ThumbWheelModes.Tabs.Back, s.Back);
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

    [Fact]
    public void Toggle_action_has_a_label()
    {
        Assert.Equal("Switch thumb wheel mode", ActionCatalog.Plain(new ActionSpec.ToggleThumbWheelMode()));
        Assert.Contains(ActionCatalog.ThumbModeOptions, o => o.Action is ActionSpec.ToggleThumbWheelMode);
    }

    private static readonly ActionSpec Switch = new ActionSpec.ToggleThumbWheelMode();

    private static Profile Bound(string? app, ushort id, ActionSpec action) =>
        new(app, new Dictionary<ushort, ActionSpec> { [id] = action });

    [Fact]
    public void Switch_candidates_leave_out_the_thumb_wheel()
    {
        Assert.DoesNotContain(ThumbWheelOps.SwitchCandidates, c => c.Id == DeviceIds.ThumbWheelPseudoId);
        Assert.Contains(ThumbWheelOps.SwitchCandidates, c => c.Id == ControlId.ModeShift);
        Assert.Contains(ThumbWheelOps.SwitchCandidates, c => c.Id == DeviceIds.ThumbWheelTapPseudoId);
    }

    [Fact]
    public void No_switch_control_when_nothing_holds_the_toggle()
    {
        Assert.Equal((null, false), ThumbWheelOps.SwitchControl(Cfg(Def(), App("excel")), "excel"));
    }

    [Fact]
    public void Switch_control_is_own_for_all_apps_and_inherited_for_an_app()
    {
        SlenderConfig c = Cfg(Bound(null, ControlId.ModeShift, Switch), App("excel"));
        Assert.Equal((ControlId.ModeShift, false), ThumbWheelOps.SwitchControl(c, null));
        Assert.Equal((ControlId.ModeShift, true), ThumbWheelOps.SwitchControl(c, "excel"));
    }

    [Fact]
    public void Switch_as_the_tap_of_a_press_hold_counts()
    {
        SlenderConfig c = Cfg(Bound(null, ControlId.Forward, new ActionSpec.TapHold(Switch, new ActionSpec.ActionsRing())));
        Assert.Equal((ControlId.Forward, false), ThumbWheelOps.SwitchControl(c, null));
    }

    [Fact]
    public void Set_switch_control_moves_the_toggle_to_the_new_button()
    {
        SlenderConfig c = ThumbWheelOps.SetSwitchControl(Cfg(Bound(null, ControlId.ModeShift, Switch)), null, ControlId.Forward);
        Assert.Equal((ControlId.Forward, false), ThumbWheelOps.SwitchControl(c, null));
        Assert.Null(ProfileOps.Own(c, null, DeviceKind.Mouse, ControlId.ModeShift));
        Assert.Same(c, ThumbWheelOps.SetSwitchControl(c, null, ControlId.Forward));
    }

    [Fact]
    public void Set_switch_control_in_an_app_overrides_the_inherited_button()
    {
        SlenderConfig c = ThumbWheelOps.SetSwitchControl(Cfg(Bound(null, ControlId.ModeShift, Switch), App("excel")), "excel", ControlId.Back);
        Assert.Equal((ControlId.Back, false), ThumbWheelOps.SwitchControl(c, "excel"));
        Assert.True(ProfileOps.IsPassThrough(c, "excel", DeviceKind.Mouse, ControlId.ModeShift));
        Assert.Equal((ControlId.ModeShift, false), ThumbWheelOps.SwitchControl(c, null));
    }

    [Fact]
    public void None_in_an_app_passes_the_inherited_button_through()
    {
        SlenderConfig c = ThumbWheelOps.SetSwitchControl(Cfg(Bound(null, ControlId.ModeShift, Switch), App("excel")), "excel", null);
        Assert.Equal((null, false), ThumbWheelOps.SwitchControl(c, "excel"));
        Assert.True(ProfileOps.IsPassThrough(c, "excel", DeviceKind.Mouse, ControlId.ModeShift));
        Assert.Equal((ControlId.ModeShift, false), ThumbWheelOps.SwitchControl(c, null));
    }

    [Fact]
    public void None_for_all_apps_clears_the_toggle()
    {
        SlenderConfig c = ThumbWheelOps.SetSwitchControl(Cfg(Bound(null, ControlId.ModeShift, Switch)), null, null);
        Assert.Equal((null, false), ThumbWheelOps.SwitchControl(c, null));
    }
}
