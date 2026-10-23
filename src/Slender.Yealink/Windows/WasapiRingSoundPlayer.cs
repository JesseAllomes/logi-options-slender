using System.Runtime.InteropServices;
using Slender.Engine.Ringer;

namespace Slender.Yealink.Windows;

/// <summary>
/// Plays a ring sound through WASAPI in shared mode. Nothing exists while idle: Play starts one thread that opens the file and the
/// device, loops the samples (gain applied up front, Windows converts rate and channels) and, on Stop or when a one-shot has
/// drained, ends them all and releases every COM object. The device is the id given, else the WH64 headset when present, else the
/// default playback device. Nothing here throws to the caller; failures are logged and the sound simply does not play.
/// </summary>
public sealed partial class WasapiRingSoundPlayer(Action<string>? log = null) : IRingSoundPlayer
{
    private const int ERender = 0;
    private const int EConsole = 0;
    private const uint DeviceStateActive = 1;
    private const uint ClsctxAll = 0x17;
    private const uint CoInitMultithreaded = 0;
    private const uint AutoConvertPcm = 0x80000000, SrcDefaultQuality = 0x08000000;
    private const int PollMs = 100;
    private const long BufferHns = 5_000_000; // 500 ms; the poll tops it up

    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MMDeviceEnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid AudioRenderClientIid = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    private static readonly PropertyKey FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    internal const string HeadsetName = "Yealink WH64";

    private sealed class Playback(string path, string? device, int volume, bool loop)
    {
        public readonly string Path = path;
        public readonly string? Device = device;
        public readonly int Volume = volume;
        public readonly bool Loop = loop;
        public readonly ManualResetEventSlim Stop = new(false);
        public Thread? Thread;
    }

    private readonly object _gate = new();
    private Playback? _current;

    public void Play(string soundPath, string? deviceId, int volume, bool loop)
    {
        try
        {
            Stop();
            var playback = new Playback(soundPath, deviceId, Math.Clamp(volume, 0, 100), loop);
            var thread = new Thread(() => Run(playback), 256 * 1024) { IsBackground = true, Name = "ring sound" };
            playback.Thread = thread;
            lock (_gate) _current = playback;
            thread.Start();
        }
        catch (Exception ex) { Log("ring sound could not start: " + ex.Message); }
    }

    public void Stop()
    {
        Playback? playback;
        lock (_gate)
        {
            playback = _current;
            _current = null;
        }

        if (playback is null) return;
        try
        {
            playback.Stop.Set();
            if (playback.Thread is { } t && t != Thread.CurrentThread && !t.Join(3000)) Log("ring sound thread did not end in time");
        }
        catch (Exception ex) { Log("ring sound stop failed: " + ex.Message); }
    }

    private void Log(string message)
    {
        try { log?.Invoke(message); } catch { }
    }

    private void Run(Playback p)
    {
        bool comReady = CoInitializeEx(0, CoInitMultithreaded) >= 0;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioRenderClient? render = null;
        bool started = false;
        try
        {
            byte[] file = File.ReadAllBytes(p.Path);
            if (!WavPcm.TryParse(file, out var wav)) { Log("ring sound is not a 16-bit PCM WAV: " + System.IO.Path.GetFileName(p.Path)); return; }
            byte[] samples = ScaledSamples(file, wav, p.Volume);
            file = [];

            int hr = CoCreateInstance(MMDeviceEnumeratorClsid, 0, ClsctxAll, MMDeviceEnumeratorIid, out nint raw);
            if (hr < 0) { Log($"ring sound: audio enumerator unavailable (0x{hr:X8})"); return; }
            enumerator = CoreAudioCom.Wrap<IMMDeviceEnumerator>(raw);
            device = OpenDevice(enumerator, p.Device);
            if (device is null) return;

            var iid = AudioClientIid;
            hr = device.Activate(in iid, ClsctxAll, 0, out raw);
            if (hr < 0) { Log($"ring sound: device refused (0x{hr:X8})"); return; }
            client = CoreAudioCom.Wrap<IAudioClient>(raw);

            // The file's own format goes in; the audio engine resamples and remixes it to the device's mix format.
            unsafe
            {
                byte* fmt = stackalloc byte[18];
                new Span<byte>(fmt, 18).Clear();
                *(ushort*)fmt = 1;
                *(ushort*)(fmt + 2) = (ushort)wav.Channels;
                *(int*)(fmt + 4) = wav.SampleRate;
                *(int*)(fmt + 8) = wav.SampleRate * wav.BlockAlign;
                *(ushort*)(fmt + 12) = (ushort)wav.BlockAlign;
                *(ushort*)(fmt + 14) = 16;
                hr = client.Initialize(0, AutoConvertPcm | SrcDefaultQuality, BufferHns, 0, (nint)fmt, Guid.Empty);
            }

            if (hr < 0) { Log($"ring sound: format not accepted (0x{hr:X8})"); return; }
            if (client.GetBufferSize(out uint bufferFrames) < 0) return;
            var renderIid = AudioRenderClientIid;
            if (client.GetService(in renderIid, out raw) < 0) return;
            render = CoreAudioCom.Wrap<IAudioRenderClient>(raw);

            int block = wav.BlockAlign;
            int position = 0, length = samples.Length;
            bool drained = false;

            bool Fill()
            {
                if (client.GetCurrentPadding(out uint padding) < 0) return false;
                if (drained) return padding > 0;
                uint free = bufferFrames - padding;
                if (free == 0) return true;
                if (render.GetBuffer(free, out nint buffer) < 0) return false;
                int wanted = (int)free * block, written = 0;
                while (written < wanted)
                {
                    int chunk = Math.Min(wanted - written, length - position);
                    Marshal.Copy(samples, position, buffer + written, chunk);
                    written += chunk;
                    position += chunk;
                    if (position < length) continue;
                    position = 0;
                    if (!p.Loop) { drained = true; break; }
                }

                return render.ReleaseBuffer((uint)(written / block), 0) >= 0;
            }

            if (!Fill()) return;
            if (client.Start() < 0) { Log("ring sound: could not start the stream"); return; }
            started = true;
            while (!p.Stop.Wait(PollMs))
            {
                if (!Fill()) break;
            }
        }
        catch (Exception ex) { Log("ring sound failed: " + ex.Message); }
        finally
        {
            try
            {
                if (started)
                {
                    client!.Stop();
                    client.Reset();
                }
            }
            catch { }

            CoreAudioCom.Release(render);
            CoreAudioCom.Release(client);
            CoreAudioCom.Release(device);
            CoreAudioCom.Release(enumerator);
            if (comReady) CoUninitialize();
            lock (_gate) { if (ReferenceEquals(_current, p)) _current = null; }
        }
    }

    /// <summary>A copy of the sample bytes with the volume applied as a straight gain.</summary>
    internal static byte[] ScaledSamples(ReadOnlySpan<byte> file, WavPcm wav, int volume)
    {
        var data = file.Slice(wav.DataOffset, wav.DataLength);
        var scaled = new byte[data.Length];
        int gain = Math.Clamp(volume, 0, 100) * 256 / 100;
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            int s = BitConverter.ToInt16(data.Slice(i, 2)) * gain >> 8;
            BitConverter.TryWriteBytes(scaled.AsSpan(i, 2), (short)Math.Clamp(s, short.MinValue, short.MaxValue));
        }

        return scaled;
    }

    private IMMDevice? OpenDevice(IMMDeviceEnumerator enumerator, string? id)
    {
        int hr;
        IMMDevice? device;
        if (!string.IsNullOrWhiteSpace(id))
        {
            hr = enumerator.GetDevice(id, out device);
            if (hr < 0) Log($"ring sound: playback device not found (0x{hr:X8})");
            return hr < 0 ? null : device;
        }

        device = FindHeadset(enumerator);
        if (device is not null) return device;
        hr = enumerator.GetDefaultAudioEndpoint(ERender, EConsole, out device);
        if (hr < 0) Log($"ring sound: no default playback device (0x{hr:X8})");
        return hr < 0 ? null : device;
    }

    private static IMMDevice? FindHeadset(IMMDeviceEnumerator enumerator)
    {
        IMMDeviceCollection? devices = null;
        try
        {
            if (enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out devices) < 0 || devices.GetCount(out uint count) < 0) return null;
            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var candidate) < 0) continue;
                if (NameOf(candidate)?.Contains(HeadsetName, StringComparison.OrdinalIgnoreCase) == true) return candidate;
                CoreAudioCom.Release(candidate);
            }

            return null;
        }
        finally { CoreAudioCom.Release(devices); }
    }

    private static string? NameOf(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(0, out store) < 0) return null; // STGM_READ
            var key = FriendlyName;
            if (store.GetValue(in key, out var value) < 0) return null;
            try { return value.Vt == 31 && value.Pointer != 0 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { CoreAudioCom.Release(store); }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);
}
