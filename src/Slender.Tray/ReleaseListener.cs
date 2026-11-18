using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Slender.Tray;

/// <summary>
/// Hears "a release is out" the moment scripts\Build-Setup.ps1 publishes one, through a free Azure Web PubSub. It reads its
/// listen-only ticket from notify.json (an asset on the latest GitHub release), keeps one socket open, and reconnects on its
/// own after sleep, network loss or a service restart (5 s doubling to 5 min). The 15-minute update check stays as the
/// fallback, so nothing here can stop an update; failures are only logged, and the ticket (it carries a token) never is.
/// </summary>
public sealed class ReleaseListener : IDisposable
{
    private static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);
    private const int MaxMessage = 64 * 1024;

    private readonly Uri _ticket;
    private readonly string _app;
    private readonly Action<string> _released;
    private readonly Action _connected;
    private readonly Action<string> _log;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="ticket">notify.json on the latest release (GitHub redirects; HttpClient follows).</param>
    /// <param name="app">This app's name in notices (ReleaseNotice.ps1's $NoticeApp): "slender".</param>
    /// <param name="released">A release notice arrived, with its version (called on a background thread).</param>
    /// <param name="connected">The socket (re)opened: a notice may have been missed while it was down.</param>
    public ReleaseListener(Uri ticket, string app, Action<string> released, Action connected, Action<string> log, HttpClient? http = null)
    {
        _ticket = ticket;
        _app = app;
        _released = released;
        _connected = connected;
        _log = log;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public void Start() => _ = Task.Run(() => RunAsync(_stop.Token));

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var attempt = 0;
        while (!stop.IsCancellationRequested)
        {
            var began = DateTime.UtcNow;
            try
            {
                var url = await ReadTicketAsync(stop).ConfigureAwait(false);
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                // Ping/pong: a socket that went dead in sleep or on a network change is noticed within a minute.
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    limit.CancelAfter(TimeSpan.FromSeconds(30));
                    await socket.ConnectAsync(url, limit.Token).ConfigureAwait(false);
                }

                _log("release notices connected");
                attempt = 0;
                Safe(_connected);
                await ReceiveAsync(socket, stop).ConfigureAwait(false);
                _log("release notices closed");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log($"release notices: {ex.GetType().Name}");
            }

            if (DateTime.UtcNow - began > TimeSpan.FromMinutes(2)) attempt = 0;
            try { await Task.Delay(Backoff(attempt++), stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<Uri> ReadTicketAsync(CancellationToken stop)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _ticket);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await _http.SendAsync(request, stop).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ParseTicket(await response.Content.ReadAsStringAsync(stop).ConfigureAwait(false), DateTimeOffset.UtcNow)
            ?? throw new InvalidDataException("release notice ticket unusable or expired");
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken stop)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var part = await socket.ReceiveAsync(buffer, stop).ConfigureAwait(false);
            if (part.MessageType == WebSocketMessageType.Close) return;
            if (message.Length + part.Count > MaxMessage) throw new InvalidDataException("release notice too large");
            message.Write(buffer, 0, part.Count);
            if (!part.EndOfMessage) continue;
            var text = part.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length) : "";
            message.SetLength(0);
            if (ParseRelease(text, _app) is { } version)
            {
                _log($"release notice: {version}");
                Safe(() => _released(version));
            }
        }
    }

    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log($"release notice handler failed: {ex.GetType().Name}"); }
    }

    /// <summary>5 s, doubling to 5 min, with up to 20% jitter so copies don't all reconnect at once.</summary>
    public static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(FirstWait.TotalSeconds * Math.Pow(2, Math.Clamp(attempt, 0, 16)), MaxWait.TotalSeconds);
        return TimeSpan.FromSeconds(seconds * (1 + Random.Shared.NextDouble() * 0.2));
    }

    /// <summary>The socket address from notify.json: wss on a Web PubSub host, and not expired.</summary>
    public static Uri? ParseTicket(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("expires", out var expires) && expires.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expires.GetString(), out var until) && until <= now) return null;
            return root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "wss"
                && uri.Host.EndsWith(".webpubsub.azure.com", StringComparison.OrdinalIgnoreCase) ? uri : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The version in a release notice for <paramref name="app"/>, or null for anything else.</summary>
    public static string? ParseRelease(string text, string app)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "release") return null;
            if (root.TryGetProperty("app", out var named) && (named.ValueKind != JsonValueKind.String || named.GetString() != app)) return null;
            if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return null;
            var version = v.GetString()!;
            return version.Length is > 0 and <= 32 && version.All(c => char.IsAsciiDigit(c) || c is '.' or '-' || char.IsAsciiLetter(c))
                && char.IsAsciiDigit(version[0]) ? version : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
