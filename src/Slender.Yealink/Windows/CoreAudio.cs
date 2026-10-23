using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Slender.Yealink.Windows;

// Core Audio COM, declared with source-generated interop so it works under NativeAOT. Only the methods up to the last one used
// are declared; vtable order is fixed by the Windows headers (mmdeviceapi.h, endpointvolume.h).

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);

    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    /// <summary>The interface comes back raw: wrap it with <see cref="CoreAudioCom.Wrap{T}"/> (the type depends on the iid asked for).</summary>
    [PreserveSig] int Activate(in Guid iid, uint clsCtx, nint activationParams, out nint iface);

    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);

    [PreserveSig] int GetId(out nint id);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid fmtid, uint pid)
{
    public Guid Fmtid = fmtid;
    public uint Pid = pid;
}

/// <summary>PROPVARIANT on 64-bit Windows: 24 bytes, the pointer member at offset 8.</summary>
[StructLayout(LayoutKind.Sequential, Size = 24)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1, Reserved2, Reserved3;
    public nint Pointer;
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);

    [PreserveSig] int GetAt(uint index, out PropertyKey key);

    [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
}

[GeneratedComInterface]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, in Guid sessionGuid);

    [PreserveSig] int GetBufferSize(out uint frames);

    [PreserveSig] int GetStreamLatency(out long latency);

    [PreserveSig] int GetCurrentPadding(out uint frames);

    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);

    [PreserveSig] int GetMixFormat(out nint format);

    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    [PreserveSig] int Start();

    [PreserveSig] int Stop();

    [PreserveSig] int Reset();

    [PreserveSig] int SetEventHandle(nint handle);

    [PreserveSig] int GetService(in Guid iid, out nint service);
}

[GeneratedComInterface]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
internal partial interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out nint data);

    [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
}

internal static class CoreAudioCom
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Wraps a raw COM pointer (taking over the caller's reference) as a generated interface.</summary>
    public static T Wrap<T>(nint raw) where T : class
    {
        try { return (T)Wrappers.GetOrCreateObjectForComInstance(raw, CreateObjectFlags.None); }
        finally { Marshal.Release(raw); }
    }

    public static void Release(object? com)
    {
        if (com == null) return;
        try { Marshal.FinalReleaseComObject(com); } catch { /* wrapper without a COM identity: the finalizer releases it */ }
    }
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
