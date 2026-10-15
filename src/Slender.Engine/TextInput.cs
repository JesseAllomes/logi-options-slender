namespace Slender.Engine;

/// <summary>One key press of typed text: a UTF-16 code unit sent as KEYEVENTF_UNICODE, or an Enter key.</summary>
public readonly record struct TextKey(ushort Unit, bool Enter);

/// <summary>Turns a snippet into the key presses that type it.</summary>
public static class TextInput
{
    /// <summary>
    /// One <see cref="TextKey"/> per UTF-16 code unit, so a surrogate pair becomes two consecutive keys (Windows joins them).
    /// "\n", "\r" and "\r\n" are one Enter key each.
    /// </summary>
    public static IReadOnlyList<TextKey> Build(string text)
    {
        var keys = new List<TextKey>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                keys.Add(new TextKey(0, Enter: true));
            }
            else if (c == '\n') keys.Add(new TextKey(0, Enter: true));
            else keys.Add(new TextKey(c, Enter: false));
        }
        return keys;
    }
}

/// <summary>Types text into the foreground app. Abstracted so tests never send input.</summary>
public interface ITextSender
{
    bool Send(IReadOnlyList<TextKey> keys);
}

/// <summary>SendInput with KEYEVENTF_UNICODE.</summary>
public sealed class SendInputTextSender : ITextSender
{
    public bool Send(IReadOnlyList<TextKey> keys) => KeyInjector.SendText(keys);
}
