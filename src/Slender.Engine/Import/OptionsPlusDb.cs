using System.Runtime.InteropServices;
using System.Text;

namespace Slender.Engine.Import;

/// <summary>
/// Reads the JSON blob Options+ keeps in settings.db / macros.db (table data(_id, _date_created, file BLOB)).
/// The original database is never opened by SQLite: it is copied (with -wal and -shm if present) into a fresh temp
/// folder first and the copy is opened read-only, then the temp folder is deleted. Uses Windows' built-in
/// winsqlite3.dll so no package is needed. Lives in Slender.Engine because the Settings app (which will host the
/// import button) already references the engine; the NativeAOT tray never calls it, so the trimmer drops it.
/// </summary>
public static partial class OptionsPlusDb
{
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiOptionsPlus");

    public static string SettingsPath => Path.Combine(DefaultFolder, "settings.db");
    public static string MacrosPath => Path.Combine(DefaultFolder, "macros.db");

    /// <summary>
    /// Returns the JSON text of the first row of <paramref name="originalDb"/>, or null if the file is missing, has no row
    /// or cannot be read. <paramref name="copyFile"/> (source, destination) is the only code that touches the original;
    /// tests inject it to prove nothing else does.
    /// </summary>
    public static string? ReadJson(string originalDb, Action<string, string>? copyFile = null, Func<string, byte[]?>? readBlob = null)
    {
        copyFile ??= CopyShared;
        readBlob ??= ReadFirstBlob;
        if (!File.Exists(originalDb)) return null;

        string temp = Path.Combine(Path.GetTempPath(), "slender-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string copy = Path.Combine(temp, Path.GetFileName(originalDb));
            copyFile(originalDb, copy);
            foreach (string suffix in new[] { "-wal", "-shm" })
                if (File.Exists(originalDb + suffix)) copyFile(originalDb + suffix, copy + suffix);

            byte[]? bytes = readBlob(copy);
            if (bytes is null || bytes.Length == 0) return null;
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (Exception) { /* temp leftovers are harmless */ }
        }
    }

    private static void CopyShared(string source, string destination)
    {
        // Share everything so a running Options+ is never blocked.
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    /// <summary>Opens <paramref name="dbPath"/> read-only and returns <c>select file from data limit 1</c>, or null.</summary>
    public static byte[]? ReadFirstBlob(string dbPath)
    {
        byte[] path = NullTerminated(dbPath);
        nint db = 0, stmt = 0;
        try
        {
            if (Sqlite.OpenV2(path, out db, Sqlite.OpenReadOnly, 0) != Sqlite.Ok) return null;
            if (Sqlite.PrepareV2(db, NullTerminated("select file from data limit 1"), -1, out stmt, 0) != Sqlite.Ok) return null;
            if (Sqlite.Step(stmt) != Sqlite.Row) return null;

            int length = Sqlite.ColumnBytes(stmt, 0);
            nint blob = Sqlite.ColumnBlob(stmt, 0);
            if (length <= 0 || blob == 0) return null;
            var result = new byte[length];
            Marshal.Copy(blob, result, 0, length);
            return result;
        }
        finally
        {
            if (stmt != 0) Sqlite.Finalize(stmt);
            if (db != 0) Sqlite.Close(db);
        }
    }

    private static byte[] NullTerminated(string s)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
        return bytes;
    }

    private static partial class Sqlite
    {
        public const int Ok = 0;
        public const int Row = 100;
        public const int OpenReadOnly = 0x00000001;

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_open_v2")]
        public static partial int OpenV2(byte[] filename, out nint db, int flags, nint vfs);

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_prepare_v2")]
        public static partial int PrepareV2(nint db, byte[] sql, int nByte, out nint stmt, nint tail);

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_step")]
        public static partial int Step(nint stmt);

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_blob")]
        public static partial nint ColumnBlob(nint stmt, int column);

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_column_bytes")]
        public static partial int ColumnBytes(nint stmt, int column);

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_finalize")]
        public static partial int Finalize(nint stmt);

        [LibraryImport("winsqlite3", EntryPoint = "sqlite3_close")]
        public static partial int Close(nint db);
    }
}
