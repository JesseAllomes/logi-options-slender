using System.Text.RegularExpressions;

namespace Slender.Settings.Logic;

/// <summary>Finds the best logo PNG of a Store (WindowsApps) package. Reads the package folder only.</summary>
public static partial class StoreLogo
{
    [GeneratedRegex(@"(?:Square44x44Logo|Square150x150Logo)\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ManifestLogo();

    /// <summary>The package folder for an exe under ...\WindowsApps\&lt;package&gt;\, or null.</summary>
    public static string? PackageDirectory(string exePath)
    {
        const string marker = @"\WindowsApps\";
        int i = exePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        int slash = exePath.IndexOf('\\', i + marker.Length);
        return slash <= 0 ? null : exePath[..slash];
    }

    /// <summary>
    /// Logos named in AppxManifest.xml (the manifest names a base like Images\App.png; the files are App.scale-200.png,
    /// App.targetsize-256.png and so on), else files named like Square44x44Logo. The largest plain variant wins.
    /// </summary>
    public static string? Find(string package)
    {
        var bases = new List<string>();
        string manifest = Path.Combine(package, "AppxManifest.xml");
        if (File.Exists(manifest))
        {
            foreach (Match m in ManifestLogo().Matches(File.ReadAllText(manifest)))
            {
                string rel = m.Groups[1].Value.Replace('/', '\\');
                if (!bases.Contains(rel, StringComparer.OrdinalIgnoreCase))
                {
                    bases.Add(rel);
                }
            }
        }

        foreach (string rel in bases)
        {
            string dir = Path.Combine(package, Path.GetDirectoryName(rel) ?? string.Empty);
            string stem = Path.GetFileNameWithoutExtension(rel);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            string? best = Largest(Directory.EnumerateFiles(dir, stem + "*.png", SearchOption.TopDirectoryOnly).Where(f => IsPlainVariant(f, stem)));
            if (best is not null)
            {
                return best;
            }
        }

        foreach (string kind in new[] { "*Square44x44Logo*.png", "*Square150x150Logo*.png", "*StoreLogo*.png" })
        {
            string? best = Largest(Directory.EnumerateFiles(package, kind, SearchOption.AllDirectories).Where(f => IsPlainVariant(f, null)));
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>True for App.png, App.scale-N.png, App.targetsize-N.png; false for another file sharing the prefix, high contrast, light-unplated.</summary>
    public static bool IsPlainVariant(string file, string? stem)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        if (name.Contains("contrast-", StringComparison.OrdinalIgnoreCase) || name.Contains("lightunplated", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (stem is null)
        {
            return true;
        }

        return name.Equals(stem, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Largest(IEnumerable<string> files)
    {
        string? best = null;
        long size = -1;
        foreach (string f in files)
        {
            long len = new FileInfo(f).Length;
            if (len > size)
            {
                best = f;
                size = len;
            }
        }

        return best;
    }
}
