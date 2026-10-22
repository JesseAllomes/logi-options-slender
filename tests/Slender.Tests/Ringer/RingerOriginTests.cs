using System.Text.Json;
using Slender.Engine.Ringer;

namespace Slender.Tests.Ringer;

public class RingerOriginTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Slender.slnx"))) dir = dir.Parent;
        return Path.Combine([dir!.FullName, .. parts]);
    }

    [Fact]
    public void The_extension_id_is_the_first_half_of_the_key_hash_written_a_to_p()
    {
        string key = Convert.ToBase64String(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(key));
        string expected = new(Convert.ToHexString(hash)[..32].ToLowerInvariant().Select(c => (char)('a' + Convert.ToInt32(c.ToString(), 16))).ToArray());
        Assert.Equal(expected, RingerOrigin.ExtensionId(key));
        Assert.Equal(32, expected.Length);
        Assert.All(expected, c => Assert.InRange(c, 'a', 'p'));
    }

    [Fact]
    public void The_shipped_manifest_has_a_stable_id_a_placeholder_origin_and_only_the_native_messaging_permission()
    {
        string text = File.ReadAllText(RepoFile("tools", "Slender.EdgeRinger", "extension", "manifest.json"));
        Assert.Equal("focneaomjdpfgihimkieknghnhidhpnk", RingerInstaller.ExtensionIdFromManifest(text));
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["nativeMessaging"], doc.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        foreach (var script in doc.RootElement.GetProperty("content_scripts").EnumerateArray())
            Assert.Equal(["__ORIGIN__/*"], script.GetProperty("matches").EnumerateArray().Select(m => m.GetString()));
        Assert.DoesNotContain("host_permissions", text);
    }

    [Theory]
    [InlineData("https://pbx.example.com", "https://pbx.example.com")]
    [InlineData("  HTTPS://PBX.Example.com/ ", "https://pbx.example.com")]
    [InlineData("https://pbx.example.com:5001", "https://pbx.example.com:5001")]
    [InlineData("https://pbx.example.com:443", "https://pbx.example.com")]
    public void A_https_origin_is_normalised(string input, string expected) => Assert.Equal(expected, RingerOrigin.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pbx.example.com")]
    [InlineData("http://pbx.example.com")]
    [InlineData("https://pbx.example.com/webclient")]
    [InlineData("https://pbx.example.com/?a=1")]
    [InlineData("https://pbx.example.com/#x")]
    [InlineData("https://user@pbx.example.com")]
    [InlineData("https://*.example.com")]
    [InlineData("https://pbx.example.com\" , \"x")]
    [InlineData("ftp://pbx.example.com")]
    public void Anything_but_a_plain_https_origin_is_refused(string? input) => Assert.Null(RingerOrigin.Normalize(input));

    [Fact]
    public void An_address_over_the_length_limit_is_refused() =>
        Assert.Null(RingerOrigin.Normalize("https://" + new string('a', RingerOrigin.MaxLength) + ".com"));
}
