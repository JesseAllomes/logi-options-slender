using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class UiStateSizeTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "slender-ui-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public void An_older_short_window_grows_to_the_taller_default_once()
    {
        File.WriteAllText(_file, """{"Width":1160,"Height":700}""");
        UiState s = UiState.Load(_file);
        Assert.Equal(UiState.DefaultHeight, s.Height);
        s.Height = 880; // the user makes it shorter again afterwards: that is kept
        s.Save(_file);
        Assert.Equal(880, UiState.Load(_file).Height);
    }

    [Fact]
    public void A_taller_remembered_window_is_kept()
    {
        File.WriteAllText(_file, """{"Width":1160,"Height":1100}""");
        Assert.Equal(1100, UiState.Load(_file).Height);
    }

    [Fact]
    public void New_windows_open_at_the_taller_default() => Assert.Equal(UiState.DefaultHeight, new UiState().Height);
}
