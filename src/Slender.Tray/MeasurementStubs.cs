using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tray;

/// <summary>--no-devices: opens nothing, so a measurement instance never competes with a running tray for the HID++ devices.</summary>
internal sealed class NoDevices : IHidppTransportFactory
{
    public IHidppTransport? TryOpen() => null;
}

/// <summary>--no-devices: the engine gets an inert hook, so a measurement instance never swallows or remaps real input.</summary>
internal sealed class InertHook : IInputHook
{
    public event EventHandler<HookWheelEventArgs>? HorizontalWheel { add { } remove { } }

    public event EventHandler<HookButtonEventArgs>? ButtonChanged { add { } remove { } }

    public void Start()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>A Run key held in memory. Used when SLENDER_CONFIG_DIR is set so a test instance never touches the real registry.</summary>
internal sealed class InMemoryRunKey : IRunKey
{
    private readonly Dictionary<string, string> _values = new();

    public string? GetValue(string name) => _values.GetValueOrDefault(name);

    public void SetValue(string name, string value) => _values[name] = value;

    public void DeleteValue(string name) => _values.Remove(name);
}
