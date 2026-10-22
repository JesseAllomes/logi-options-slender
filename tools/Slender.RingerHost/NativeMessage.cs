using System.Buffers.Binary;
using System.Text.Json;
using Slender.Engine;

namespace Slender.RingerHost;

/// <summary>
/// Chrome/Edge native messaging for the 3CX ringer: one message in (4-byte little-endian length, then UTF-8 JSON), one reply out in the
/// same framing. Pure stream code so the tests drive it with memory streams. A message body is never logged.
/// </summary>
public static class NativeMessage
{
    /// <summary>Largest body accepted; the real message is about 50 bytes.</summary>
    public const int MaxBytes = 4096;

    /// <summary>Reads one framed message body. Null when the stream ends early or the announced length is zero or over <see cref="MaxBytes"/> (the body is then not read).</summary>
    public static byte[]? ReadFrame(Stream input)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            input.ReadExactly(header);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (length is 0 or > MaxBytes) return null;
            var body = new byte[length];
            input.ReadExactly(body);
            return body;
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>
    /// The signal in {"event":"ring-start"|"ring-stop","source":"hid"|"notification"}. Null for anything else: bad JSON, another event
    /// or source, a missing or non-text field, or any extra property.
    /// </summary>
    public static RingSignal? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 4 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
            string? ev = null, source = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                if (!reader.Read() || reader.TokenType != JsonTokenType.String) return null;
                string value = reader.GetString()!;
                if (name == "event" && ev is null) ev = value;
                else if (name == "source" && source is null) source = value;
                else return null;
            }
            if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) return null; // nothing may follow the object
            bool start;
            if (ev == "ring-start") start = true;
            else if (ev == "ring-stop") start = false;
            else return null;
            if (source == "hid") return new RingSignal(start, RingerSignalSource.Headset);
            if (source == "notification") return new RingSignal(start, RingerSignalSource.Notification);
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Writes {"ok":true} or {"ok":false}, framed.</summary>
    public static void WriteReply(Stream output, bool ok)
    {
        byte[] body = ok ? "{\"ok\":true}"u8.ToArray() : "{\"ok\":false}"u8.ToArray();
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}
