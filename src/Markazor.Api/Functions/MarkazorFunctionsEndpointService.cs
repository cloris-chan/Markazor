using System.Net;
using System.Text.Json;
using Markazor.Api.Auth;
using Markazor.Api.Setup;
using Markazor.Core.Auth;
using Markazor.Core.Serialization;
using Markazor.Core.Setup;
using Microsoft.Azure.Functions.Worker.Http;

namespace Markazor.Api.Functions;

public sealed class MarkazorFunctionsEndpointService(HttpClient httpClient, MarkazorFunctionsOptions options)
{
    private const string AuthCookiePath = "/api/auth/github";

    public HttpResponseData StartGitHubAuth(HttpRequestData request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            MarkazorGitHubAuthorizationRequest authorizationRequest = ReadAuthorizationRequest(request);
            MarkazorSiteSettings settings = LoadSettings();
            MarkazorGitHubOAuthOptions oauthOptions = CreateOAuthOptions(settings);
            MarkazorGitHubOAuthService service = new(httpClient, oauthOptions);
            MarkazorGitHubOAuthStartResult result = service.Start(CreateCallbackUri(request, settings, authorizationRequest.SiteBaseUrl), authorizationRequest.ClientId);

            HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            AddNoStore(response);
            response.Cookies.Append(CreateCookie(oauthOptions.StateCookieName, result.StateCookieValue, oauthOptions.StateLifetime));
            response.WriteString(JsonSerializer.Serialize(new MarkazorGitHubAuthorizationResponse(result.AuthorizationUrl, result.StateExpiresAtUtc), MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAuthorizationResponse));

            return response;
        }
        catch (InvalidOperationException ex)
        {
            return CreateErrorResponse(request, HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    public async Task<HttpResponseData> CompleteGitHubAuthAsync(
        HttpRequestData request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            MarkazorGitHubCallbackRequest callbackRequest = ReadCallbackRequest(request);

            if (string.IsNullOrWhiteSpace(callbackRequest.Code) || string.IsNullOrWhiteSpace(callbackRequest.State))
            {
                return CreateErrorResponse(request, HttpStatusCode.BadRequest, "GitHub callback is missing code or state.");
            }

            MarkazorSiteSettings settings = LoadSettings();
            MarkazorGitHubOAuthOptions oauthOptions = CreateOAuthOptions(settings);
            MarkazorGitHubOAuthService service = new(httpClient, oauthOptions);
            MarkazorGitHubOAuthCallbackResult result = await service.CompleteCallbackAsync(callbackRequest.Code, callbackRequest.State, ReadCookie(request, oauthOptions.StateCookieName), CreateCallbackFallbackUri(settings), cancellationToken: cancellationToken).ConfigureAwait(false);

            HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            AddNoStore(response);
            response.Cookies.Append(DeleteCookie(oauthOptions.StateCookieName));
            response.Cookies.Append(CreateCookie(oauthOptions.RefreshCookieName, result.RefreshCookieValue, GetCookieLifetime(result.RefreshTokenExpiresAtUtc)));
            await response.WriteStringAsync(JsonSerializer.Serialize(new MarkazorGitHubAccessTokenResponse(result.AccessToken, result.AccessTokenExpiresAtUtc), MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAccessTokenResponse)).ConfigureAwait(false);

            return response;
        }
        catch (InvalidOperationException ex)
        {
            return CreateErrorResponse(request, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (FormatException ex)
        {
            return CreateErrorResponse(request, HttpStatusCode.BadRequest, ex.Message);
        }
    }

    public async Task<HttpResponseData> RefreshGitHubAuthAsync(HttpRequestData request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            MarkazorGitHubOAuthOptions oauthOptions = CreateOAuthOptions(LoadSettings());
            MarkazorGitHubOAuthService service = new(httpClient, oauthOptions);
            MarkazorGitHubOAuthRefreshResult result = await service.RefreshAsync(ReadCookie(request, oauthOptions.RefreshCookieName), cancellationToken: cancellationToken).ConfigureAwait(false);

            HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            AddNoStore(response);

            if (result.RefreshCookieValue is not null)
            {
                response.Cookies.Append(CreateCookie(oauthOptions.RefreshCookieName, result.RefreshCookieValue, GetCookieLifetime(result.RefreshTokenExpiresAtUtc)));
            }

            await response.WriteStringAsync(JsonSerializer.Serialize(new MarkazorGitHubAccessTokenResponse(result.AccessToken, result.AccessTokenExpiresAtUtc), MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAccessTokenResponse)).ConfigureAwait(false);

            return response;
        }
        catch (InvalidOperationException ex)
        {
            return CreateErrorResponse(request, HttpStatusCode.Unauthorized, ex.Message);
        }
    }

    public HttpResponseData GetSetupStatus(HttpRequestData request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MarkazorSetupStatusService setupStatusService = new(
            options: new MarkazorSetupStatusOptions
            {
                SiteSettings = LoadSettings(),
                SiteSettingsFilePath = options.SiteSettingsFilePath,
                ExpectedStaticWebAppsBuildSettings = options.ExpectedStaticWebAppsBuildSettings,
            });

        HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        AddNoStore(response);
        response.WriteString(JsonSerializer.Serialize(setupStatusService.GetStatus(), MarkazorCoreJsonSerializerContext.Default.MarkazorSetupStatus));

        return response;
    }

    private MarkazorSiteSettings LoadSettings()
    {
        return MarkazorSiteSettingsLoader.Load(options.SiteSettingsFilePath);
    }

    private static MarkazorGitHubOAuthOptions CreateOAuthOptions(MarkazorSiteSettings settings)
    {
        return MarkazorGitHubOAuthOptions.FromEnvironment(settings: settings);
    }

    private static MarkazorGitHubAuthorizationRequest ReadAuthorizationRequest(HttpRequestData request)
    {
        try
        {
            MarkazorGitHubAuthorizationRequest? authorizationRequest = JsonSerializer.Deserialize(request.Body, MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubAuthorizationRequest);

            return authorizationRequest ?? new MarkazorGitHubAuthorizationRequest(null, null);
        }
        catch (JsonException)
        {
            return new MarkazorGitHubAuthorizationRequest(null, null);
        }
    }

    private static MarkazorGitHubCallbackRequest ReadCallbackRequest(HttpRequestData request)
    {
        try
        {
            MarkazorGitHubCallbackRequest? callbackRequest = JsonSerializer.Deserialize(request.Body, MarkazorCoreJsonSerializerContext.Default.MarkazorGitHubCallbackRequest);

            return callbackRequest ?? new MarkazorGitHubCallbackRequest(null, null);
        }
        catch (JsonException)
        {
            return new MarkazorGitHubCallbackRequest(null, null);
        }
    }

    private static Uri CreateCallbackUri(HttpRequestData request, MarkazorSiteSettings settings, Uri? siteBaseUrl)
    {
        Uri publicBaseUri = GetPublicBaseUri(request, settings, siteBaseUrl);
        UriBuilder builder = new(publicBaseUri)
        {
            Path = "/setup/github-callback",
            Query = string.Empty,
        };

        return builder.Uri;
    }

    private static Uri CreateCallbackFallbackUri(MarkazorSiteSettings settings)
    {
        Uri baseUri = settings.Site.PrimaryBaseUrl ?? new Uri("https://localhost/");

        return new Uri(baseUri, "/setup/github-callback");
    }

    private static Uri GetPublicBaseUri(HttpRequestData request, MarkazorSiteSettings settings, Uri? siteBaseUrl)
    {
        if (siteBaseUrl is not null && siteBaseUrl.IsAbsoluteUri)
        {
            return siteBaseUrl;
        }

        Uri? configuredBaseUri = settings.Site.PrimaryBaseUrl;
        if (configuredBaseUri is not null)
        {
            return configuredBaseUri;
        }

        string scheme = ReadForwardedHeader(request, "X-Forwarded-Proto") ?? request.Url.Scheme;
        string host = ReadForwardedHeader(request, "X-Forwarded-Host") ?? request.Url.Authority;

        return new UriBuilder(scheme, host).Uri;
    }

    private static string? ReadForwardedHeader(HttpRequestData request, string name)
    {
        if (!request.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return null;
        }

        string? value = values
            .SelectMany(static item => item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item));

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static TimeSpan GetCookieLifetime(DateTimeOffset? expiresAtUtc)
    {
        return expiresAtUtc is null ? TimeSpan.FromDays(180) : expiresAtUtc.Value - DateTimeOffset.UtcNow;
    }

    private static HttpCookie CreateCookie(string name, string value, TimeSpan maxAge)
    {
        return new HttpCookie(name, value)
        {
            HttpOnly = true,
            MaxAge = Math.Max(0D, maxAge.TotalSeconds),
            Path = AuthCookiePath,
            SameSite = SameSite.Lax,
            Secure = true,
        };
    }

    private static HttpCookie DeleteCookie(string name)
    {
        return CreateCookie(name, string.Empty, TimeSpan.Zero);
    }

    private static string? ReadCookie(HttpRequestData request, string name)
    {
        if (!request.Headers.TryGetValues("Cookie", out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string cookieHeader in values)
        {
            string[] cookies = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string cookie in cookies)
            {
                int separatorIndex = cookie.IndexOf('=', StringComparison.Ordinal);

                if (separatorIndex <= 0)
                {
                    continue;
                }

                if (string.Equals(cookie[..separatorIndex], name, StringComparison.Ordinal))
                {
                    return cookie[(separatorIndex + 1)..];
                }
            }
        }

        return null;
    }

    private static HttpResponseData CreateErrorResponse(HttpRequestData request, HttpStatusCode statusCode, string message)
    {
        HttpResponseData response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        AddNoStore(response);
        response.WriteString(message);

        return response;
    }

    private static void AddNoStore(HttpResponseData response)
    {
        response.Headers.Add("Cache-Control", "no-store");
    }
}
