namespace Slender.Hidpp.Inventory;

/// <summary>
/// Best-effort feature and control names. Source: Solaar lib/logitech_receiver/hidpp20_constants.py (FEATURE enum) and
/// special_keys.py (CONTROL), github.com/pwr-Solaar/Solaar; plus docs/protocol.md for what this repo has confirmed on device.
/// A feature missing here is reported as "unknown"; a wrong name here never changes which functions are called.
/// </summary>
public static class InventoryNames
{
    public const string Unknown = "unknown";

    private static readonly Dictionary<ushort, string> Features = new()
    {
        [0x0000] = "IRoot",
        [0x0001] = "IFeatureSet",
        [0x0002] = "IFeatureInfo",
        [0x0003] = "DeviceFwVersion",
        [0x0004] = "DeviceUnitId",
        [0x0005] = "DeviceName",
        [0x0006] = "DeviceGroups",
        [0x0007] = "DeviceFriendlyName",
        [0x0008] = "KeepAlive",
        [0x0020] = "ConfigChange",
        [0x0021] = "CryptoId",
        [0x0030] = "TargetSoftware",
        [0x00C2] = "DfuControlSigned",
        [0x00C3] = "DfuControl",
        [0x00D0] = "Dfu",
        [0x00D1] = "Dfu2",
        [0x1000] = "BatteryStatus",
        [0x1001] = "BatteryVoltage",
        [0x1004] = "UnifiedBattery",
        [0x1010] = "ChargingControl",
        [0x1300] = "LedControl",
        [0x1802] = "DeviceReset",
        [0x1803] = "GpioAccess",
        [0x1805] = "OobState",
        [0x1806] = "ConfigDeviceProps",
        [0x1814] = "ChangeHost",
        [0x1815] = "HostsInfo",
        [0x1830] = "PowerModes",
        [0x18A1] = "LedSwControl",
        [0x1981] = "BacklightLevel",
        [0x1982] = "Backlight2",
        [0x1983] = "Backlight3",
        [0x1990] = "Illumination",
        [0x19B0] = "Haptic",
        [0x19C0] = "ForceSensingButton",
        [0x1A00] = "PresenterControl",
        [0x1A01] = "Sensor3D",
        [0x1B00] = "ReprogControls",
        [0x1B01] = "ReprogControlsV2",
        [0x1B02] = "ReprogControlsV2_2",
        [0x1B03] = "ReprogControlsV3",
        [0x1B04] = "ReprogControlsV4",
        [0x1C00] = "PersistentRemappableAction",
        [0x1D4B] = "WirelessDeviceStatus",
        [0x1DF0] = "RemainingPairing",
        [0x1E00] = "EnableHiddenFeatures",
        [0x1F1F] = "FirmwareProperties",
        [0x1F20] = "AdcMeasurement",
        [0x2001] = "LeftRightSwap",
        [0x2005] = "SwapButton",
        [0x2006] = "PointerAxesOrientation",
        [0x2100] = "VerticalScrolling",
        [0x2110] = "SmartShift",
        [0x2111] = "SmartShiftEnhanced",
        [0x2121] = "HiResWheel",
        [0x2130] = "LowResWheel",
        [0x2150] = "ThumbWheel",
        [0x2200] = "MousePointer",
        [0x2201] = "AdjustableDpi",
        [0x2205] = "PointerSpeed",
        [0x2230] = "AngleSnapping",
        [0x2240] = "SurfaceTuning",
        [0x2400] = "HybridTracking",
        [0x40A0] = "FnInversion",
        [0x40A2] = "NewFnInversion",
        [0x40A3] = "FnInversionMultiHost",
        [0x4100] = "Encryption",
        [0x4220] = "LockKeyState",
        [0x4301] = "SolarDashboard",
        [0x4520] = "KeyboardLayout",
        [0x4521] = "DisableKeys",
        [0x4522] = "DisableKeysByUsage",
        [0x4530] = "DualPlatform",
        [0x4531] = "MultiPlatform",
        [0x4540] = "KeyboardInternationalLayout",
        [0x4600] = "Crown",
        [0x6010] = "TouchpadFwItems",
        [0x6011] = "TouchpadSwItems",
        [0x6012] = "TouchpadWin8FwItems",
        [0x6100] = "TouchpadRawXy",
        [0x6110] = "TouchmouseRawPoints",
        [0x6120] = "TouchmouseSettings",
        [0x8010] = "GKey",
        [0x8020] = "MKeys",
        [0x8030] = "MR",
        [0x8040] = "BrightnessControl",
        [0x8060] = "ReportRate",
        [0x8061] = "ExtendedAdjustableReportRate",
        [0x8070] = "ColorLedEffects",
        [0x8071] = "RgbEffects",
        [0x8080] = "PerKeyLighting",
        [0x8081] = "PerKeyLightingV2",
        [0x8100] = "OnboardProfiles",
        [0x8110] = "MouseButtonSpy",
        [0x8111] = "LatencyMonitoring",
        [0x8113] = "GamingAttachments",
        [0x8123] = "ForceFeedback",
        [0x8300] = "Sidetone",
        [0x8310] = "Equalizer",
        [0x8320] = "HeadsetOut",
    };

    /// <summary>Feature name, or <see cref="Unknown"/>. 0x1E01-0x1E22 are the engineering/test block (names not public).</summary>
    public static string FeatureName(ushort id)
    {
        if (Features.TryGetValue(id, out var n)) return n;
        if (id is >= 0x1E01 and <= 0x1E22) return "EngineeringTest (0x1E block)";
        return Unknown;
    }

    public static bool IsKnownFeature(ushort id) => FeatureName(id) != Unknown;

    // CONTROL ids: Solaar special_keys.py CONTROL, plus ControlId in HidppContracts.cs (confirmed on the MX Master 4).
    private static readonly Dictionary<ushort, string> Controls = new()
    {
        [0x0050] = "Left Click",
        [0x0051] = "Right Click",
        [0x0052] = "Middle Button",
        [0x0053] = "Back Button",
        [0x0056] = "Forward Button",
        [0x00C3] = "Mouse Gesture Button",
        [0x00C4] = "Smart Shift",
        [0x00C7] = "Brightness Down",
        [0x00C8] = "Brightness Up",
        [0x00D1] = "Host Switch Channel 1",
        [0x00D2] = "Host Switch Channel 2",
        [0x00D3] = "Host Switch Channel 3",
        [0x00D7] = "Virtual Gesture Button",
        [0x00FD] = "DPI Switch",
        [0x01A0] = "Sense Panel",
    };

    public static string? ControlName(ushort cid) => Controls.TryGetValue(cid, out var n) ? n : null;

    // Device type byte of 0x0005 fn2: Solaar hidpp20.py DEVICE_KIND / Logitech x0005 spec.
    private static readonly string[] DeviceTypes =
    [
        "keyboard", "remote control", "numpad", "mouse", "touchpad", "trackball", "presenter", "receiver", "headset", "webcam",
        "steering wheel", "joystick", "gamepad", "dock", "speaker", "microphone", "illumination light", "programmable controller",
        "car sim pedals", "adapter",
    ];

    public static string DeviceType(byte t) => t < DeviceTypes.Length ? DeviceTypes[t] : Unknown;
}
