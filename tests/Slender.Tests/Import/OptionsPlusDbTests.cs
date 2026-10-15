using System.Runtime.InteropServices;
using System.Text;
using Slender.Engine.Import;

namespace Slender.Tests.Import;

public partial class OptionsPlusDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-db-test-" + Guid.NewGuid().ToString("N"));

    public OptionsPlusDbTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [LibraryImport("winsqlite3", EntryPoint = "sqlite3_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string file, out nint db);

    [LibraryImport("winsqlite3", EntryPoint = "sqlite3_exec", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Exec(nint db, string sql, nint cb, nint arg, nint err);

    [LibraryImport("winsqlite3", EntryPoint = "sqlite3_close")]
    private static partial int Close(nint db);

    private string MakeDb(string json)
    {
        string path = Path.Combine(_dir, "orig", "settings.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.Equal(0, Open(path, out nint db));
        string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(json));
        Assert.Equal(0, Exec(db, "create table data(_id integer primary key, _date_created text, file blob);"
            + $"insert into data(_date_created, file) values ('now', x'{hex}');", 0, 0, 0));
        Close(db);
        return path;
    }

    [Fact]
    public void ReadsTheBlobFromARealSqliteFile()
    {
        const string json = "{\"profile_keys\":[],\"x\":\"café\"}";
        string original = MakeDb(json);
        Assert.Equal(json, OptionsPlusDb.ReadJson(original));
    }

    [Fact]
    public void OriginalPathIsOnlyTouchedByTheCopyStep()
    {
        string original = MakeDb("{}");
        File.WriteAllText(original + "-wal", "x");
        var copies = new List<(string Src, string Dst)>();
        var opened = new List<string>();

        string? json = OptionsPlusDb.ReadJson(original,
            copyFile: (s, d) => { copies.Add((s, d)); File.Copy(s, d); },
            readBlob: p => { opened.Add(p); return OptionsPlusDb.ReadFirstBlob(p); });

        Assert.Equal("{}", json);
        Assert.Equal([original, original + "-wal"], copies.Select(c => c.Src));
        string orig = Path.GetFullPath(original);
        Assert.All(opened, p => Assert.NotEqual(orig, Path.GetFullPath(p)));
        Assert.All(copies, c => Assert.NotEqual(orig, Path.GetFullPath(c.Dst)));
        Assert.Equal(copies[0].Dst, opened.Single());
        Assert.False(Directory.Exists(Path.GetDirectoryName(copies[0].Dst))); // temp copy cleaned up
        Assert.True(File.Exists(original)); // original untouched
    }

    [Fact]
    public void MissingFileGivesNull()
    {
        Assert.Null(OptionsPlusDb.ReadJson(Path.Combine(_dir, "nope.db")));
    }
}
