using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class GestureSwipeOpsTests
{
    private static readonly ushort Up = DeviceIds.SwipeId(GestureDirection.Up);

    [Fact]
    public void Set_and_read_swipe_in_all_apps_and_app()
    {
        var cfg = ProfileOps.AddApp(SlenderConfig.Default, "teams", "Teams", null);
        cfg = ProfileOps.SetBinding(cfg, null, DeviceKind.Mouse, Up, new ActionSpec.Keys("Win+Tab"));
        Assert.Equal(new ActionSpec.Keys("Win+Tab"), ProfileOps.Own(cfg, null, DeviceKind.Mouse, Up));

        var (inheritedAction, inherited) = ProfileOps.Effective(cfg, "teams", DeviceKind.Mouse, Up);
        Assert.True(inherited);
        Assert.Equal(new ActionSpec.Keys("Win+Tab"), inheritedAction);

        cfg = ProfileOps.SetBinding(cfg, "teams", DeviceKind.Mouse, Up, new ActionSpec.Keys("Win"));
        Assert.Equal((new ActionSpec.Keys("Win"), false), ProfileOps.Effective(cfg, "teams", DeviceKind.Mouse, Up));
        Assert.Empty(ProfileOps.Find(cfg, "teams")!.Bindings);

        cfg = ProfileOps.SetBinding(cfg, "teams", DeviceKind.Mouse, Up, null);
        Assert.True(ProfileOps.Effective(cfg, "teams", DeviceKind.Mouse, Up).Inherited);
    }

    [Fact]
    public void Explicit_none_is_pass_through()
    {
        var cfg = ProfileOps.AddApp(SlenderConfig.Default, "teams", "Teams", null);
        cfg = ProfileOps.SetBinding(cfg, null, DeviceKind.Mouse, Up, new ActionSpec.Keys("Win+Tab"));
        cfg = ProfileOps.SetBinding(cfg, "teams", DeviceKind.Mouse, Up, new ActionSpec.None());
        Assert.True(ProfileOps.IsPassThrough(cfg, "teams", DeviceKind.Mouse, Up));
        Assert.Null(ProfileOps.Effective(cfg, "teams", DeviceKind.Mouse, Up).Action);
    }

    [Fact]
    public void Catalog_lists_four_swipe_rows()
    {
        Assert.Equal(4, ControlCatalog.SwipeControls.Count);
        Assert.All(ControlCatalog.SwipeControls, c => Assert.True(DeviceIds.TryGetSwipe(c.Id, out _)));
    }
}
