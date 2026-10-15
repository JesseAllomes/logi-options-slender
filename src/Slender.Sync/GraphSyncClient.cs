using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Slender.Sync;

/// <summary>Microsoft Graph over HttpClient: the OneDrive app folder, /me/drive/special/approot:/slender/.</summary>
public sealed class GraphSyncClient : ISyncGraph
{
    public const string RemoteFolder = "slender";
    public const string RemoteFileName = "config.json";

    private const string Root = "https://graph.microsoft.com/v1.0/me/drive/special/approot:/" + RemoteFolder + "/";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _token;

    public GraphSyncClient(HttpClient http, Func<CancellationToken, Task<string>> tokenProvider)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _token = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async Task<RemoteFile?> GetConfigAsync(CancellationToken ct)
    {
        using HttpResponseMessage meta = await SendAsync(HttpMethod.Get, Root + RemoteFileName, null, null, ct).ConfigureAwait(false);
        if (meta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(meta, ct).ConfigureAwait(false);
        string eTag = await ReadETagAsync(meta, ct).ConfigureAwait(false);

        using HttpResponseMessage content = await SendAsync(HttpMethod.Get, Root + RemoteFileName + ":/content", null, null, ct).ConfigureAwait(false);
        if (content.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(content, ct).ConfigureAwait(false);
        byte[] bytes = await content.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return new RemoteFile(eTag, bytes);
    }

    public async Task<string> PutConfigAsync(byte[] content, string? ifMatchETag, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Put, Root + RemoteFileName + ":/content?@microsoft.graph.conflictBehavior=replace", content, ifMatchETag, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new SyncPreconditionFailedException();
        }

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await ReadETagAsync(response, ct).ConfigureAwait(false);
    }

    public async Task PutBackupAsync(string fileName, byte[] content, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Put, Root + Uri.EscapeDataString(fileName) + ":/content?@microsoft.graph.conflictBehavior=rename", content, null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, byte[]? body, string? ifMatch, CancellationToken ct)
    {
        string token = await _token(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new SyncOfflineException(e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new SyncOfflineException(e); // HttpClient timeout
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SyncSignInRequiredException("Your Microsoft sign-in expired. Sign in again.");
        }

        string detail = string.Empty;
        try
        {
            detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(detail);
            if (doc.RootElement.TryGetProperty("error", out JsonElement err) && err.TryGetProperty("message", out JsonElement msg))
            {
                detail = msg.GetString() ?? detail;
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException($"OneDrive returned {(int)response.StatusCode}. {detail}".Trim());
    }

    private static async Task<string> ReadETagAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("eTag", out JsonElement e) && e.GetString() is { Length: > 0 } tag)
        {
            return tag;
        }

        throw new InvalidOperationException("OneDrive did not return an eTag.");
    }
}
