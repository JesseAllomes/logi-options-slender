using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slender.Settings.Logic;

/// <summary>A device whose render and hotspots are cached locally.</summary>
public sealed record CachedDevice(string DeviceId, string ImagePath, string ThumbPath, DeviceLayout Layout, int ImageWidth, int ImageHeight);

/// <summary>
/// Copies device renders and hotspot metadata from the local Logi Options+ depots into Slender's own assets folder, so
/// nothing from Logitech is ever committed. Missing sources are not an error: Ensure returns null and the UI draws a
/// vector silhouette instead.
/// </summary>
public sealed class AssetCache
{
    private const string LayoutFileName = "layout.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _depotsRoot;
    private readonly string _assetsDirectory;

    public AssetCache(string depotsRoot, string assetsDirectory)
    {
        _depotsRoot = depotsRoot;
        _assetsDirectory = assetsDirectory;
    }

    public static string DefaultDepotsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogiOptionsPlus", "depots");

    public string IconsDirectory => Path.Combine(_assetsDirectory, "icons");

    /// <summary>The device spec for a kind: Options+ model id and the resource keys to copy.</summary>
    public static (string DeviceId, string ModelId, string ImageKey, string MetadataImageKey) SpecFor(DeviceKind kind) => kind == DeviceKind.Mouse
        ? (DeviceIds.Mouse, DeviceIds.MouseModel, "device_buttons_image", "device_buttons_image")
        : (DeviceIds.Keyboard, DeviceIds.KeyboardModel, "device_keys_image", "device_keys_image");

    /// <summary>Returns the cached device, copying it from Options+ first if it is missing. Null when Options+ assets are absent.</summary>
    public CachedDevice? Ensure(DeviceKind kind)
    {
        (string deviceId, string modelId, string imageKey, string metaKey) = SpecFor(kind);
        string dir = Path.Combine(_assetsDirectory, deviceId);

        CachedDevice? cached = TryLoad(deviceId, dir);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            return Copy(kind, deviceId, modelId, imageKey, metaKey, dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static CachedDevice? TryLoad(string deviceId, string dir)
    {
        string layoutPath = Path.Combine(dir, LayoutFileName);
        try
        {
            if (!File.Exists(layoutPath))
            {
                return null;
            }

            StoredLayout? stored = JsonSerializer.Deserialize<StoredLayout>(File.ReadAllText(layoutPath), JsonOptions);
            if (stored is null || stored.Image is null || stored.Thumb is null || stored.Hotspots is null)
            {
                return null;
            }

            string image = Path.Combine(dir, stored.Image);
            string thumb = Path.Combine(dir, stored.Thumb);
            if (!File.Exists(image) || !File.Exists(thumb))
            {
                return null;
            }

            (int w, int h) = ImageSize.TryRead(image) ?? (stored.Width, stored.Height);
            return new CachedDevice(deviceId, image, thumb, new DeviceLayout(stored.Image, stored.Thumb, stored.Width, stored.Height, stored.Hotspots), w, h);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private CachedDevice? Copy(DeviceKind kind, string deviceId, string modelId, string imageKey, string metaKey, string dir)
    {
        string? folder = FindDeviceFolder(modelId);
        if (folder is null)
        {
            return null;
        }

        string manifest = File.ReadAllText(Path.Combine(folder, "manifest.json"));
        string? imageSrc = OptionsPlusLayout.FindResource(manifest, modelId, imageKey);
        string? thumbSrc = OptionsPlusLayout.FindResource(manifest, modelId, "device_image") ?? imageSrc;
        string? metaSrc = OptionsPlusLayout.FindMetadataFile(manifest, modelId);
        if (imageSrc is null || thumbSrc is null || metaSrc is null)
        {
            return null;
        }

        string imagePath = Path.Combine(folder, imageSrc);
        string thumbPath = Path.Combine(folder, thumbSrc);
        string metaPath = Path.Combine(folder, metaSrc);
        if (!File.Exists(imagePath) || !File.Exists(thumbPath) || !File.Exists(metaPath))
        {
            return null;
        }

        var parsed = OptionsPlusLayout.ParseMetadata(File.ReadAllText(metaPath), metaKey, kind);
        if (parsed is null || parsed.Value.Hotspots.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(dir);
        string imageName = "render" + Path.GetExtension(imageSrc);
        string thumbName = string.Equals(thumbSrc, imageSrc, StringComparison.OrdinalIgnoreCase) ? imageName : "thumb" + Path.GetExtension(thumbSrc);
        File.Copy(imagePath, Path.Combine(dir, imageName), overwrite: true);
        if (thumbName != imageName)
        {
            File.Copy(thumbPath, Path.Combine(dir, thumbName), overwrite: true);
        }

        var layout = new DeviceLayout(imageName, thumbName, parsed.Value.Width, parsed.Value.Height, parsed.Value.Hotspots);
        var stored = new StoredLayout
        {
            Image = layout.ImageFile,
            Thumb = layout.ThumbFile,
            Width = layout.Width,
            Height = layout.Height,
            Hotspots = [.. layout.Hotspots],
        };

        // layout.json is written last: its presence means the copy is complete.
        string layoutPath = Path.Combine(dir, LayoutFileName);
        string temp = layoutPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, JsonOptions));
        File.Move(temp, layoutPath, overwrite: true);
        string copiedImage = Path.Combine(dir, imageName);
        (int imgW, int imgH) = ImageSize.TryRead(copiedImage) ?? (layout.Width, layout.Height);
        return new CachedDevice(deviceId, copiedImage, Path.Combine(dir, thumbName), layout, imgW, imgH);
    }

    /// <summary>The newest depot folder whose manifest lists the model, or null.</summary>
    internal string? FindDeviceFolder(string modelId)
    {
        if (!Directory.Exists(_depotsRoot))
        {
            return null;
        }

        string? best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (string depot in Directory.EnumerateDirectories(_depotsRoot))
        {
            foreach (string folder in Directory.EnumerateDirectories(depot))
            {
                string manifestPath = Path.Combine(folder, "manifest.json");
                try
                {
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    DateTime time = File.GetLastWriteTimeUtc(manifestPath);
                    if (time <= bestTime && best is not null)
                    {
                        continue;
                    }

                    if (OptionsPlusLayout.HasModel(File.ReadAllText(manifestPath), modelId))
                    {
                        best = folder;
                        bestTime = time;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Skip unreadable or unparseable manifests.
                }
            }
        }

        return best;
    }

    private sealed class StoredLayout
    {
        public string? Image { get; set; }
        public string? Thumb { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<Hotspot>? Hotspots { get; set; }
    }
}
