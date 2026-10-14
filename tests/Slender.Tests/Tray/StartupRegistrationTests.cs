using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class StartupRegistrationTests
{
    private const string ExePath = @"C:\Apps\Slender\Slender.Tray.exe";

    [Fact]
    public void Enable_writes_quoted_exe_path_and_reports_enabled()
    {
        var run = new FakeRunKey();
        var startup = new StartupRegistration(run, ExePath);

        Assert.False(startup.IsEnabled);

        startup.Enable();

        Assert.Equal("\"" + ExePath + "\"", run.Get(StartupRegistration.ValueName));
        Assert.True(startup.IsEnabled);
    }

    [Fact]
    public void Disable_removes_value()
    {
        var run = new FakeRunKey();
        var startup = new StartupRegistration(run, ExePath);
        startup.Enable();

        startup.Disable();

        Assert.Null(run.Get(StartupRegistration.ValueName));
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void Disable_when_not_enabled_is_harmless()
    {
        var run = new FakeRunKey();
        var startup = new StartupRegistration(run, ExePath);

        startup.Disable();

        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void Value_pointing_at_another_exe_is_not_enabled()
    {
        var run = new FakeRunKey();
        run.SetValue(StartupRegistration.ValueName, @"""C:\Old\Slender.Tray.exe""");

        var startup = new StartupRegistration(run, ExePath);

        Assert.False(startup.IsEnabled);
    }

    private sealed class FakeRunKey : IRunKey
    {
        private readonly Dictionary<string, string> _values = new();

        public string? Get(string name) => _values.GetValueOrDefault(name);

        public string? GetValue(string name) => Get(name);

        public void SetValue(string name, string value) => _values[name] = value;

        public void DeleteValue(string name) => _values.Remove(name);
    }
}
