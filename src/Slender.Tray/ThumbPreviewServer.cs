using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// Pipe server for the thumb wheel card's live preview. Settings connects while the card is open; while it is connected the
/// engine is in preview mode (thumb wheel turns and the top button are sent here as lines and nothing runs). The client never
/// writes, so a read returning 0 means the card closed (or Settings exited) and preview mode ends at once. One client at a time,
/// current user only. Everything is off the hook and HID threads: the sink only queues.
/// </summary>
public sealed class ThumbPreviewServer : IDisposable
{
    private readonly SlenderEngine _engine;
    private readonly ILog _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public ThumbPreviewServer(SlenderEngine engine, ILog? log = null)
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
                using var pipe = new NamedPipeServerStream(ThumbPreview.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await ServeAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.Error("thumb wheel preview pipe failed", ex);
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var lines = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _engine.SetPreview(new Sink(lines.Writer));
        _log.Info("thumb wheel card preview started");
        try
        {
            var writer = WriteAsync(pipe, lines.Reader, session.Token);
            var buffer = new byte[16];
            while (await pipe.ReadAsync(buffer, session.Token).ConfigureAwait(false) > 0) { } // the client never writes; 0 = it closed
            session.Cancel();
            try { await writer.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        catch (IOException) { } // the card closed mid-write
        finally
        {
            _engine.SetPreview(null);
            lines.Writer.TryComplete();
            _log.Info("thumb wheel card preview ended");
        }
    }

    private static async Task WriteAsync(Stream pipe, ChannelReader<string> reader, CancellationToken ct)
    {
        await foreach (var line in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            await pipe.WriteAsync(bytes, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed class Sink(ChannelWriter<string> writer) : IThumbPreviewSink
    {
        public void Rotated(int delta) => writer.TryWrite(ThumbPreview.Rotation(delta));
        public void TopButton(bool down) => writer.TryWrite(ThumbPreview.Top(down));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _engine.SetPreview(null);
        _cts.Dispose();
    }
}
