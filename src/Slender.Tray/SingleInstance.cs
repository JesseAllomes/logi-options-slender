namespace Slender.Tray;

/// <summary>Holds the per-session named mutex. The second launch sees IsFirstInstance == false and exits.</summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\Slender.Tray";

    private readonly Mutex _mutex;

    public SingleInstance(string name = MutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public void Dispose()
    {
        // Only release what this instance actually owns.
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
