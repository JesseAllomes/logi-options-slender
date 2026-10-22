using System.Runtime.InteropServices;
using System.Text;

namespace Slender.Settings.Services;

/// <summary>
/// Reads the name and arguments of the user's shortcuts (Start menu, Startup and desktop) for the 3CX "Find" button. Read only, on a
/// background thread, only when the button is pressed; nothing is kept.
/// </summary>
internal static class WebAppShortcutScan
{
    private const int MaxShortcuts = 400;

    public static List<(string Name, string? Arguments)> Read()
    {
        var result = new List<(string, string?)>();
        foreach (string folder in Folders())
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string file in files)
            {
                if (result.Count >= MaxShortcuts) return result;
                result.Add((Path.GetFileNameWithoutExtension(file), Arguments(file)));
            }
        }

        return result;
    }

    private static IEnumerable<string> Folders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);       // includes Startup
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    /// <summary>The shortcut's arguments through IShellLinkW; null when it cannot be read.</summary>
    private static string? Arguments(string path)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(path, 0 /* STGM_READ */);
            var sb = new StringBuilder(2048);
            ((IShellLinkW)link).GetArguments(sb, sb.Capacity);
            return sb.ToString();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
    }
}
