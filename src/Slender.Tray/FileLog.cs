using System.Text;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>Append-only log at %LOCALAPPDATA%\Slender\logs\slender.log, rolled once at 1 MB.</summary>
public sealed class FileLog : ILog
{
    private const long MaxBytes = 1024 * 1024;
    private readonly string _path;
    private readonly object _gate = new();

    public FileLog(string? directory = null)
    {
        var dir = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slender", "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "slender.log");
    }

    public void Info(string message) => Write("INFO ", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes) File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
