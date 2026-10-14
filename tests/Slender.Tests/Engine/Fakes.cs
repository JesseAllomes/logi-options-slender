using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

internal sealed class InlineQueue : IWorkQueue
{
    public void Post(Action work) => work();
    public void Dispose() { }
}

internal sealed class FakeMouse : ILogiMouse
{
    public string Name => "fake";
    public bool IsConnected { get; set; } = true;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<ButtonEvent>? ButtonChanged;
    public event EventHandler<ThumbWheelEvent>? ThumbWheelMoved { add { } remove { } }
    public List<ushort[]> Diverts { get; } = [];
    public List<byte> Haptics { get; } = [];
    public bool ThrowOnHaptic;
    public bool Started;

    public Task StartAsync(CancellationToken ct) { Started = true; return Task.CompletedTask; }
    public Task SetDivertedControlsAsync(IReadOnlyCollection<ushort> ids, CancellationToken ct)
    {
        lock (Diverts) Diverts.Add(ids.ToArray());
        return Task.CompletedTask;
    }
    public Task SetThumbWheelDivertedAsync(bool d, CancellationToken ct) => Task.CompletedTask;
    public Task PlayHapticAsync(byte w, CancellationToken ct)
    {
        Haptics.Add(w);
        return ThrowOnHaptic ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
    }
    public Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct) => Task.FromResult<BatteryStatus?>(new BatteryStatus(80, false));
    public void Dispose() { }
    public void Press(ushort id, bool pressed) => ButtonChanged?.Invoke(this, new ButtonEvent(id, pressed));
    public void Connect(bool c) => ConnectionChanged?.Invoke(this, c);
    public int DivertCount { get { lock (Diverts) return Diverts.Count; } }
}

internal sealed class FakeHook : IInputHook
{
    public event EventHandler<HookWheelEventArgs>? HorizontalWheel;
    public bool Started;
    public void Start() => Started = true;
    public void Dispose() { }
    public HookWheelEventArgs Wheel(int delta)
    {
        var a = new HookWheelEventArgs(delta);
        HorizontalWheel?.Invoke(this, a);
        return a;
    }
}

internal sealed class FakeForeground : IForegroundApp
{
    public string? ProcessName { get; set; }
    public nint WindowHandle { get; set; } = 42;
    public event EventHandler<string?>? Changed { add { } remove { } }
}

internal sealed class FakeExcel : IExcelBridge
{
    public List<(nint, int)> Scrolls { get; } = [];
    public List<ExcelCommand> Commands { get; } = [];
    public bool Throw;
    public bool TryScrollColumns(nint w, int c) { if (Throw) throw new InvalidOperationException("x"); Scrolls.Add((w, c)); return true; }
    public bool TryRunCommand(nint w, ExcelCommand c) { if (Throw) throw new InvalidOperationException("x"); Commands.Add(c); return true; }
}

internal sealed class FakeExecutor : IActionExecutor
{
    public List<(ActionSpec, bool)> Calls { get; } = [];
    public bool Throw;
    public void Execute(ActionSpec a, bool pressed) { if (Throw) throw new InvalidOperationException("x"); Calls.Add((a, pressed)); }
}

internal sealed class ListLog : ILog
{
    public List<string> Errors { get; } = [];
    public void Info(string m) { }
    public void Error(string m, Exception? ex = null) { lock (Errors) Errors.Add(m); }
}
