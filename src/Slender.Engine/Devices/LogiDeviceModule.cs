using Slender.Hidpp;

namespace Slender.Engine.Devices;

/// <summary>
/// Adapts a running HID++ device to the registry. The device's own connect loop does the work; this forwards its link state and
/// passes arrival hints on.
/// </summary>
public sealed class LogiDeviceModule : IDeviceModule, IDisposable
{
    private readonly LogiDeviceBase _device;
    private readonly Action<string, bool> _reportLink;

    public LogiDeviceModule(string deviceId, LogiDeviceBase device, Action<string, bool> reportLink)
    {
        DeviceId = deviceId;
        _device = device;
        _reportLink = reportLink;
        _device.ConnectionChanged += OnConnectionChanged;
        _reportLink(deviceId, device.IsConnected);
    }

    public string DeviceId { get; }

    /// <summary>No-op: the engine/router already starts the device, and this milestone must not change that behaviour.</summary>
    public void Start() { }

    /// <summary>
    /// No-op: with the device gone the HID++ loop sits idle in its backoff delay, and stopping it would need an arrival that a
    /// Bluetooth device whose interfaces stay listed never sends.
    /// </summary>
    public void Stop() { }

    public void Wake() => _device.Wake();

    public void Dispose() => _device.ConnectionChanged -= OnConnectionChanged;

    private void OnConnectionChanged(object? sender, bool connected) => _reportLink(DeviceId, connected);
}
