using System.Security.Cryptography;
using System.Text;

namespace Slender.Engine.Ringer;

/// <summary>The 3CX web address the Edge extension is allowed to run on, and the extension's fixed ID.</summary>
public static class RingerOrigin
{
    public const int MaxLength = 120;

    /// <summary>
    /// The address as an origin ("https://host" or "https://host:port", lower case), or null when it is not usable: https only, no
    /// user name, path, query or fragment (a lone "/" is dropped), letters, digits, '-' and '.' in the host, at most <see cref="MaxLength"/> characters.
    /// The origin goes into a manifest file, so anything outside this narrow shape is refused instead of escaped.
    /// </summary>
    public static string? Normalize(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/") return null;
        string host = uri.IdnHost.ToLowerInvariant();
        if (host.Length == 0 || host[0] is '.' or '-' || !host.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.')) return null;
        return uri.IsDefaultPort ? $"https://{host}" : $"https://{host}:{uri.Port}";
    }

    /// <summary>The extension ID Edge derives from the manifest's public key (base64 DER): the first 32 hex digits of its SHA-256, 0-f written as a-p.</summary>
    public static string ExtensionId(string publicKeyBase64)
    {
        byte[] hash = SHA256.HashData(Convert.FromBase64String(publicKeyBase64));
        var id = new StringBuilder(32);
        for (int i = 0; i < 16; i++)
        {
            id.Append((char)('a' + (hash[i] >> 4)));
            id.Append((char)('a' + (hash[i] & 0xF)));
        }
        return id.ToString();
    }
}
