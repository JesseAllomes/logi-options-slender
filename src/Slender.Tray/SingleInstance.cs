using System.Security.Cryptography;
using System.Text;

namespace Slender.Tray;

/// <summary>Holds the per-session named mutex. The second launch sees IsFirstInstance == false and exits.</summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\Slender.Tray";

    private readonly Mutex _mutex;
    private bool _released;

    public SingleInstance(string? name = null)
    {
        _mutex = new Mutex(initiallyOwned: true, name ?? NameFor(Environment.GetEnvironmentVariable("SLENDER_CONFIG_DIR")), out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    /// <summary>
    /// The mutex name. A test instance (SLENDER_CONFIG_DIR set) gets its own name keyed off the folder, so it can run beside the
    /// real tray without sharing its single-instance lock; the real tray keeps the plain name.
    /// </summary>
    public static string NameFor(string? configDir)
    {
        if (string.IsNullOrEmpty(configDir))
        {
            return MutexName;
        }

        string full = Path.GetFullPath(configDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return MutexName + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
    }

    /// <summary>Releases the lock now (used before a restart, so the new instance can take it).</summary>
    public void Release()
    {
        if (IsFirstInstance && !_released)
        {
            _released = true;
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        // Only release what this instance actually owns.
        Release();
        _mutex.Dispose();
    }
}
