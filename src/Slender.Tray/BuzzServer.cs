using System.IO.Pipes;
using System.Text;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// The buzz pipe: other programs write one line (an alert name, or nothing) and the mouse plays that alert; Settings' Test
/// button writes a "!test ..." line (see <see cref="BuzzProtocol"/>). Current user only, never a network port. Lines over 64
/// characters are ignored and at most about five per second are played; the rest are dropped. All playing is queued by the engine.
/// </summary>
public sealed class BuzzServer : IDisposable
{
    private const int MaxClients = 4;
    private readonly SlenderEngine _engine;
    private readonly ILog _log;
    private readonly BuzzRateLimiter _limiter = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public BuzzServer(SlenderEngine engine, ILog? log = null)
    {
        _engine = engine;
        _log = log ?? NullLog.Instance;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(BuzzProtocol.PipeName, PipeDirection.In, MaxClients, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                _ = Task.Run(() => ServeAsync(pipe, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.Error("buzz pipe failed", ex);
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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(1000); // a client that never finishes its line is cut off
                string? line = await ReadLineAsync(pipe, timeout.Token).ConfigureAwait(false);
                Handle(line);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex) { _log.Error("buzz line failed", ex); }
        }
    }

    /// <summary>One line, at most MaxLineChars + a little; an over-long line comes back as a string that Parse rejects.</summary>
    private static async Task<string?> ReadLineAsync(Stream pipe, CancellationToken ct)
    {
        var buffer = new byte[(BuzzProtocol.MaxLineChars + 1) * 4];
        int used = 0;
        while (used < buffer.Length)
        {
            int n = await pipe.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (n == 0) break;
            int nl = Array.IndexOf(buffer, (byte)'\n', used, n);
            used += n;
            if (nl >= 0) { used = nl; break; }
        }
        return used >= buffer.Length ? new string('x', BuzzProtocol.MaxLineChars + 1) : Encoding.UTF8.GetString(buffer, 0, used);
    }

    /// <summary>Plays what a line asks for (rate limited); public so tests can drive it without a pipe.</summary>
    internal void Handle(string? line)
    {
        if (BuzzProtocol.Parse(line) is not { } request) return;
        if (request.Ring is null && request.RingResult is null && !_limiter.TryAcquire()) return; // a ring stop must never lose to the rate limit
        if (request.Ring is { } ring) _engine.SignalRing(ring);
        else if (request.RingResult is { } result) _engine.NoteRingResult(result);
        else if (request.TestCue is { } test) _engine.PlayCue(test);
        else _engine.PlayAlert(request.Name);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
    }
}
