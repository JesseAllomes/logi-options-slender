using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class ThumbCardModelTests
{
    private static readonly ThumbWheelSecond Tabs = new(true, new ActionSpec.Keys("Ctrl+Tab"), new ActionSpec.Keys("Ctrl+Shift+Tab"));

    private static SlenderConfig Cfg(params Profile[] profiles) => SlenderConfig.Default with { Profiles = profiles };

    private static Profile Def(ThumbWheelSecond? alt = null, int? side = null) => new(null, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = alt, SideScrollSpeed = side };

    private static Profile App(string app, ThumbWheelSecond? alt = null, int? side = null) => new(app, new Dictionary<ushort, ActionSpec>()) { ThumbWheelAlt = alt, SideScrollSpeed = side };

    [Theory]
    [InlineData(25, 0)]
    [InlineData(100, 3)]
    [InlineData(400, 6)]
    [InlineData(120, 3)]
    [InlineData(130, 4)]
    [InlineData(300, 5)]
    [InlineData(10, 0)]
    public void Notch_index_is_the_nearest_notch(int speed, int index) => Assert.Equal(index, ThumbCardModel.NotchIndex(speed));

    [Fact]
    public void Side_speed_is_inherited_until_the_app_sets_its_own()
    {
        SlenderConfig c = Cfg(Def(side: 150), App("edge"));
        Assert.Equal(new ThumbSpeedView(150, true), ThumbCardModel.SideSpeed(c, "edge"));
        Assert.Equal(new ThumbSpeedView(150, false), ThumbCardModel.SideSpeed(c, null));

        SlenderConfig own = ThumbCardModel.SetSideSpeed(c, "edge", 50);
        Assert.Equal(new ThumbSpeedView(50, false), ThumbCardModel.SideSpeed(own, "edge"));
        Assert.Equal(150, ThumbCardModel.SideSpeed(own, null).Value);
    }

    [Fact]
    public void Setting_a_speed_creates_the_profile_and_clamps()
    {
        SlenderConfig c = ThumbCardModel.SetSideSpeed(Cfg(Def()), "notepad", 9000);
        Assert.Equal(PointerSettings.SpeedMax, c.Profiles.Single(p => p.AppProcess == "notepad").SideScrollSpeed);
    }

    [Fact]
    public void Second_speed_copies_the_inherited_action_into_the_app()
    {
        SlenderConfig c = Cfg(Def(Tabs), App("excel"));
        Assert.True(ThumbCardModel.SecondSpeed(c, "excel").Inherited);

        SlenderConfig own = ThumbCardModel.SetSecondSpeed(c, "excel", 200);
        ThumbWheelSecond s = own.Profiles.Single(p => p.AppProcess == "excel").ThumbWheelAlt!;
        Assert.Equal(200, s.Speed);
        Assert.Equal(Tabs.Forward, s.Forward);
        Assert.Equal(new ThumbSpeedView(200, false), ThumbCardModel.SecondSpeed(own, "excel"));
        Assert.Null(own.Profiles.Single(p => p.AppProcess is null).ThumbWheelAlt!.Speed);
    }

    [Fact]
    public void Turn_chips_follow_the_thumb_wheel_binding()
    {
        Assert.Equal(("Scroll left", "Scroll right"), Pair(ThumbCardModel.TurnChips(null)));
        Assert.Equal(("Off", "Off"), Pair(ThumbCardModel.TurnChips(new ActionSpec.Disabled())));
        ThumbChips vol = ThumbCardModel.TurnChips(new ActionSpec.Keys("VolumeUp|VolumeDown"));
        Assert.Equal(("VolumeDown", "VolumeUp"), Pair(vol));
        Assert.True(vol.Mono);
        Assert.Equal(("Ctrl+Shift+Tab", "Ctrl+Tab"), Pair(ThumbCardModel.TurnChips(new ActionSpec.Keys("Ctrl+Tab"))));
    }

    [Fact]
    public void Hold_chips_put_back_on_the_left()
    {
        Assert.Equal(("Ctrl+Shift+Tab", "Ctrl+Tab"), Pair(ThumbCardModel.HoldChips(Tabs)));
        Assert.Equal(("Nothing", "Nothing"), Pair(ThumbCardModel.HoldChips(null)));
        var half = new ThumbWheelSecond(true, new ActionSpec.Keys("Ctrl+Plus"), new ActionSpec.None());
        Assert.Equal(("Nothing", "Ctrl+Plus"), Pair(ThumbCardModel.HoldChips(half)));
    }

    [Fact]
    public void Preview_kind_matches_the_action()
    {
        Assert.Equal(ThumbPreviewKind.Sheet, ThumbCardModel.TurnPreview(null));
        Assert.Equal(ThumbPreviewKind.Count, ThumbCardModel.TurnPreview(new ActionSpec.Keys("VolumeUp|VolumeDown")));
        Assert.Equal(ThumbPreviewKind.Tabs, ThumbCardModel.TurnPreview(new ActionSpec.Keys("Ctrl+Tab")));
        Assert.Equal(ThumbPreviewKind.Tabs, ThumbCardModel.HoldPreview(Tabs));
        Assert.Equal(ThumbPreviewKind.Tabs, ThumbCardModel.HoldPreview(new ThumbWheelSecond(true, new ActionSpec.Excel(ExcelCommand.NextSheet), new ActionSpec.Excel(ExcelCommand.PreviousSheet))));
        Assert.Equal(ThumbPreviewKind.Count, ThumbCardModel.HoldPreview(new ThumbWheelSecond(true, new ActionSpec.Keys("Ctrl+Plus"), new ActionSpec.Keys("Ctrl+Minus"))));
        Assert.Equal(ThumbPreviewKind.Count, ThumbCardModel.HoldPreview(null));
    }

    private static (string, string) Pair(ThumbChips c) => (c.Left, c.Right);
}
