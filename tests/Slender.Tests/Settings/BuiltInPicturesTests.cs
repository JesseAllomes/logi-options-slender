using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class BuiltInPicturesTests
{
    private static string DevicesFolder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Slender.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Slender.Settings", "Assets", "Devices");
    }

    public static TheoryData<string> Names() => new(BuiltInPictures.All.Select(d => d.DeviceId));

    private static CachedDevice Find(string id) => BuiltInPictures.All.Single(d => d.DeviceId == id);

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_picture_is_in_the_repo_at_its_declared_size(string id)
    {
        CachedDevice device = Find(id);
        string path = Path.Combine(DevicesFolder(), device.Layout.ImageFile);

        Assert.True(File.Exists(path), path);
        Assert.Equal((device.ImageWidth, device.ImageHeight), ImageSize.Parse(File.ReadAllBytes(path)));
        Assert.Equal((device.Layout.Width, device.Layout.Height), (device.ImageWidth, device.ImageHeight));
        Assert.Equal(device.ImagePath, device.ThumbPath);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Hotspots_sit_on_the_picture_and_have_unique_ids(string id)
    {
        IReadOnlyList<Hotspot> spots = Find(id).Layout.Hotspots;

        Assert.NotEmpty(spots);
        Assert.All(spots, s =>
        {
            Assert.InRange(s.X, 0, 100);
            Assert.InRange(s.Y, 0, 100);
        });
        Assert.Equal(spots.Count, spots.Select(s => s.ControlId).Distinct().Count());
    }

    [Fact]
    public void Headset_and_base_callouts_are_the_expected_slots()
    {
        Assert.Equal(
            [YealinkSlots.TeamsButton, YealinkSlots.Busylight, YealinkSlots.BoomMute, YealinkSlots.Battery],
            BuiltInPictures.Headset.Layout.Hotspots.Select(s => s.Slot));
        Assert.Equal(
            [YealinkSlots.DeskKey, YealinkSlots.PcKey, YealinkSlots.BaseTeamsKey, YealinkSlots.MuteLight, YealinkSlots.ChargeLight, YealinkSlots.CallLight],
            BuiltInPictures.Base.Layout.Hotspots.Select(s => s.Slot));
    }

    [Fact]
    public void The_pictures_are_published_with_the_app()
    {
        // 1.1.48 shipped without them: Debug builds copied the files, the installer's publish did not.
        string csproj = File.ReadAllText(Path.Combine(DevicesFolder(), "..", "..", "Slender.Settings.csproj"));
        Assert.Contains("<Content Include=\"Assets\\Devices\\*.webp\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" />", csproj);
    }

    [Fact]
    public void Mouse_and_keyboard_resolve_by_kind()
    {
        Assert.Same(BuiltInPictures.Mouse, BuiltInPictures.For(DeviceKind.Mouse));
        Assert.Same(BuiltInPictures.Keyboard, BuiltInPictures.For(DeviceKind.Keyboard));
    }
}
