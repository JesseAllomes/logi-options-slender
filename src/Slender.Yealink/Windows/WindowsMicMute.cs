using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Slender.Yealink.Windows;

/// <summary>
/// Mute of the default communications capture endpoint through IAudioEndpointVolume. The endpoint is resolved once at
/// construction; a later change of default device is not followed until the module restarts.
/// </summary>
public sealed partial class WindowsMicMute : IMicMute
{
    private const int ECapture = 1;
    private const int ECommunications = 2;
    private const uint ClsctxAll = 0x17;
    private const uint CoInitMultithreaded = 0;

    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MMDeviceEnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid AudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private readonly object _gate = new();
    private IMMDevice? _device;
    private IAudioEndpointVolume? _volume;
    private MuteCallback? _callback;
    private Guid _context = Guid.NewGuid();

    public WindowsMicMute()
    {
        // Threads that never initialised COM get an implicit MTA only if another thread already did; make sure of it.
        CoInitializeEx(0, CoInitMultithreaded);

        var clsid = MMDeviceEnumeratorClsid;
        var iid = MMDeviceEnumeratorIid;
        int hr = CoCreateInstance(ref clsid, 0, ClsctxAll, ref iid, out nint raw);
        if (hr < 0) throw new Win32Exception(hr);
        IMMDeviceEnumerator enumerator;
        try { enumerator = (IMMDeviceEnumerator)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(raw, CreateObjectFlags.None); }
        finally { Marshal.Release(raw); }

        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(ECapture, ECommunications, out var device));
            _device = device;
            var vol = AudioEndpointVolumeIid;
            Check(device.Activate(in vol, ClsctxAll, 0, out nint volume));
            _volume = CoreAudioCom.Wrap<IAudioEndpointVolume>(volume);
        }
        catch
        {
            Dispose();
            throw;
        }
        finally { Release(enumerator); }
    }

    public bool GetMute()
    {
        lock (_gate)
        {
            Check(Volume().GetMute(out bool muted));
            return muted;
        }
    }

    public void SetMute(bool muted)
    {
        lock (_gate) Check(Volume().SetMute(muted, in _context));
    }

    public void Subscribe(Action<bool> changed)
    {
        lock (_gate)
        {
            UnsubscribeCore();
            var cb = new MuteCallback(changed);
            Check(Volume().RegisterControlChangeNotify(cb));
            _callback = cb;
        }
    }

    public void Unsubscribe()
    {
        lock (_gate) UnsubscribeCore();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { UnsubscribeCore(); } catch { /* the endpoint may already be gone */ }
            Release(_volume);
            Release(_device);
            _volume = null;
            _device = null;
        }
    }

    private void UnsubscribeCore()
    {
        var cb = _callback;
        _callback = null;
        if (cb != null && _volume != null) _volume.UnregisterControlChangeNotify(cb);
    }

    private IAudioEndpointVolume Volume() => _volume ?? throw new ObjectDisposedException(nameof(WindowsMicMute));

    private static void Check(int hr)
    {
        if (hr < 0) throw new Win32Exception(hr);
    }

    private static void Release(object? com)
    {
        if (com == null) return;
        try { Marshal.FinalReleaseComObject(com); } catch { /* wrapper without a COM identity: the finalizer releases it */ }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(ref Guid clsid, nint outer, uint clsContext, ref Guid iid, out nint instance);
}
