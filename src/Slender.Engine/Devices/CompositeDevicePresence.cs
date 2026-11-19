namespace Slender.Engine.Devices;

/// <summary>
/// Several presence sources as one (HID plus Bluetooth Classic): Snapshot is the union, Changed forwards any source's Changed, Start
/// starts them all (a source that fails to start is skipped, the others still run), Dispose disposes them all.
/// </summary>
public sealed class CompositeDevicePresence : IDevicePresenceSource
{
    private readonly IDevicePresenceSource[] _sources;
    private readonly ILog _log;

    public CompositeDevicePresence(ILog? log, params IDevicePresenceSource[] sources)
    {
        _sources = sources;
        _log = log ?? NullLog.Instance;
        foreach (var s in _sources) s.Changed += Forward;
    }

    public event EventHandler? Changed;

    public void Start()
    {
        foreach (var s in _sources)
        {
            try { s.Start(); }
            catch (Exception e) { _log.Error("device presence source failed to start", e); }
        }
    }

    public IReadOnlySet<HidDeviceKey> Snapshot()
    {
        var all = new HashSet<HidDeviceKey>();
        foreach (var s in _sources)
        {
            try { all.UnionWith(s.Snapshot()); }
            catch (Exception e) { _log.Error("device presence snapshot failed", e); }
        }

        return all;
    }

    public void Dispose()
    {
        foreach (var s in _sources)
        {
            s.Changed -= Forward;
            try { s.Dispose(); }
            catch { }
        }
    }

    private void Forward(object? sender, EventArgs e) => Changed?.Invoke(this, e);
}
