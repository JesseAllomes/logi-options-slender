using System.IO.Pipes;
using System.Text;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// The on-keyboard remap pipe: Settings sends one request line (read, or apply after the user saved a change) and gets one reply
/// line. Current user only. This is the only way the tray ever writes keyboard flash; nothing runs on connect, start or config reload.
/// </summary>
public sealed class OnboardServer : IDisposable
{
    private const int MaxClients = 2;
    private readonly OnboardRemapService _service;
    private readonly ILog _log;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public OnboardServer(OnboardRemapService service, ILog? log = null, string pipeName = OnboardProtocol.PipeName)
    {
        _service = service;
        _log = log ?? NullLog.Instance;
        _pipeName = pipeName;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                _ = Task.Run(() => ServeAsync(pipe, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.Error("on-keyboard remap pipe failed", ex);
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            try
            {
                string? line;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(2000); // a client that never finishes its line is cut off
                    line = await ReadLineAsync(pipe, timeout.Token).ConfigureAwait(false);
                }

                string reply = await _service.HandleLineAsync(line, ct).ConfigureAwait(false);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(reply + "\n"), ct).ConfigureAwait(false);
                await pipe.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex) { _log.Error("on-keyboard remap request failed", ex); }
        }
    }

    /// <summary>One line; an over-long line comes back as a string the protocol rejects.</summary>
    private static async Task<string?> ReadLineAsync(Stream pipe, CancellationToken ct)
    {
        var buffer = new byte[(OnboardProtocol.MaxLineChars + 1) * 4];
        int used = 0;
        while (used < buffer.Length)
        {
            int n = await pipe.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (n == 0) break;
            int nl = Array.IndexOf(buffer, (byte)'\n', used, n);
            used += n;
            if (nl >= 0) { used = nl; break; }
        }

        return used >= buffer.Length ? new string('x', OnboardProtocol.MaxLineChars + 1) : Encoding.UTF8.GetString(buffer, 0, used).TrimEnd('\r');
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
    }
}
