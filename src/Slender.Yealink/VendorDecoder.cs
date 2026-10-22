using System.Text;

namespace Slender.Yealink;

/// <summary>What the base has said about the headset. Null = not said yet. Signal 0 to 7 as the base reports it.</summary>
public readonly record struct HeadsetStatus(bool? Registered, int? BatteryPercent, bool? Charging, bool? Docked, int? Signal, bool? InRange, string? Model = null);

/// <summary>
/// Reads the base's messages: command replies (Hello, version, EnvGet and EnvSet short and long, GetDect) and the unsolicited DECT channel
/// 0x0D of the notification stream (battery, signal). Every other notification channel, the debug log above all, is ignored on its
/// first byte. Replies raise <see cref="Reply"/>; a changed headset status raises <see cref="StatusChanged"/>. Nothing is
/// allocated for a notification; a reply allocates its key and value strings.
/// </summary>
public sealed class VendorDecoder
{
    public const byte DectChannel = 0x0D;
    public const byte EventSignal = 0x04;
    public const byte EventBattery = 0x09;

    /// <summary>The battery state the base sends while the headset is docked and charging.</summary>
    public const int BatteryCharging = 2;

    /// <summary>The handset the WH64 pairs as; the base has four slots.</summary>
    private const byte Handset = 1;
    private const int HandsetBlock = 131;
    private const int HandsetUsed = 7;
    private const int ModelOffset = 44, ModelLength = 32;

    private HeadsetStatus _status;

    public HeadsetStatus Status => _status;

    /// <summary>Command byte, settings key (null for an unset EnvGet or any other command) and value (null when unset).</summary>
    public event Action<byte, string?, string?>? Reply;

    public event Action<HeadsetStatus>? StatusChanged;

    public void Message(byte stream, ReadOnlySpan<byte> m)
    {
        if (m.IsEmpty) return;
        if (stream == VendorFrame.StreamNotify) Notification(m);
        else if (stream == VendorFrame.StreamCommand) Command(m);
    }

    private void Notification(ReadOnlySpan<byte> m)
    {
        if (m[0] != DectChannel || m.Length < 8) return;
        int len = m[2] | m[3] << 8;
        if (len < 4 || m[5] != Handset) return;
        byte a = m[6], b = m[7];
        switch (m[4])
        {
            case EventBattery when a <= 100:
                Set(_status with { Registered = true, BatteryPercent = a, Charging = b == BatteryCharging, Docked = b == BatteryCharging });
                break;
            case EventSignal:
                Set(_status with { Registered = true, Signal = a, InRange = a > 0 });
                break;
        }
    }

    private void Command(ReadOnlySpan<byte> m)
    {
        byte cmd = m[0];
        switch ((VendorCommand)cmd)
        {
            case VendorCommand.EnvGet or VendorCommand.EnvSet when m.Length >= 6:
                if (m[1] == 0) Reply?.Invoke(cmd, null, null);
                else
                {
                    var name = m.Slice(6, Math.Min(VendorFrame.ShortKey, m.Length - 6));
                    var value = m.Length > 22 ? m[22..] : default;
                    Reply?.Invoke(cmd, Ascii(name), Ascii(value));
                }
                break;
            case VendorCommand.EnvGetLong or VendorCommand.EnvSetLong when m.Length >= 6:
                int nl = m[2] | m[3] << 8, vl = m[4] | m[5] << 8;
                if (6 + nl + vl > m.Length) return;
                Reply?.Invoke(cmd, Ascii(m.Slice(6, nl)), Ascii(m.Slice(6 + nl, vl)));
                break;
            case VendorCommand.GetDect:
                Dect(m);
                Reply?.Invoke(cmd, null, null);
                break;
            case VendorCommand.Hello or VendorCommand.GetVersion:
                Reply?.Invoke(cmd, null, null);
                break;
        }
    }

    private void Dect(ReadOnlySpan<byte> m)
    {
        if (m.Length < HandsetBlock + HandsetUsed) return;
        var hs = m[HandsetBlock..];
        bool registered = hs[0] != 0;
        if (!registered)
        {
            Set(new HeadsetStatus(false, null, null, null, null, null));
            return;
        }
        string? model = hs.Length >= ModelOffset + ModelLength ? Ascii(hs.Slice(ModelOffset, ModelLength)) : _status.Model;
        Set(new HeadsetStatus(true, hs[3] <= 100 ? hs[3] : null, hs[4] == BatteryCharging, hs[5] != 0, hs[2], hs[6] != 0, model));
    }

    private void Set(HeadsetStatus next)
    {
        if (next == _status) return;
        _status = next;
        StatusChanged?.Invoke(next);
    }

    /// <summary>Text up to the first NUL; empty text is null (an unset value).</summary>
    private static string? Ascii(ReadOnlySpan<byte> b)
    {
        int end = b.IndexOf((byte)0);
        if (end >= 0) b = b[..end];
        return b.IsEmpty ? null : Encoding.Latin1.GetString(b);
    }
}
