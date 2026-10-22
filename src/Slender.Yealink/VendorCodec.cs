using System.Text;
using Slender.Engine.Devices;

namespace Slender.Yealink;

/// <summary>
/// The only commands this code can put on the vendor channel (report 0xC8). All but the two EnvSets are reads or a handshake;
/// nothing that resets, flashes, runs a shell, moves files, powers off or updates firmware has an id here, and
/// <see cref="VendorRequests.Build"/> refuses every other byte, the EnvSets included: those are built only by
/// <see cref="VendorRequests.BuildEnvSet"/>, for a catalogued, editable setting and a value the catalog lists.
/// </summary>
public enum VendorCommand : byte
{
    /// <summary>Handshake: the request carries the same text the base answers with.</summary>
    Hello = 0x00,
    GetVersion = 0x01,
    /// <summary>Read one setting whose key is at most 16 characters.</summary>
    EnvGet = 0x03,
    /// <summary>Write one setting whose key is at most 16 characters (only through <see cref="VendorRequests.BuildEnvSet"/>).</summary>
    EnvSet = 0x04,
    /// <summary>Read one setting whose key is longer than 16 characters.</summary>
    EnvGetLong = 0x22,
    /// <summary>Write one setting whose key is longer than 16 characters (only through <see cref="VendorRequests.BuildEnvSet"/>).</summary>
    EnvSetLong = 0x23,
    /// <summary>Handset status: registered, signal, battery, docked, alive.</summary>
    GetDect = 0x79,
}

/// <summary>Frame constants shared by the codec: every vendor report is 64 bytes <c>C8 type len seq chunk[len]</c>.</summary>
public static class VendorFrame
{
    public const byte ReportId = 0xC8;
    public const int Length = 64;
    public const int HeaderLength = 4;
    public const int MaxChunk = 0x3C;

    /// <summary>Stream bits of the type byte: command replies, and notifications the base starts on its own.</summary>
    public const byte StreamCommand = 0x00;
    public const byte StreamNotify = 0x38;

    /// <summary>Fragment position, the type byte's two low bits.</summary>
    public const byte Middle = 0, First = 1, Last = 2, Single = 3;

    /// <summary>The longest settings key the short EnvGet carries (a 16-byte field).</summary>
    public const int ShortKey = 16;
}

/// <summary>
/// Builds the requests, and nothing else. The allowlist is the <see cref="VendorCommand"/> enum: any other command byte throws.
/// Layouts follow the SDK's own Hello, GetVersion and EnvGet frames; the long EnvGet and GetDect requests are inferred from the
/// replies, and both EnvSets were confirmed on a live base (see docs/devices/yealink-wh64.md).
/// </summary>
public static class VendorRequests
{
    public const string HelloText = "hello RC8-MCU device!";

    /// <summary>The commands <see cref="Build(byte, ReadOnlySpan{char}, Span{byte})"/> builds: reads and the handshake. The EnvSets are not among them.</summary>
    public static bool IsAllowed(byte commandId) =>
        commandId is (byte)VendorCommand.Hello or (byte)VendorCommand.GetVersion or (byte)VendorCommand.EnvGet
            or (byte)VendorCommand.EnvGetLong or (byte)VendorCommand.GetDect;

    /// <summary>
    /// Writes one single-fragment request into frame (cleared first; at least <see cref="VendorFrame.Length"/> bytes, any extra stays
    /// zero) and returns the report length. argument is the key for the two EnvGets and ignored otherwise. Throws
    /// ArgumentOutOfRangeException for a command that is not allowlisted, ArgumentException for a bad key.
    /// </summary>
    public static int Build(byte commandId, ReadOnlySpan<char> argument, Span<byte> frame)
    {
        if (!IsAllowed(commandId)) throw new ArgumentOutOfRangeException(nameof(commandId), commandId, "Not an allowlisted vendor command.");
        if (frame.Length < VendorFrame.Length) throw new ArgumentException("Frame buffer is shorter than a vendor report.", nameof(frame));
        frame.Clear();
        frame[0] = VendorFrame.ReportId;
        frame[1] = VendorFrame.Single;
        frame[4] = commandId;

        int body;
        switch ((VendorCommand)commandId)
        {
            case VendorCommand.Hello:
                body = 6 + 0x20;
                frame[5] = 0x20;
                Encoding.ASCII.GetBytes(HelloText, frame[10..]);
                break;
            case VendorCommand.EnvGet:
                CheckKey(argument, VendorFrame.ShortKey);
                body = 6 + 0x36;
                frame[5] = 0x36;
                Encoding.ASCII.GetBytes(argument, frame[10..]);
                break;
            case VendorCommand.EnvGetLong:
                CheckKey(argument, VendorFrame.MaxChunk - 8);
                if (argument.Length <= VendorFrame.ShortKey) throw new ArgumentException("A short key uses EnvGet.", nameof(argument));
                // 22 <namelen+1> <namelen u16> <valuelen u16 = 0> <name> 00
                body = 6 + argument.Length + 1;
                frame[5] = (byte)(argument.Length + 1);
                frame[6] = (byte)argument.Length;
                Encoding.ASCII.GetBytes(argument, frame[10..]);
                break;
            default: // GetVersion, GetDect: command and an empty payload
                body = 6;
                break;
        }

        frame[2] = (byte)body;
        return VendorFrame.Length;
    }

    /// <summary>The longest value the short EnvSet carries.</summary>
    public const int MaxShortValue = 36;

    /// <summary>
    /// Writes the request that sets a catalogued setting to raw: EnvSet for a key up to 16 characters, EnvSetLong beyond. The only
    /// way to build a set. Throws ArgumentException unless def is in the catalog, can be set and raw is one of its listed choices or
    /// a whole number within its range; the message must fit one chunk.
    /// </summary>
    public static int BuildEnvSet(YealinkSettingDef def, string raw, Span<byte> frame)
    {
        if (frame.Length < VendorFrame.Length) throw new ArgumentException("Frame buffer is shorter than a vendor report.", nameof(frame));
        int index = YealinkSettingsCatalog.IndexOf(def.Key);
        if (index < 0 || !ReferenceEquals(YealinkSettingsCatalog.All[index], def))
            throw new ArgumentException("Not a catalogued setting.", nameof(def));
        if (!def.CanSet) throw new ArgumentException("This setting is read-only.", nameof(def));
        if (!IsValidRaw(def, raw)) throw new ArgumentException("Not a value this setting can take.", nameof(raw));
        CheckKey(def.Key, VendorFrame.MaxChunk - 8);

        frame.Clear();
        frame[0] = VendorFrame.ReportId;
        frame[1] = VendorFrame.Single;
        int body;
        if (def.Key.Length <= VendorFrame.ShortKey)
        {
            if (raw.Length > MaxShortValue) throw new ArgumentException("Value too long.", nameof(raw));
            body = 6 + 0x36;
            frame[4] = (byte)VendorCommand.EnvSet;
            frame[5] = 0x36;
            Encoding.ASCII.GetBytes(def.Key, frame[10..]);
            Encoding.ASCII.GetBytes(raw, frame[(10 + VendorFrame.ShortKey)..]);
        }
        else
        {
            int plen = def.Key.Length + raw.Length + 1;
            body = 6 + plen;
            if (body > VendorFrame.MaxChunk) throw new ArgumentException("The setting does not fit one report.", nameof(raw));
            frame[4] = (byte)VendorCommand.EnvSetLong;
            frame[5] = (byte)plen;
            frame[6] = (byte)def.Key.Length;
            frame[8] = (byte)raw.Length;
            Encoding.ASCII.GetBytes(def.Key, frame[10..]);
            Encoding.ASCII.GetBytes(raw, frame[(10 + def.Key.Length)..]);
        }
        frame[2] = (byte)body;
        return VendorFrame.Length;
    }

    private static bool IsValidRaw(YealinkSettingDef def, string raw)
    {
        if (def.IsChoice) return def.ChoiceIndex(raw) >= 0;
        return long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n) && def.RawFor(n) == raw;
    }

    /// <summary>The EnvGet for a key: the short form up to 16 characters, the long one beyond.</summary>
    public static int BuildEnvGet(ReadOnlySpan<char> key, Span<byte> frame) =>
        Build(key.Length <= VendorFrame.ShortKey ? (byte)VendorCommand.EnvGet : (byte)VendorCommand.EnvGetLong, key, frame);

    public static int Build(VendorCommand command, Span<byte> frame) => Build((byte)command, default, frame);

    private static void CheckKey(ReadOnlySpan<char> key, int max)
    {
        if (key.Length == 0 || key.Length > max) throw new ArgumentException("Settings key length out of range.", nameof(key));
        foreach (char c in key)
            if (c is < ' ' or > '~') throw new ArgumentException("Settings keys are printable ASCII.", nameof(key));
    }
}

/// <summary>A complete message (or, for notifications, its first fragment) from the base.</summary>
public delegate void VendorMessageHandler(byte stream, ReadOnlySpan<byte> message);

/// <summary>
/// Puts 0xC8 reports back into messages. Command-stream fragments are joined in one fixed buffer allocated once; a message
/// that does not fit, arrives out of order or has no start is dropped. For the notification stream only the first fragment
/// is delivered (every message the decoder wants fits in it) and the rest of that message, often the debug log, is skipped
/// without being copied. Nothing is allocated per report.
/// </summary>
public sealed class VendorReassembler
{
    /// <summary>Largest command reply kept (GetDect is 475 bytes).</summary>
    public const int Capacity = 512;

    private readonly byte[] _buffer = new byte[Capacity];
    private int _length;
    private byte _lastSeq;
    private bool _open;

    public void Reset()
    {
        _open = false;
        _length = 0;
    }

    public void Feed(ReadOnlySpan<byte> report, VendorMessageHandler sink)
    {
        if (report.Length < VendorFrame.HeaderLength || report[0] != VendorFrame.ReportId) return;
        byte type = report[1], len = report[2], seq = report[3];
        if (len > VendorFrame.MaxChunk || VendorFrame.HeaderLength + len > report.Length) return;
        byte stream = (byte)(type & 0xFC), pos = (byte)(type & 3);
        var chunk = report.Slice(VendorFrame.HeaderLength, len);

        if (stream == VendorFrame.StreamNotify)
        {
            if (pos is VendorFrame.First or VendorFrame.Single) sink(stream, chunk);
            return;
        }
        if (stream != VendorFrame.StreamCommand) return;

        switch (pos)
        {
            case VendorFrame.Single:
                sink(stream, chunk);
                break;
            case VendorFrame.First:
                _length = 0;
                _open = Append(chunk);
                _lastSeq = seq;
                break;
            default:
                if (!_open) return;
                if (seq != (byte)(_lastSeq + 4) || !Append(chunk)) { _open = false; return; }
                _lastSeq = seq;
                if (pos == VendorFrame.Last)
                {
                    _open = false;
                    sink(stream, new ReadOnlySpan<byte>(_buffer, 0, _length));
                }
                break;
        }
    }

    private bool Append(ReadOnlySpan<byte> chunk)
    {
        if (_length + chunk.Length > Capacity) return false;
        chunk.CopyTo(_buffer.AsSpan(_length));
        _length += chunk.Length;
        return true;
    }
}
