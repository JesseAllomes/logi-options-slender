using System.IO.Pipes;
using System.Text;

namespace Slender.Engine;

/// <summary>Settings' side: asks the tray to read or write the keyboard's own remaps. Settings never opens a HID device.</summary>
public sealed class OnboardClient
{
    private readonly Func<string, CancellationToken, Task<string?>> _exchange;

    /// <param name="exchange">Sends one request line, returns the reply line, or null when the tray cannot be reached.</param>
    public OnboardClient(Func<string, CancellationToken, Task<string?>> exchange) => _exchange = exchange;

    public static OnboardClient ForPipe(string pipeName = OnboardProtocol.PipeName) => new((line, ct) => PipeExchangeAsync(pipeName, line, ct));

    public async Task<OnboardReadReply> ReadAsync(CancellationToken ct = default) =>
        OnboardProtocol.ParseRead(await _exchange(OnboardProtocol.ReadLine(), ct).ConfigureAwait(false));

    public async Task<OnboardApplyReply> ApplyAsync(IEnumerable<OnboardRemap> sets, IEnumerable<ushort> resets, CancellationToken ct = default) =>
        OnboardProtocol.ParseApply(await _exchange(OnboardProtocol.ApplyLine(sets, resets), ct).ConfigureAwait(false));

    private static async Task<string?> PipeExchangeAsync(string name, string line, CancellationToken ct)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(1500, ct).ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }
    }
}
