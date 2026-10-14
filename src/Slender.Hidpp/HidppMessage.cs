using System.Diagnostics.CodeAnalysis;

namespace Slender.Hidpp;

/// <summary>One HID++ report, normalised to the 20-byte long layout (see docs/protocol.md, Framing).</summary>
public sealed class HidppMessage
{
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    public const int LongLength = 20;
    public const int ParamLength = 16;
    public const byte Hidpp2ErrorFeature = 0xFF;
    public const byte Hidpp1ErrorFeature = 0x8F;

    private HidppMessage(byte[] raw) => Raw = raw;

    /// <summary>Normalised 20 bytes: [0]=report id, [1]=device index, [2]=feature index, [3]=(func&lt;&lt;4)|swId, [4..19]=params.</summary>
    public byte[] Raw { get; }

    public byte ReportId => Raw[0];
    public byte DeviceIndex => Raw[1];
    public byte FeatureIndex => Raw[2];
    public byte Function => (byte)(Raw[3] >> 4);
    public byte SwId => (byte)(Raw[3] & 0x0F);
    public ReadOnlySpan<byte> Params => Raw.AsSpan(4, ParamLength);

    public bool IsError => FeatureIndex == Hidpp2ErrorFeature || FeatureIndex == Hidpp1ErrorFeature;

    // Error layout: [2]=0xFF/0x8F, [3]=failed feature index, params[0]=failed (func<<4)|swId, params[1]=error code.
    public byte ErrorFeatureIndex => Raw[3];
    public byte ErrorFunction => (byte)(Raw[4] >> 4);
    public byte ErrorSwId => (byte)(Raw[4] & 0x0F);
    public byte ErrorCode => Raw[5];

    public string ToHex() => Convert.ToHexString(Raw);

    public static byte[] Encode(byte deviceIndex, byte featureIndex, byte function, byte swId, ReadOnlySpan<byte> parameters)
    {
        if (parameters.Length > ParamLength)
            throw new ArgumentException("HID++ long report carries at most 16 parameter bytes.", nameof(parameters));
        var buf = new byte[LongLength];
        buf[0] = LongReportId;
        buf[1] = deviceIndex;
        buf[2] = featureIndex;
        buf[3] = (byte)(((function & 0x0F) << 4) | (swId & 0x0F));
        parameters.CopyTo(buf.AsSpan(4));
        return buf;
    }

    /// <summary>Parses a report with or without the leading report-ID byte (Windows may or may not include it).</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out HidppMessage? message)
    {
        message = null;
        int offset = 0;
        byte reportId = LongReportId;
        if (data.Length > 0 && (data[0] == ShortReportId || data[0] == LongReportId))
        {
            reportId = data[0];
            offset = 1;
        }
        if (data.Length - offset < 3) return false;
        var raw = new byte[LongLength];
        raw[0] = reportId;
        var body = data[offset..];
        body[..Math.Min(body.Length, LongLength - 1)].CopyTo(raw.AsSpan(1));
        message = new HidppMessage(raw);
        return true;
    }
}

public class HidppException : Exception
{
    public HidppException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class HidppTimeoutException : HidppException
{
    public HidppTimeoutException(string message) : base(message) { }
}

public sealed class HidppErrorException : HidppException
{
    /// <summary>HID++ 2.0 error 8 = busy; worth retrying.</summary>
    public const byte Busy = 0x08;

    public HidppErrorException(byte code) : base($"HID++ error 0x{code:X2}") => Code = code;
    public byte Code { get; }
    public bool IsTransient => Code == Busy;
}

public sealed class HidppFeatureNotSupportedException : HidppException
{
    public HidppFeatureNotSupportedException(ushort featureId) : base($"Feature 0x{featureId:X4} not supported by device")
        => FeatureId = featureId;
    public ushort FeatureId { get; }
}
