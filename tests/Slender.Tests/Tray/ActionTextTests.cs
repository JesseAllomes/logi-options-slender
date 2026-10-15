using Slender.Engine;
using Slender.Tray;

namespace Slender.Tests.Tray;

public class ActionTextTests
{
    [Fact]
    public void Launch_describes_the_target() =>
        Assert.Equal(@"Open: C:\Tools\x.exe", ActionText.Describe(new ActionSpec.Launch(@"C:\Tools\x.exe")));

    [Fact]
    public void Disabled_describes_as_off() =>
        Assert.Equal("Off", ActionText.Describe(new ActionSpec.Disabled()));

    [Fact]
    public void Brightness_describes_the_direction()
    {
        Assert.Equal("Brightness up", ActionText.Describe(new ActionSpec.Brightness(10)));
        Assert.Equal("Brightness down", ActionText.Describe(new ActionSpec.Brightness(-10)));
    }

    [Fact]
    public void Text_describes_the_typed_value() =>
        Assert.Equal("Type: Kind regards", ActionText.Describe(new ActionSpec.Text("Kind regards")));
}
