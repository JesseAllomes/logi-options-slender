using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class DeviceRequestTests
{
    [Theory]
    [InlineData(new[] { "--device", "mx-master-4" }, "mx-master-4")]
    [InlineData(new[] { "--popover", "--device", "ERGO-K860" }, "ergo-k860")]
    [InlineData(new[] { "--device=yealink-wh64" }, "yealink-wh64")]
    [InlineData(new[] { "--device", "laptop" }, null)]
    [InlineData(new[] { "--device" }, null)]
    [InlineData(new string[0], null)]
    public void Parse_reads_only_known_device_ids(string[] args, string? expected) =>
        Assert.Equal(expected, DeviceRequest.Parse(args));

    [Fact]
    public void Each_id_maps_to_its_page()
    {
        Assert.Equal(new DeviceTarget(DeviceKind.Mouse, false), DeviceRequest.Target("mx-master-4"));
        Assert.Equal(new DeviceTarget(DeviceKind.Keyboard, false), DeviceRequest.Target("ergo-k860"));
        Assert.Equal(new DeviceTarget(null, true), DeviceRequest.Target("yealink-wh64"));
        Assert.Null(DeviceRequest.Target("something-else"));
        Assert.Null(DeviceRequest.Target(null));
    }

    [Fact]
    public void Hand_off_is_read_once_then_gone()
    {
        string path = Path.Combine(Path.GetTempPath(), "slender-tests-" + Guid.NewGuid().ToString("N"), "device-request.txt");
        try
        {
            Assert.True(DeviceRequest.Hand(path, "ergo-k860"));
            Assert.Equal("ergo-k860", DeviceRequest.Take(path));
            Assert.Null(DeviceRequest.Take(path));
            Assert.True(DeviceRequest.Hand(path, "not-a-device"));
            Assert.Null(DeviceRequest.Take(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
