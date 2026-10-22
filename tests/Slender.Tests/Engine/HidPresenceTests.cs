using Slender.Engine.Devices;

namespace Slender.Tests.Engine;

public class HidPresenceTests
{
    [Fact]
    public void Usb_path()
    {
        var k = WindowsHidPresence.ParseKey(@"\\?\hid#vid_6993&pid_b0ae&mi_03&col02#7&abc&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Assert.Equal(new HidDeviceKey(0x6993, 0xB0AE), k);
    }

    [Fact]
    public void Bluetooth_le_path()
    {
        var k = WindowsHidPresence.ParseKey(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b042_rev&0019_aabbccddeeff&col02#8&1&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Assert.Equal(new HidDeviceKey(0x046D, 0xB042), k);
    }

    [Fact]
    public void Receiver_path()
    {
        var k = WindowsHidPresence.ParseKey(@"\\?\hid#vid_046d&pid_c52b&mi_01&col02#7&1&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Assert.Equal(new HidDeviceKey(0x046D, 0xC52B), k);
    }

    [Fact]
    public void Uppercase_path()
    {
        var k = WindowsHidPresence.ParseKey(@"\\?\HID#VID_046D&PID_B359&MI_00#7&1&0&0000#{4D1E55B2-F16F-11CF-88CB-001111000030}");
        Assert.Equal(new HidDeviceKey(0x046D, 0xB359), k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData(@"\\?\hid#vid_zzzz&pid_b0ae#1")]
    [InlineData(@"\\?\hid#vid_046d#1")]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d#1")]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02")]
    public void Unrecognised_is_null(string path) => Assert.Null(WindowsHidPresence.ParseKey(path));
}
