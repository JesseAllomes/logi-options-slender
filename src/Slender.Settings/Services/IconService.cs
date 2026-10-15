using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Slender.Settings.Services;

/// <summary>
/// Extracts the real icon of an app at 256 px and caches it as a PNG under the assets folder. Store apps use the package
/// largest app-list logo. Nothing is held in memory after the call; the UI decodes the PNG at display size.
/// </summary>
public sealed class IconService
{
    private const int SiigbfBiggerSizeOk = 0x1;
    private const int SiigbfIconOnly = 0x4;
    private const int IconSize = 256;

    private readonly string _iconsDirectory;

    public IconService(string iconsDirectory)
    {
        _iconsDirectory = iconsDirectory;
    }

    public static string CacheName(string process) => process.ToLowerInvariant() + ".png";

    /// <summary>Returns the cached PNG path for an app, creating it if needed; null if no icon could be extracted.</summary>
    public async Task<string?> GetIconPathAsync(string process, string? exePath)
    {
        string target = Path.Combine(_iconsDirectory, CacheName(process));
        if (File.Exists(target))
        {
            return target;
        }

        try
        {
            string? exe = exePath is not null && File.Exists(exePath) ? exePath : FindRunningExe(process) ?? FindRegisteredExe(process);
            if (exe is null)
            {
                return null;
            }

            Directory.CreateDirectory(_iconsDirectory);

            string? logo = FindStoreLogo(exe);
            if (logo is not null)
            {
                File.Copy(logo, target, overwrite: true);
                return target;
            }

            byte[]? png = await ExtractShellIconPngAsync(exe);
            if (png is null)
            {
                return null;
            }

            await File.WriteAllBytesAsync(target, png);
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Path of the main module of a running process by name, or null (not running or access denied).</summary>
    public static string? FindRunningExe(string process)
    {
        Process[] found;
        try
        {
            found = Process.GetProcessesByName(process);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        string? result = null;
        foreach (Process p in found)
        {
            try
            {
                result ??= p.MainModule?.FileName;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Elevated or exiting process: try the next one.
            }
            finally
            {
                p.Dispose();
            }
        }

        return result;
    }

    /// <summary>Path registered under App Paths for process.exe (HKCU first, then HKLM), or null. Lets icons show while the app is closed.</summary>
    public static string? FindRegisteredExe(string process)
    {
        string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + process + ".exe";
        foreach (Microsoft.Win32.RegistryKey hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using Microsoft.Win32.RegistryKey? sub = hive.OpenSubKey(key);
                if (sub?.GetValue(null) is string path)
                {
                    path = path.Trim('"');
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Not readable: try the next hive.
            }
        }

        return null;
    }

    /// <summary>For a path under ...\WindowsApps\&lt;package&gt;\, the package's largest logo PNG; else null.</summary>
    internal static string? FindStoreLogo(string exePath)
    {
        string? package = Logic.StoreLogo.PackageDirectory(exePath);
        if (package is null)
        {
            return null;
        }

        try
        {
            return Logic.StoreLogo.Find(package);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // the package folder is often not readable; fall back to the shell icon
        }
    }

    private static async Task<byte[]?> ExtractShellIconPngAsync(string exe)
    {
        NativeMethods.SHCreateItemFromParsingName(exe, 0, NativeMethods.IidShellItemImageFactory, out NativeMethods.IShellItemImageFactory factory);
        nint hbm = 0;
        try
        {
            int hr = factory.GetImage(new NativeMethods.Size { Cx = IconSize, Cy = IconSize }, SiigbfBiggerSizeOk | SiigbfIconOnly, out hbm);
            if (hr != 0 || hbm == 0)
            {
                return null;
            }

            if (NativeMethods.GetObject(hbm, Marshal.SizeOf<NativeMethods.Bitmap>(), out NativeMethods.Bitmap bmp) == 0 || bmp.Width <= 0 || bmp.Height <= 0)
            {
                return null;
            }

            var info = new NativeMethods.BitmapInfo
            {
                Size = 40,
                Width = bmp.Width,
                Height = -bmp.Height, // top-down
                Planes = 1,
                BitCount = 32,
            };
            byte[] pixels = new byte[bmp.Width * bmp.Height * 4];
            nint dc = NativeMethods.CreateCompatibleDC(0);
            try
            {
                if (NativeMethods.GetDIBits(dc, hbm, 0, (uint)bmp.Height, pixels, ref info, 0) == 0)
                {
                    return null;
                }
            }
            finally
            {
                NativeMethods.DeleteDC(dc);
            }

            // Icons without an alpha channel come back with alpha 0 everywhere: make them opaque.
            bool anyAlpha = false;
            for (int p = 3; p < pixels.Length; p += 4)
            {
                if (pixels[p] != 0)
                {
                    anyAlpha = true;
                    break;
                }
            }

            if (!anyAlpha)
            {
                for (int p = 3; p < pixels.Length; p += 4)
                {
                    pixels[p] = 255;
                }
            }

            using var stream = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bmp.Width, (uint)bmp.Height, 96, 96, pixels);
            await encoder.FlushAsync();
            stream.Seek(0);
            byte[] png = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(png);
            return png;
        }
        finally
        {
            if (hbm != 0)
            {
                NativeMethods.DeleteObject(hbm);
            }

            Marshal.ReleaseComObject(factory);
        }
    }
}
