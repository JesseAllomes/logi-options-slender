namespace Slender.GalaxyBuds;

/// <summary>One decoded frame from the buds: message id and payload (CRC and framing removed).</summary>
public readonly record struct BudsFrame(byte Id, byte[] Payload);

/// <summary>
/// Galaxy Buds control-channel framing: 0xFD, header u16 LE (low 10 bits = byte count of id + payload + CRC; higher bits are flags
/// and a sequence counter the buds set on their own frames and we ignore), id, payload, CRC-16/XMODEM over id + payload (LE), 0xDD.
/// </summary>
public static class BudsCodec
{
    public const byte Start = 0xFD;
    public const byte End = 0xDD;
    public const int LengthMask = 0x03FF;
    public const int MaxBody = 600;

    /// <summary>CRC-16/XMODEM: polynomial 0x1021, initial value 0.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>Builds a request frame; the header carries the length only.</summary>
    public static byte[] Encode(byte id, ReadOnlySpan<byte> payload)
    {
        int body = 1 + payload.Length + 2;
        if (body > LengthMask) throw new ArgumentException("Payload too long.", nameof(payload));
        var frame = new byte[body + 4];
        frame[0] = Start;
        frame[1] = (byte)(body & 0xFF);
        frame[2] = (byte)(body >> 8);
        frame[3] = id;
        payload.CopyTo(frame.AsSpan(4));
        ushort crc = Crc16(frame.AsSpan(3, 1 + payload.Length));
        frame[4 + payload.Length] = (byte)(crc & 0xFF);
        frame[5 + payload.Length] = (byte)(crc >> 8);
        frame[6 + payload.Length] = End;
        return frame;
    }
}

/// <summary>
/// Turns the byte stream from the buds into frames. Feed any split of the stream; frames come out once complete. A frame with a bad
/// CRC or a wrong end byte is skipped (resynchronising on the next start byte); unknown ids are returned like any other.
/// </summary>
public sealed class BudsFrameReader
{
    private byte[] _buf = new byte[1024];
    private int _len;

    /// <summary>Count of frames dropped for a bad CRC or end byte.</summary>
    public int Dropped { get; private set; }

    public void Reset() { _len = 0; }

    public void Feed(ReadOnlySpan<byte> data, List<BudsFrame> frames)
    {
        if (_len + data.Length > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + data.Length));
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;

        int pos = 0;
        while (true)
        {
            while (pos < _len && _buf[pos] != BudsCodec.Start) pos++;
            if (_len - pos < 3) break;
            int body = (_buf[pos + 1] | (_buf[pos + 2] << 8)) & BudsCodec.LengthMask;
            // The largest frame seen is 522 bytes; a longer claim is a stray start byte, not a frame to wait for.
            if (body < 3 || body > BudsCodec.MaxBody)
            {
                Dropped++;
                pos++;
                continue;
            }
            int total = body + 4;
            if (_len - pos < total) break;
            var frame = _buf.AsSpan(pos, total);
            ushort want = (ushort)(frame[total - 3] | (frame[total - 2] << 8));
            if (frame[total - 1] == BudsCodec.End && BudsCodec.Crc16(frame.Slice(3, body - 2)) == want)
            {
                frames.Add(new BudsFrame(frame[3], frame.Slice(4, body - 3).ToArray()));
                pos += total;
            }
            else
            {
                Dropped++;
                pos++;
            }
        }

        if (pos > 0)
        {
            Buffer.BlockCopy(_buf, pos, _buf, 0, _len - pos);
            _len -= pos;
        }
        // A stray start byte that never completes must not grow without bound.
        if (_len > 4096) { _len = 0; Dropped++; }
    }
}
