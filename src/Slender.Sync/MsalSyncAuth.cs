using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Slender.Sync;

/// <summary>
/// MSAL public client for PERSONAL Microsoft accounts only (the /consumers authority). Interactive sign-in uses the system
/// browser with a http://localhost redirect. The token cache is DPAPI-encrypted (MSAL extensions) under the state folder.
/// </summary>
public sealed class MsalSyncAuth : ISyncAuth
{
    public const string Authority = "https://login.microsoftonline.com/consumers";
    public const string RedirectUri = "http://localhost";
    public const string CacheFileName = "msal-token-cache.bin";

    private static readonly string[] Scopes = ["Files.ReadWrite.AppFolder"];

    private readonly IPublicClientApplication _app;
    private readonly string _stateDirectory;
    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _cacheReady;

    public MsalSyncAuth(string clientId, string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _stateDirectory = stateDirectory;
        _app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(Authority)
            .WithRedirectUri(RedirectUri)
            .WithClientName("Slender")
            .Build();
    }

    public async Task<SyncAccount?> GetAccountAsync(CancellationToken ct)
    {
        await EnsureCacheAsync().ConfigureAwait(false);
        IAccount? account = (await _app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        return account is null ? null : ToAccount(account);
    }

    public async Task<SyncAccount> SignInAsync(CancellationToken ct)
    {
        await EnsureCacheAsync().ConfigureAwait(false);
        try
        {
            AuthenticationResult result = await _app.AcquireTokenInteractive(Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = "<html><body style=\"font-family:Segoe UI,sans-serif\"><h3>Signed in to Slender</h3><p>You can close this tab and return to Slender.</p></body></html>",
                })
                .ExecuteAsync(ct)
                .ConfigureAwait(false);
            return ToAccount(result.Account);
        }
        catch (Exception e) when (IsOffline(e))
        {
            throw new SyncOfflineException(e);
        }
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        await EnsureCacheAsync().ConfigureAwait(false);
        foreach (IAccount account in await _app.GetAccountsAsync().ConfigureAwait(false))
        {
            await _app.RemoveAsync(account).ConfigureAwait(false);
        }
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await EnsureCacheAsync().ConfigureAwait(false);
        IAccount? account = (await _app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault()
            ?? throw new SyncSignInRequiredException("Not signed in.");
        try
        {
            AuthenticationResult result = await _app.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct).ConfigureAwait(false);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException e)
        {
            throw new SyncSignInRequiredException("Your Microsoft sign-in expired. Sign in again.", e);
        }
        catch (Exception e) when (IsOffline(e))
        {
            throw new SyncOfflineException(e);
        }
    }

    private static SyncAccount ToAccount(IAccount account) => new(account.HomeAccountId.Identifier, account.Username);

    private static bool IsOffline(Exception e) =>
        e is HttpRequestException
        || e.InnerException is HttpRequestException
        || e is MsalServiceException { ErrorCode: MsalError.ServiceNotAvailable or MsalError.RequestTimeout }
        || e is MsalClientException { ErrorCode: MsalError.HttpStatusNotFound or "http_request_failed" };

    private async Task EnsureCacheAsync()
    {
        if (_cacheReady)
        {
            return;
        }

        await _init.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cacheReady)
            {
                return;
            }

            Directory.CreateDirectory(_stateDirectory);
            StorageCreationProperties props = new StorageCreationPropertiesBuilder(CacheFileName, _stateDirectory).Build();
            MsalCacheHelper helper = await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false);
            helper.RegisterCache(_app.UserTokenCache);
            _cacheReady = true;
        }
        finally
        {
            _init.Release();
        }
    }
}
