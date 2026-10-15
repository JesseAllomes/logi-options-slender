namespace Slender.Tray;

/// <summary>
/// Holds the engines back while Logi Options+ runs. Evaluate() is called at startup and then every
/// <see cref="PollInterval"/> while paused; once Options+ is gone it starts the engines exactly once.
/// It never touches Options+.
/// </summary>
public sealed class OptionsPlusGuard
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly Func<bool> _optionsPlusRunning;
    private readonly Action _startEngines;

    public OptionsPlusGuard(Func<bool> optionsPlusRunning, Action startEngines)
    {
        _optionsPlusRunning = optionsPlusRunning ?? throw new ArgumentNullException(nameof(optionsPlusRunning));
        _startEngines = startEngines ?? throw new ArgumentNullException(nameof(startEngines));
    }

    public bool Paused { get; private set; }

    public bool Started { get; private set; }

    /// <summary>Probes once. Returns true while paused.</summary>
    public bool Evaluate()
    {
        if (Started)
        {
            return false;
        }

        if (_optionsPlusRunning())
        {
            Paused = true;
            return true;
        }

        Paused = false;
        Started = true;
        _startEngines();
        return false;
    }
}
