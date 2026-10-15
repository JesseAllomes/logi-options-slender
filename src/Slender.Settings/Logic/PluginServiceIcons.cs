using System.Security.Cryptography;
using Slender.Engine;
using Slender.Engine.Import;

namespace Slender.Settings.Logic;

/// <summary>
/// The custom images of imported Actions Ring actions. The Plugin Service keeps one PNG per action in the profile's
/// ActionImages folder; <see cref="For"/> reads it (through a temp copy, never the original), names it by a hash of its content
/// and remembers it, and <see cref="Save"/> writes the files to %LOCALAPPDATA%\Slender\assets\ring after the import is saved.
/// Same image, same file, so importing again never piles up copies.
/// </summary>
public sealed class PluginServiceIcons
{
    private readonly string _directory;
    private readonly Func<string, byte[]?> _read;
    private readonly Dictionary<string, byte[]> _pending = new(StringComparer.OrdinalIgnoreCase);

    public PluginServiceIcons(string? directory = null, Func<string, byte[]?>? readBytes = null)
    {
        _directory = directory ?? Path.Combine(SlenderPaths.AssetsDirectory, "ring");
        _read = readBytes ?? ReadThroughCopy;
    }

    public bool HasPending => _pending.Count > 0;

    /// <summary>"file:&lt;asset path&gt;" for the action's image, or null when it has none.</summary>
    public string? For(PluginServiceProfile profile, string action)
    {
        string source = Path.Combine(profile.ProfileDirectory, "ActionImages", action + ".png");
        byte[]? bytes;
        try
        {
            bytes = _read(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        string name = Convert.ToHexString(SHA256.HashData(bytes), 0, 8).ToLowerInvariant() + ".png";
        string target = Path.Combine(_directory, name);
        _pending[target] = bytes;
        return RingGlyphs.FilePrefix + target;
    }

    /// <summary>Writes the remembered images that are not already there. Failures are skipped: a missing image only costs a glyph.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach ((string path, byte[] bytes) in _pending)
        {
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, bytes);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // keep going
            }
        }

        _pending.Clear();
    }

    private static byte[]? ReadThroughCopy(string original)
    {
        if (!File.Exists(original))
        {
            return null;
        }

        string temp = Path.Combine(Path.GetTempPath(), "slender-icon-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            PluginServiceStore.CopyShared(original, temp);
            return File.ReadAllBytes(temp);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // temp leftovers are harmless
            }
        }
    }
}
