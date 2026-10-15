using Slender.Engine;
using Slender.Tray.Ring;

namespace Slender.Tests.Tray;

public class RingIconFileTests
{
    [Theory]
    [InlineData(@"file:\\server\share\app.exe", null, null)]
    [InlineData(null, @"\\server\tools\x.exe", null)]
    [InlineData(null, @"C:\Tools\x.exe", @"C:\Tools\x.exe")]
    [InlineData(@"file:C:\img\a.png", null, @"C:\img\a.png")]
    [InlineData(null, @"relative\x.exe", null)]
    public void IconFile_never_returns_network_or_relative_paths(string? icon, string? launch, string? expected)
    {
        RingSlot slot = launch is null
            ? new RingSlot(new ActionSpec.None()) { Icon = icon }
            : new RingSlot(new ActionSpec.Launch(launch));
        Assert.Equal(expected, RingSlotText.IconFile(slot));
    }
}
