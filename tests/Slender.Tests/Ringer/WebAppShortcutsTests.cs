using Slender.Engine.Ringer;

namespace Slender.Tests.Ringer;

public class WebAppShortcutsTests
{
    private const string EdgeArgs = " --profile-directory=Default --app-id=abcdefghijklmnopabcdefghijklmnop --app-url=https://pbx.example.com/ --app-run-on-os-login-mode=windowed";

    [Theory]
    [InlineData(EdgeArgs, "https://pbx.example.com/")]
    [InlineData("--app-url=\"https://pbx.example.com/webclient\" --x", "https://pbx.example.com/webclient")]
    [InlineData("--app-url=https://pbx.example.com", "https://pbx.example.com")]
    [InlineData("--profile-directory=Default", null)]
    [InlineData("--app-url=", null)]
    [InlineData(null, null)]
    public void App_url_is_read_from_the_arguments(string? args, string? expected) => Assert.Equal(expected, WebAppShortcuts.AppUrl(args));

    [Fact]
    public void The_3cx_shortcut_gives_its_origin()
    {
        (string, string?)[] shortcuts =
        [
            ("Outlook (PWA)", "--app-url=https://outlook.example.com/mail/"),
            ("Notepad", null),
            ("3CX (PWA)", EdgeArgs),
        ];
        Assert.Equal("https://pbx.example.com", WebAppShortcuts.Find3cx(shortcuts));
    }

    [Fact]
    public void A_3cx_address_counts_even_when_the_shortcut_has_another_name() =>
        Assert.Equal("https://office.3cx.example.com", WebAppShortcuts.Find3cx([("Phone", "--app-url=https://office.3cx.example.com/#/people")]));

    [Fact]
    public void Nothing_found_or_unusable_gives_null()
    {
        Assert.Null(WebAppShortcuts.Find3cx([("Outlook (PWA)", "--app-url=https://outlook.example.com/")]));
        Assert.Null(WebAppShortcuts.Find3cx([("3CX", "--app-url=http://pbx.example.com/")])); // https only
    }
}
