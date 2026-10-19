using System.IO.Pipes;
using Slender.Engine;

namespace Slender.Settings.Logic;

/// <summary>
/// Connects to the tray's thumb wheel preview pipe while the thumb wheel card is open. While connected the tray runs nothing for
/// the thumb wheel and the top button and sends them here instead. Dispose (the card closed) ends preview mode in the tray at once.
/// Events are raised through <c>post</c>, normally the UI dispatcher.
/// </summary>
public sealed class ThumbPreviewClient : IDisposable
{
    private readonly Action<Action> _post;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeClientStream? _pipe;

    public ThumbPreviewClient(Action<Action> post) => _post = post;

    /// <summary>The tray answered (true) or the link ended (false).</summary>
    public event Action<bool>? ConnectedChanged;

    /// <summary>A thumb wheel turn, 1/120 notch units, positive = right.</summary>
    public event Action<int>? Rotated;

    /// <summary>The top button went down (true) or up.</summary>
    public event Action<bool>? TopButton;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        bool connected = false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", ThumbPreview.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _pipe = pipe;
            await pipe.ConnectAsync(1500, ct).ConfigureAwait(false);
            connected = true;
            _post(() => ConnectedChanged?.Invoke(true));
            using var reader = new StreamReader(pipe, leaveOpen: true);
            while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (!ThumbPreview.TryParse(line, out char kind, out int value))
                {
                    continue;
                }

                if (kind == 'r')
                {
                    _post(() => Rotated?.Invoke(value));
                }
                else
                {
                    _post(() => TopButton?.Invoke(value == 1));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { } // the tray is not running: no live preview
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            if (connected && !ct.IsCancellationRequested)
            {
                _post(() => ConnectedChanged?.Invoke(false));
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
        _cts.Dispose();
    }
}
