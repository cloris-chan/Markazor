using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Markazor.Api.Serialization;

namespace Markazor.Api.Auth;

public sealed class MarkazorGitHubOAuthService(HttpClient httpClient, MarkazorGitHubOAuthOptions options)
{
    private readonly MarkazorCookieProtector cookieProtector = new(GetCookieProtectionSecret(options));

    public MarkazorGitHubOAuthStartResult Start(Uri callbackUri, DateTimeOffset? now = null)
    {
        return Start(callbackUri, null, now);
    }

    public MarkazorGitHubOAuthStartResult Start(Uri callbackUri, string? clientId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);
        EnsureSecretConfigured();

        DateTimeOffset currentTime = now ?? DateTimeOffset.UtcNow;
        string effectiveClientId = ResolveClientId(clientId);
        string state = Base64Url.RandomToken();
        string codeVerifier = Base64Url.RandomToken();
        string codeChallenge = CreateCodeChallenge(codeVerifier);
        DateTimeOffset expiresAtUtc = currentTime.Add(options.StateLifetime);
        MarkazorOAuthState oauthState = new(state, codeVerifier, expiresAtUtc, effectiveClientId, callbackUri);
        string stateCookieValue = cookieProtector.Protect(oauthState, MarkazorApiJsonSerializerContext.Default.MarkazorOAuthState);

        return new MarkazorGitHubOAuthStartResult(BuildAuthorizationUrl(callbackUri, state, codeChallenge, effectiveClientId), stateCookieValue, expiresAtUtc);
    }

    public async Task<MarkazorGitHubOAuthCallbackResult> CompleteCallbackAsync(string code, string state, string? stateCookieValue, Uri callbackUri, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(callbackUri);
        EnsureSecretConfigured();

        DateTimeOffset currentTime = now ?? DateTimeOffset.UtcNow;
        MarkazorOAuthState oauthState = cookieProtector.Unprotect(stateCookieValue, MarkazorApiJsonSerializerContext.Default.MarkazorOAuthState) ?? throw new InvalidOperationException("OAuth state cookie is missing or invalid.");

        if (oauthState.ExpiresAtUtc <= currentTime)
        {
            throw new InvalidOperationException("OAuth state has expired.");
        }

        if (!string.Equals(oauthState.State, state, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OAuth state does not match.");
        }

        string effectiveClientId = ResolveClientId(oauthState.ClientId);
        Uri effectiveCallbackUri = oauthState.CallbackUri ?? callbackUri;

        GitHubOAuthTokenResponse tokenResponse = await ExchangeTokenAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = effectiveClientId,
                ["client_secret"] = options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = effectiveCallbackUri.ToString(),
                ["code_verifier"] = oauthState.CodeVerifier,
            },
            cancellationToken).ConfigureAwait(false);

        MarkazorRefreshTokenCookie refreshCookie = CreateRefreshCookie(tokenResponse, currentTime, effectiveClientId);
        DateTimeOffset? accessTokenExpiresAtUtc = tokenResponse.ExpiresInSeconds is null ? null : currentTime.AddSeconds(tokenResponse.ExpiresInSeconds.Value);

        return new MarkazorGitHubOAuthCallbackResult(
            tokenResponse.AccessToken ?? throw new InvalidOperationException("GitHub did not return an access token."),
            accessTokenExpiresAtUtc,
            cookieProtector.Protect(refreshCookie, MarkazorApiJsonSerializerContext.Default.MarkazorRefreshTokenCookie), refreshCookie.ExpiresAtUtc);
    }

    public async Task<MarkazorGitHubOAuthRefreshResult> RefreshAsync(string? refreshCookieValue, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        EnsureSecretConfigured();

        DateTimeOffset currentTime = now ?? DateTimeOffset.UtcNow;
        MarkazorRefreshTokenCookie refreshCookie = cookieProtector.Unprotect(refreshCookieValue, MarkazorApiJsonSerializerContext.Default.MarkazorRefreshTokenCookie) ?? throw new InvalidOperationException("Refresh token cookie is missing or invalid.");

        if (refreshCookie.ExpiresAtUtc is not null && refreshCookie.ExpiresAtUtc <= currentTime)
        {
            throw new InvalidOperationException("Refresh token has expired.");
        }

        string effectiveClientId = ResolveClientId(refreshCookie.ClientId);

        GitHubOAuthTokenResponse tokenResponse = await ExchangeTokenAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = effectiveClientId,
                ["client_secret"] = options.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshCookie.RefreshToken,
            },
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset? accessTokenExpiresAtUtc = tokenResponse.ExpiresInSeconds is null ? null : currentTime.AddSeconds(tokenResponse.ExpiresInSeconds.Value);

        string? rotatedCookieValue = null;
        DateTimeOffset? rotatedRefreshTokenExpiresAtUtc = null;

        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            MarkazorRefreshTokenCookie rotatedRefreshCookie = CreateRefreshCookie(tokenResponse, currentTime, effectiveClientId);
            rotatedCookieValue = cookieProtector.Protect(rotatedRefreshCookie, MarkazorApiJsonSerializerContext.Default.MarkazorRefreshTokenCookie);
            rotatedRefreshTokenExpiresAtUtc = rotatedRefreshCookie.ExpiresAtUtc;
        }

        return new MarkazorGitHubOAuthRefreshResult(tokenResponse.AccessToken ?? throw new InvalidOperationException("GitHub did not return an access token."), accessTokenExpiresAtUtc, rotatedCookieValue, rotatedRefreshTokenExpiresAtUtc);
    }

    private static string GetCookieProtectionSecret(MarkazorGitHubOAuthOptions options)
    {
        return string.IsNullOrWhiteSpace(options.CookieProtectionSecret) ? options.ClientSecret : options.CookieProtectionSecret;
    }

    private static MarkazorRefreshTokenCookie CreateRefreshCookie(GitHubOAuthTokenResponse tokenResponse, DateTimeOffset currentTime, string clientId)
    {
        if (!string.IsNullOrWhiteSpace(tokenResponse.Error))
        {
            throw new InvalidOperationException(tokenResponse.ErrorDescription ?? tokenResponse.Error);
        }

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("GitHub did not return an access token.");
        }

        if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            throw new InvalidOperationException("GitHub did not return a refresh token.");
        }

        DateTimeOffset? refreshTokenExpiresAtUtc = tokenResponse.RefreshTokenExpiresInSeconds is null ? null : currentTime.AddSeconds(tokenResponse.RefreshTokenExpiresInSeconds.Value);

        return new MarkazorRefreshTokenCookie(tokenResponse.RefreshToken, refreshTokenExpiresAtUtc, clientId);
    }

    private async Task<GitHubOAuthTokenResponse> ExchangeTokenAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        GitHubOAuthTokenResponse? tokenResponse = await JsonSerializer.DeserializeAsync(responseStream, MarkazorApiJsonSerializerContext.Default.GitHubOAuthTokenResponse, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("GitHub returned an empty token response.");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(tokenResponse.ErrorDescription ?? response.ReasonPhrase ?? "GitHub token exchange failed.");
        }

        return tokenResponse;
    }

    private Uri BuildAuthorizationUrl(Uri callbackUri, string state, string codeChallenge, string clientId)
    {
        Dictionary<string, string> query = new(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = callbackUri.ToString(),
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        string queryString = string.Join("&", query.Select(static pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

        UriBuilder builder = new(options.AuthorizationEndpoint)
        {
            Query = queryString,
        };

        return builder.Uri;
    }

    private void EnsureSecretConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException("GITHUB_APP_CLIENT_SECRET is not configured.");
        }
    }

    private string ResolveClientId(string? clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            return clientId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ClientId))
        {
            return options.ClientId.Trim();
        }

        throw new InvalidOperationException("GitHub App Client ID is not configured.");
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));

        return Base64Url.Encode(hash);
    }
}
