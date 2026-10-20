using System.IO.Pipes;
using System.Text;
using ExcelDna.Integration;
using Slender.Engine.ExcelAddin;

namespace Slender.ExcelAddin;

/// <summary>
/// One pipe, "Slender.Excel.&lt;this Excel's pid&gt;", current user only, one client at a time, one request per connection.
/// Every step has a time limit: reading the request 1 s, getting Excel's main thread <see cref="MainThreadWaitMs"/> ms. A request that
/// Excel could not start in time (edit mode, a dialog) is cancelled before it can run later, and answered "busy", so Slender can use its
/// key-based fallback without the action then happening twice.
/// </summary>
internal sealed class AddinPipeServer : IDisposable
{
    /// <summary>Slender's own time box is 250 ms; answer inside it.</summary>
    internal const int MainThreadWaitMs = 180;
    private const int ReadTimeoutMs = 1000;

    private readonly ExcelActions _actions;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public AddinPipeServer(ExcelActions actions) => _actions = actions;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        var name = ExcelAddinProtocol.PipeName(Environment.ProcessId);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // CurrentUserOnly: the pipe's ACL grants the creating user alone. Other accounts cannot open it.
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await ServeAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        AddinReply reply;
        try
        {
            using var readLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readLimit.CancelAfter(ReadTimeoutMs);
            var line = await ReadLineAsync(pipe, readLimit.Token).ConfigureAwait(false);
            reply = ExcelAddinProtocol.TryDecodeRequest(line, out var request)
                ? await HandleAsync(request).ConfigureAwait(false)
                : new AddinReply(0, AddinStatus.BadRequest);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            reply = new AddinReply(0, AddinStatus.BadRequest, "timeout");
        }
        using var writeLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        writeLimit.CancelAfter(ReadTimeoutMs);
        try
        {
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(ExcelAddinProtocol.Encode(reply) + "\n"), writeLimit.Token).ConfigureAwait(false);
            await pipe.FlushAsync(writeLimit.Token).ConfigureAwait(false);
            pipe.WaitForPipeDrain();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { } // Slender gave up; nothing to do
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[ExcelAddinProtocol.MaxLineChars * 4 + 4];
        var used = 0;
        while (used < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (read == 0) return null;
            var nl = Array.IndexOf(buffer, (byte)'\n', used, read);
            used += read;
            if (nl >= 0) return Encoding.UTF8.GetString(buffer, 0, nl).TrimEnd('\r');
        }
        return null;
    }

    private async Task<AddinReply> HandleAsync(AddinRequest request)
    {
        if (request.Verb == AddinVerb.Ping) return new AddinReply(request.Id, AddinStatus.Ok, "pong"); // never touches Excel

        var done = new TaskCompletionSource<AddinReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = 0; // 0 queued, 1 started, 2 abandoned (Excel did not get to it in time)
        try
        {
            ExcelAsyncUtil.QueueAsMacro(() =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return; // the caller already gave up: do nothing
                if (request.Verb == AddinVerb.Macro)
                {
                    done.TrySetResult(new AddinReply(request.Id, AddinStatus.Ok)); // a macro can take long: answer when it starts
                    _actions.RunMacro(request.Arg!);
                    return;
                }
                done.TrySetResult(_actions.Run(request));
            });
        }
        catch
        {
            return new AddinReply(request.Id, AddinStatus.Failed);
        }

        var finished = await Task.WhenAny(done.Task, Task.Delay(MainThreadWaitMs)).ConfigureAwait(false);
        if (finished == done.Task) return await done.Task.ConfigureAwait(false);
        if (Interlocked.CompareExchange(ref state, 2, 0) == 0) return new AddinReply(request.Id, AddinStatus.Busy);
        return await Task.WhenAny(done.Task, Task.Delay(MainThreadWaitMs)).ConfigureAwait(false) == done.Task
            ? await done.Task.ConfigureAwait(false)
            : new AddinReply(request.Id, AddinStatus.Busy); // started but still running: report not-done so Slender does not double up
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
    }
}
