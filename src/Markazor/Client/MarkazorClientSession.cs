using System.Net.Http.Json;
using System.Text.Json;
using Markazor.Core.Auth;
using Markazor.Core.Configuration;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;
using Markazor.Core.Serialization;

namespace Markazor.Client;

public sealed class MarkazorClientSession(HttpClient httpClient) : IMarkazorClientSession, IDisposable
{
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(5);
    private static readonly Uri SetupStatusUri = new("/api/setup/status", UriKind.Relative);
    private static readonly Uri AuthStartUri = new("/api/auth/github/start", UriKind.Relative);
    private static readonly Uri AuthCallbackUri = new("/api/auth/github/callback", UriKind.Relative);
    private static readonly Uri AuthRefreshUri = new("/api/auth/github/refresh", UriKind.Relative);
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public MarkazorSetupStatus? SetupStatus { get; private set; }

    public string? AccessToken { get; private set; }

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; private set; }

    public bool IsReady => MarkazorSetupReadiness.HasRepositoryConfiguration(SetupStatus);

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    public async Task<MarkazorSetupStatus?> LoadSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                SetupStatusUri,
                cancellationToken).ConfigureAwait(false);
            _ = response.EnsureSuccessStatusCode();

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            SetupStatus = await JsonSerializer.DeserializeAsync(
                stream,
                MarkazorCoreJsonSerializerContext.Default.MarkazorSetupStatus,
                cancellationToken).ConfigureAwait(false);

            return SetupStatus
                ?? throw new HttpRequestException("The setup status endpoint returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException(
                "The setup status endpoint did not return valid JSON. Ensure the Static Web Apps Functions API is deployed and /api/setup/status is available.",
                ex);
        }
    }

    public async Task<MarkazorGitHubAuthorizationResponse> StartGitHubAuthorizationAsync(
        MarkazorGitHubAuthorizationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            AuthStartUri,
            request ?? new MarkazorGitHubAuthorizationRequest(null, null),
            MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAuthorizationRequest,
            cancellationToken).ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        MarkazorGitHubAuthorizationResponse? authorization = await JsonSerializer.DeserializeAsync(
            stream,
            MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAuthorizationResponse,
            cancellationToken).ConfigureAwait(false);

        return authorization ?? throw new InvalidOperationException("The authorization endpoint returned an empty response.");
    }

    public async Task<bool> CompleteGitHubAuthorizationAsync(
        MarkazorGitHubCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            AuthCallbackUri,
            request,
            MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubCallbackRequest,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ClearAccessToken();

            return false;
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        MarkazorGitHubAccessTokenResponse? token = await JsonSerializer.DeserializeAsync(
            stream,
            MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAccessTokenResponse,
            cancellationToken).ConfigureAwait(false);

        AccessToken = token?.AccessToken;
        AccessTokenExpiresAtUtc = token?.AccessTokenExpiresAtUtc;

        return IsAuthenticated;
    }

    public Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default) => RefreshAccessTokenAsync(null, cancellationToken);

    private async Task<bool> RefreshAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (rejectedToken is not null && string.Equals(AccessToken, rejectedToken, StringComparison.Ordinal))
            {
                ClearAccessToken();
            }

            if (HasCurrentAccessToken())
            {
                return true;
            }

            using HttpResponseMessage response = await httpClient.PostAsync(
                AuthRefreshUri,
                null,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ClearAccessToken();

                return false;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            MarkazorGitHubAccessTokenResponse? token = await JsonSerializer.DeserializeAsync(
                stream,
                MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAccessTokenResponse,
                cancellationToken).ConfigureAwait(false);

            AccessToken = token?.AccessToken;
            AccessTokenExpiresAtUtc = token?.AccessTokenExpiresAtUtc;

            return IsAuthenticated;
        }
        finally
        {
            _ = refreshLock.Release();
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HasCurrentAccessToken())
        {
            return AccessToken;
        }

        return await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false)
            ? AccessToken
            : null;
    }

    public async Task<MarkazorGitHubClientResult<T>> ExecuteGitHubAsync<T>(
        Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
        MarkazorRepositorySettings? repository = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        string? accessToken = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new MarkazorGitHubClientResult<T>(
                MarkazorGitHubClientResultKind.Unauthorized,
                default,
                System.Net.HttpStatusCode.Unauthorized,
                "GitHub authorization is required.");
        }

        MarkazorGitHubRepositoryClient client = CreateRepositoryClient(repository);
        MarkazorGitHubClientResult<T> result = await operation(
            client,
            accessToken,
            cancellationToken).ConfigureAwait(false);

        if (result.Kind != MarkazorGitHubClientResultKind.Unauthorized)
        {
            return result;
        }

        accessToken = await RefreshAccessTokenAsync(accessToken, cancellationToken).ConfigureAwait(false) ? AccessToken : null;

        return string.IsNullOrWhiteSpace(accessToken)
            ? result
            : await operation(client, accessToken, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        refreshLock.Dispose();
    }

    private MarkazorGitHubRepositoryClient CreateRepositoryClient(MarkazorRepositorySettings? repository)
    {
        MarkazorRepositoryStatus? configured = SetupStatus?.Repository;
        MarkazorApiOptions options = new()
        {
            RepositoryOwner = repository?.Owner ?? configured?.Owner ?? throw new InvalidOperationException("Setup status has not been loaded."),
            RepositoryName = repository?.Name ?? configured!.Name,
            DefaultBranch = repository?.DefaultBranch ?? configured!.DefaultBranch,
        };
        return new MarkazorGitHubRepositoryClient(httpClient, options);
    }

    private bool HasCurrentAccessToken()
    {
        return IsAuthenticated
            && AccessTokenExpiresAtUtc is not null
            && AccessTokenExpiresAtUtc.Value > DateTimeOffset.UtcNow.Add(RefreshBeforeExpiry);
    }

    private void ClearAccessToken()
    {
        AccessToken = null;
        AccessTokenExpiresAtUtc = null;
    }
}
