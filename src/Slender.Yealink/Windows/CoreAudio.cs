using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Slender.Yealink.Windows;

// Core Audio COM, declared with source-generated interop so it works under NativeAOT. Only the methods up to the last one used
// are declared; vtable order is fixed by the Windows headers (mmdeviceapi.h, endpointvolume.h).

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);

    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig] int Activate(in Guid iid, uint clsCtx, nint activationParams, out IAudioEndpointVolume volume);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, in Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, in Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, in Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, in Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[GeneratedComInterface]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
internal partial interface IAudioEndpointVolumeCallback
{
    /// <summary>notifyData points at AUDIO_VOLUME_NOTIFICATION_DATA: a Guid context, then the BOOL mute at offset 16.</summary>
    [PreserveSig] int OnNotify(nint notifyData);
}

[GeneratedComClass]
internal sealed partial class MuteCallback(Action<bool> changed) : IAudioEndpointVolumeCallback
{
    public int OnNotify(nint notifyData)
    {
        try { changed(Marshal.ReadInt32(notifyData, 16) != 0); }
        catch { /* never throw into native code */ }
        return 0;
    }
}
