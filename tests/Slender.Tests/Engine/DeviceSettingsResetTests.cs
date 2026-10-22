using Slender.Engine;
using Slender.Engine.Devices;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public sealed class DeviceSettingsResetTests
{
    private static readonly ActionSpec Act = new ActionSpec.Excel(ExcelCommand.NextSheet);

    private static SlenderConfig Busy()
    {
        var all = new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.Back] = Act })
        {
            KeyBindings = new Dictionary<ushort, ActionSpec> { [0x0100] = Act },
            GestureSwipes = new Dictionary<GestureDirection, ActionSpec> { [GestureDirection.Up] = Act },
            ThumbWheelAlt = new ThumbWheelSecond(true, Act, Act),
            SideScrollSpeed = 200, WheelSpeed = 150, Dpi = 2000,
            ThumbFlick = new ThumbWheelFlick(true, Act, Act),
            RingSlots = [new RingSlot(Act)],
            DisplayName = "All apps",
        };
        var mouseOnly = new Profile("mouseapp", new Dictionary<ushort, ActionSpec> { [ControlId.Back] = Act }) { Dpi = 800 };
        var keysOnly = new Profile("keyapp", new Dictionary<ushort, ActionSpec>()) { KeyBindings = new Dictionary<ushort, ActionSpec> { [0x0100] = Act } };
        var userEmpty = new Profile("blank", new Dictionary<ushort, ActionSpec>());
        return SlenderConfig.Default with
        {
            Profiles = [all, mouseOnly, keysOnly, userEmpty],
            Pointer = new PointerSettings(Dpi: 1600, InvertWheel: true),
            Haptics = new HapticSettings(Level: 0),
            Keyboard = new KeyboardSettings(FnLock: true, DisabledKeys: 3),
            OnboardRemaps = [new OnboardRemap(0x0100, "Ctrl+C")],
            BatteryAlerts = new BatteryAlertSettings(true, 40, 30),
            Excel = new ExcelSettings(false, 5),
            LaunchAtLogin = true,
        };
    }

    [Fact]
    public void Mouse_reset_clears_mouse_fields_only()
    {
        var before = Busy();
        var after = DeviceSettingsReset.Forget(before, "mx-master-4");

        Assert.Equal(new PointerSettings(), after.Pointer);
        Assert.Equal(new HapticSettings(), after.Haptics);
        Assert.Equal(15, after.BatteryAlerts.MouseThresholdPercent);
        Assert.Equal(30, after.BatteryAlerts.KeyboardThresholdPercent);
        Assert.Equal(before.Keyboard, after.Keyboard);
        Assert.Equal(before.OnboardRemaps, after.OnboardRemaps);
        Assert.Equal(before.Ring, after.Ring);
        Assert.Equal(before.Excel, after.Excel);
        Assert.Equal(before.Macros, after.Macros);
        Assert.True(after.LaunchAtLogin);

        var all = after.Profiles[0];
        Assert.Null(all.AppProcess);
        Assert.Empty(all.Bindings);
        Assert.Empty(all.GestureSwipes);
        Assert.Null(all.ThumbWheelAlt);
        Assert.Null(all.SideScrollSpeed);
        Assert.Null(all.WheelSpeed);
        Assert.Null(all.ThumbFlick);
        Assert.Null(all.Dpi);
        Assert.Single(all.KeyBindings);
        Assert.Single(all.RingSlots!);
        // mouseapp held only mouse settings and goes; the user's own empty profile was not emptied by the reset and stays.
        Assert.Equal(["All apps", "keyapp", "blank"], after.Profiles.Select(p => p.DisplayName ?? p.AppProcess));
    }

    [Fact]
    public void Keyboard_reset_clears_keyboard_fields_only()
    {
        var before = Busy();
        var after = DeviceSettingsReset.Forget(before, "ergo-k860");

        Assert.Equal(new KeyboardSettings(), after.Keyboard);
        Assert.Empty(after.OnboardRemaps);
        Assert.Equal(10, after.BatteryAlerts.KeyboardThresholdPercent);
        Assert.Equal(40, after.BatteryAlerts.MouseThresholdPercent);
        Assert.Equal(before.Pointer, after.Pointer);
        Assert.Equal(before.Haptics, after.Haptics);
        Assert.Equal(before.Ring, after.Ring);
        Assert.Equal(before.Excel, after.Excel);

        Assert.Empty(after.Profiles[0].KeyBindings);
        Assert.Single(after.Profiles[0].Bindings);
        Assert.Equal(200, after.Profiles[0].SideScrollSpeed);
        Assert.Equal(["All apps", "mouseapp", "blank"], after.Profiles.Select(p => p.DisplayName ?? p.AppProcess));
    }

    [Fact]
    public void Default_profile_is_never_removed()
    {
        var cfg = SlenderConfig.Default with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { KeyBindings = new Dictionary<ushort, ActionSpec> { [1] = Act } }] };

        Assert.Single(DeviceSettingsReset.Forget(cfg, "ergo-k860").Profiles);
    }

    [Fact]
    public void Unknown_id_changes_nothing()
    {
        var cfg = Busy();

        Assert.Same(cfg, DeviceSettingsReset.Forget(cfg, "nope"));
    }
}
