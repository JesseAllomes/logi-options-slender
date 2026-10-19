using Slender.Engine;

namespace Slender.Tests.Engine;

public class ShellLauncherTests
{
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void A_full_path_starts_in_its_own_folder()
    {
        string exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.Equal(Environment.SystemDirectory, ShellLauncher.WorkingDirectoryFor(exe));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData(@"shell:AppsFolder\Microsoft.Office.ONENOTE.EXE.15")]
    [InlineData("onenote.exe")]
    [InlineData(@"C:\no such folder here\app.exe")]
    [InlineData("")]
    public void Anything_else_starts_in_the_user_profile_never_the_install_folder(string target)
    {
        string dir = ShellLauncher.WorkingDirectoryFor(target);
        Assert.Equal(Profile, dir);
        Assert.NotEqual(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), dir.TrimEnd(Path.DirectorySeparatorChar));
    }
}
