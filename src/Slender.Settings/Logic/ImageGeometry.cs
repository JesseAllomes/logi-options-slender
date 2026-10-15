namespace Slender.Settings.Logic;

/// <summary>Reads the pixel size of a WebP or PNG file from its header, without decoding it.</summary>
public static class ImageSize
{
    public static (int Width, int Height)? TryRead(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] header = new byte[32];
            int read = stream.Read(header, 0, header.Length);
            return Parse(header.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static (int Width, int Height)? Parse(ReadOnlySpan<byte> h)
    {
        // PNG: signature, then IHDR with big-endian width and height at 16 and 20.
        if (h.Length >= 24 && h[0] == 0x89 && h[1] == (byte)'P' && h[2] == (byte)'N' && h[3] == (byte)'G')
        {
            int w = (h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19];
            int ht = (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23];
            return w > 0 && ht > 0 ? (w, ht) : null;
        }

        // WebP: "RIFF" size "WEBP", then a VP8 (lossy), VP8L (lossless) or VP8X (extended) chunk.
        if (h.Length >= 30 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
        {
            if (h[12] == 'V' && h[13] == 'P' && h[14] == '8' && h[15] == ' ')
            {
                int w = (h[26] | (h[27] << 8)) & 0x3FFF;
                int ht = (h[28] | (h[29] << 8)) & 0x3FFF;
                return w > 0 && ht > 0 ? (w, ht) : null;
            }

            if (h[12] == 'V' && h[13] == 'P' && h[14] == '8' && h[15] == 'L')
            {
                uint bits = (uint)(h[21] | (h[22] << 8) | (h[23] << 16) | (h[24] << 24));
                return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }

            if (h[12] == 'V' && h[13] == 'P' && h[14] == '8' && h[15] == 'X')
            {
                int w = (h[24] | (h[25] << 8) | (h[26] << 16)) + 1;
                int ht = (h[27] | (h[28] << 8) | (h[29] << 16)) + 1;
                return (w, ht);
            }
        }

        return null;
    }
}

/// <summary>
/// Maps Options+ hotspot percentages onto the real image. The percentages are relative to the "origin" size in the metadata,
/// not to the image file: the mouse picture is centred in its box; the keyboard markers are the top-left of a key, in the box top-left.
/// </summary>
public static class HotspotMapper
{
    /// <summary>Position of the hotspot as a fraction (0..1) of the image width and height.</summary>
    public static (double X, double Y) ToImageFraction(DeviceKind kind, Hotspot spot, DeviceLayout layout, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || layout.Width <= 0 || layout.Height <= 0)
        {
            return (spot.X / 100, spot.Y / 100);
        }

        double px;
        double py;
        if (kind == DeviceKind.Mouse)
        {
            px = spot.X / 100 * layout.Width + (imageWidth - layout.Width) / 2.0;
            py = spot.Y / 100 * layout.Height + (imageHeight - layout.Height) / 2.0;
        }
        else
        {
            // Marker = top-left corner of the key. Measured against the render: x is a share of the picture width and
            // y a share of the origin height; +1.7% across and +5% down reach the key centre.
            px = (spot.X + 1.7) / 100 * imageWidth;
            py = (spot.Y + 5.0) / 100 * layout.Height;
        }

        return (Math.Clamp(px / imageWidth, 0, 1), Math.Clamp(py / imageHeight, 0, 1));
    }
}
