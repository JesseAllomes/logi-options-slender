using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slender.Settings.Logic;

/// <summary>A device whose render and hotspots are cached locally.</summary>
public sealed record CachedDevice(string DeviceId, string ImagePath, string ThumbPath, DeviceLayout Layout, int ImageWidth, int ImageHeight);

/// <summary>
/// Copies device renders and hotspot metadata from the local Logi Options+ depots into Slender's own assets folder, so
/// nothing from Logitech is ever committed. Slender's copy is kept for good, with a second copy in a backup folder: Options+
/// can be uninstalled, and "Refresh pictures" once deleted the only copy before finding Options+ gone (user 2026-10-20). A
/// copy is only ever replaced by a complete new one. Missing sources are not an error: Ensure returns null and the UI draws
/// a vector silhouette instead.
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
    private readonly string? _backupDirectory;

    public AssetCache(string depotsRoot, string assetsDirectory, string? backupDirectory = null)
    {
        _depotsRoot = depotsRoot;
        _assetsDirectory = assetsDirectory;
        _backupDirectory = backupDirectory;
    }

    /// <summary>True when the Options+ picture library is on this PC (Refresh can only work then).</summary>
    public bool OptionsPlusPresent => Directory.Exists(_depotsRoot);

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
        return EnsureFrom(deviceId, dir => Copy(kind, deviceId, modelId, imageKey, metaKey, dir));
    }

    /// <summary>
    /// The same cache, backup and restore rules for any device whose pictures come from somewhere else (the Yealink headset's
    /// come from the user's own USB Connect install). copy fills the folder it is given and returns the device, or null when its
    /// source is absent.
    /// </summary>
    public CachedDevice? EnsureFrom(string deviceId, Func<string, CachedDevice?> copy)
    {
        string dir = Path.Combine(_assetsDirectory, deviceId);

        CachedDevice? cached = TryLoad(deviceId, dir);
        if (cached is not null)
        {
            Backup(deviceId, dir);
            return cached;
        }

        // The local copy is gone: the backup first, then Options+.
        if (_backupDirectory is not null && TryLoad(deviceId, Path.Combine(_backupDirectory, deviceId)) is not null)
        {
            try
            {
                CopyFolder(Path.Combine(_backupDirectory, deviceId), dir);
                if (TryLoad(deviceId, dir) is { } restored)
                {
                    return restored;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            CachedDevice? copied = copy(dir);
            if (copied is not null)
            {
                Backup(deviceId, dir);
            }

            return copied;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies the device afresh from Options+ into a staging folder and only then replaces Slender's copy (and the backup).
    /// When Options+ is missing or the copy fails, the pictures Slender has are kept. Refreshed is false then.
    /// </summary>
    public (CachedDevice? Device, bool Refreshed) Refresh(DeviceKind kind)
    {
        (string deviceId, string modelId, string imageKey, string metaKey) = SpecFor(kind);
        return RefreshFrom(deviceId, dir => Copy(kind, deviceId, modelId, imageKey, metaKey, dir));
    }

    /// <summary>Refresh for a device with its own source (see <see cref="EnsureFrom"/>): copy into staging, then replace, else keep what there is.</summary>
    public (CachedDevice? Device, bool Refreshed) RefreshFrom(string deviceId, Func<string, CachedDevice?> copy)
    {
        string dir = Path.Combine(_assetsDirectory, deviceId);
        string staging = dir + ".new";
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            if (copy(staging) is null)
            {
                return (EnsureFrom(deviceId, copy), false);
            }

            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            Directory.Move(staging, dir);
            Backup(deviceId, dir, replace: true);
            return (TryLoad(deviceId, dir), true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            return (EnsureFrom(deviceId, copy), false);
        }
    }

    /// <summary>Keeps the backup in step with a complete local copy (only creates it unless <paramref name="replace"/>).</summary>
    private void Backup(string deviceId, string dir, bool replace = false)
    {
        if (_backupDirectory is null)
        {
            return;
        }

        string target = Path.Combine(_backupDirectory, deviceId);
        try
        {
            if (!replace && TryLoad(deviceId, target) is not null)
            {
                return;
            }

            CopyFolder(dir, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Copies every file of <paramref name="from"/> into <paramref name="to"/>, layout.json last (its presence means complete).</summary>
    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        var files = Directory.GetFiles(from).OrderBy(f => string.Equals(Path.GetFileName(f), LayoutFileName, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        foreach (string file in files)
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
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

        return WriteCopy(deviceId, dir, imagePath, thumbPath, parsed.Value.Width, parsed.Value.Height, parsed.Value.Hotspots);
    }

    /// <summary>
    /// Copies the two picture files into dir as render.* and thumb.* and writes layout.json last (its presence means the copy is
    /// complete). Shared by every source of pictures.
    /// </summary>
    internal static CachedDevice WriteCopy(string deviceId, string dir, string imagePath, string thumbPath, int width, int height, IReadOnlyList<Hotspot> hotspots)
    {
        Directory.CreateDirectory(dir);
        string imageName = "render" + Path.GetExtension(imagePath);
        string thumbName = string.Equals(thumbPath, imagePath, StringComparison.OrdinalIgnoreCase) ? imageName : "thumb" + Path.GetExtension(thumbPath);
        File.Copy(imagePath, Path.Combine(dir, imageName), overwrite: true);
        if (thumbName != imageName)
        {
            File.Copy(thumbPath, Path.Combine(dir, thumbName), overwrite: true);
        }

        var layout = new DeviceLayout(imageName, thumbName, width, height, hotspots);
        var stored = new StoredLayout
        {
            Image = layout.ImageFile,
            Thumb = layout.ThumbFile,
            Width = layout.Width,
            Height = layout.Height,
            Hotspots = [.. layout.Hotspots],
        };

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
