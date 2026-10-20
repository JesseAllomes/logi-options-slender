using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>Which pointer speed (0x2201 DPI) a foreground app asks for.</summary>
public static class AppDpi
{
    /// <summary>
    /// The DPI in effect for the app: its own profile's, else the default (All apps) profile's, else PointerSettings.Dpi.
    /// Null = no one asked for a speed, so the mouse keeps its own.
    /// </summary>
    public static int? For(SlenderConfig config, string? process)
    {
        if (process is not null)
        {
            string name = Path.GetFileNameWithoutExtension(process);
            Profile? own = config.Profiles.FirstOrDefault(p => p.AppProcess is not null
                && string.Equals(Path.GetFileNameWithoutExtension(p.AppProcess), name, StringComparison.OrdinalIgnoreCase));
            if (own?.Dpi is { } d) return d;
        }

        return config.Profiles.FirstOrDefault(p => p.AppProcess is null)?.Dpi ?? config.Pointer.Dpi;
    }
}

/// <summary>
/// Writes the per-app DPI to the mouse. DPI is volatile on the device, so this remembers what it last wrote and writes again
/// only when the target differs; leaving an override restores the All-apps value, or the mouse's own speed read before the first
/// override when nobody set one. Never runs on the hook thread: the engine calls <see cref="ApplyAsync"/> from a task.
/// </summary>
internal sealed class AppDpiWriter(ILogiMouse mouse, ILog log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int? _written;            // last value this connection wrote (or confirmed); null = nothing yet
    private int? _home;               // the mouse's own speed, read before the first write; survives reconnects
    private DpiCapabilities? _caps;   // what the sensor accepts, read once per connection
    private bool _capsRead;           // the read was tried this connection (a mouse without the list is not asked again)

    /// <summary>What the sensor accepts, once read on this connection; null before that or when the mouse would not say.</summary>
    public DpiCapabilities? Capabilities => Volatile.Read(ref _caps);

    /// <summary>The mouse reconnected or went away: whatever it held is gone, so the next apply writes again.</summary>
    public void Reset()
    {
        _written = null;
        Volatile.Write(ref _caps, null);
        _capsRead = false;
    }

    /// <summary>Resolves the target now (after taking the gate, so a stale call never wins) and writes it if it changed. Returns the DPI written, else null.</summary>
    public async Task<int?> ApplyAsync(Func<int?> resolve, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!mouse.IsConnected) return null;
            if (_home is null && _written is null && await mouse.GetDpiAsync(ct).ConfigureAwait(false) is { } current) _home = current;
            if (!_capsRead) // the one list read per connection: it also tells Settings the sensor's real range
            {
                Volatile.Write(ref _caps, await mouse.GetDpiCapabilitiesAsync(ct).ConfigureAwait(false));
                _capsRead = true;
            }

            int? target = resolve();
            if (target is null && _written is null) return null; // nobody asked for a speed and we never changed it
            target ??= _home;
            if (target is not { } wanted) return null;
            if (_caps is { } caps) wanted = caps.Clamp(wanted);
            if (_written == wanted) return null;
            if (_written is null && _home == wanted) { _written = wanted; return null; } // the mouse already has it
            await mouse.SetDpiAsync(wanted, ct).ConfigureAwait(false);
            _written = wanted;
            log.Info($"pointer speed set to {wanted} DPI");
            return wanted;
        }
        finally { _gate.Release(); }
    }
}
