namespace Slender.Yealink.Windows;

/// <summary>The PCM format and sample bytes of a 16-bit PCM WAV.</summary>
internal readonly record struct WavPcm(int Channels, int SampleRate, int DataOffset, int DataLength)
{
    public int BlockAlign => Channels * 2;

    /// <summary>Reads a RIFF/WAVE file with 16-bit integer PCM; false for anything else.</summary>
    public static bool TryParse(ReadOnlySpan<byte> file, out WavPcm wav)
    {
        wav = default;
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("WAVE"u8)) return false;
        int channels = 0, rate = 0;
        for (int at = 12; at + 8 <= file.Length;)
        {
            var id = file.Slice(at, 4);
            int size = BitConverter.ToInt32(file.Slice(at + 4, 4));
            int body = at + 8;
            if (size < 0 || body + (long)size > file.Length) size = Math.Max(0, file.Length - body);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                bool pcm = BitConverter.ToUInt16(file.Slice(body, 2)) == 1;
                channels = BitConverter.ToUInt16(file.Slice(body + 2, 2));
                rate = BitConverter.ToInt32(file.Slice(body + 4, 4));
                if (!pcm || BitConverter.ToUInt16(file.Slice(body + 14, 2)) != 16) return false;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels is < 1 or > 8 || rate < 8000) return false;
                int block = channels * 2;
                int length = size / block * block;
                if (length == 0) return false;
                wav = new WavPcm(channels, rate, body, length);
                return true;
            }

            at = body + size + (size & 1);
        }

        return false;
    }
}
