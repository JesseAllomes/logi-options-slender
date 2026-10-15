using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class ProfileOpsPassThroughTests
{
    private const ushort Id = Slender.Hidpp.ControlId.ModeShift;

    private static SlenderConfig WithDefaultLaunch()
    {
        var def = new Profile(null, new Dictionary<ushort, ActionSpec> { [Id] = new ActionSpec.Launch("https://chatgpt.com/") });
        return SlenderConfig.Default with { Profiles = [def] };
    }

    [Fact]
    public void Setting_none_in_an_app_stores_a_pass_through()
    {
        SlenderConfig c = ProfileOps.SetBinding(WithDefaultLaunch(), "ms-teams", DeviceKind.Mouse, Id, new ActionSpec.None());

        Assert.True(ProfileOps.IsPassThrough(c, "ms-teams", DeviceKind.Mouse, Id));
        Profile app = c.Profiles.Single(p => p.AppProcess == "ms-teams");
        Assert.IsType<ActionSpec.None>(app.Bindings[Id]);
    }

    [Fact]
    public void Setting_null_in_an_app_removes_the_pass_through()
    {
        SlenderConfig withNone = ProfileOps.SetBinding(WithDefaultLaunch(), "ms-teams", DeviceKind.Mouse, Id, new ActionSpec.None());

        SlenderConfig c = ProfileOps.SetBinding(withNone, "ms-teams", DeviceKind.Mouse, Id, null);

        Assert.False(ProfileOps.IsPassThrough(c, "ms-teams", DeviceKind.Mouse, Id));
        Profile app = c.Profiles.Single(p => p.AppProcess == "ms-teams");
        Assert.False(app.Bindings.ContainsKey(Id));
    }
}
