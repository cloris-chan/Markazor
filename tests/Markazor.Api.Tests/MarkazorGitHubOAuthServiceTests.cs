using System.Net;
using System.Text;
using Markazor.Api.Auth;

namespace Markazor.Api.Tests;

public sealed class MarkazorGitHubOAuthServiceTests
{
    [Fact]
    public async Task CallbackExchangesAuthorizationCodeAndProtectsRefreshToken()
    {
        using FakeGitHubHandler handler = new(
            """
            {"access_token":"access-1","expires_in":28800,"refresh_token":"refresh-1","refresh_token_expires_in":15552000}
            """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubOAuthService service = CreateService(httpClient);
        Uri callbackUri = new("https://example.com/setup/github-callback");
        MarkazorGitHubOAuthStartResult start = service.Start(callbackUri, FixedNow);
        string state = ParseQuery(start.AuthorizationUrl)["state"];
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubOAuthCallbackResult callback = await service.CompleteCallbackAsync(
            "code-1",
            state,
            start.StateCookieValue,
            callbackUri,
            FixedNow,
            cancellationToken);

        Assert.Equal("access-1", callback.AccessToken);
        Assert.NotNull(callback.AccessTokenExpiresAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(callback.RefreshCookieValue));
        Assert.Contains("code=code-1", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("code_verifier=", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("client_id=client-id", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-1", callback.RefreshCookieValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemporaryClientIdIsUsedForStartCallbackAndRefresh()
    {
        using FakeGitHubHandler callbackHandler = new(
            """
            {"access_token":"access-1","expires_in":28800,"refresh_token":"refresh-1","refresh_token_expires_in":15552000}
            """);
        using HttpClient callbackClient = new(callbackHandler);
        MarkazorGitHubOAuthService callbackService = CreateService(callbackClient, clientId: string.Empty);
        Uri callbackUri = new("https://public.example/setup/github-callback");

        MarkazorGitHubOAuthStartResult start = callbackService.Start(
            callbackUri,
            "temporary-client",
            FixedNow);
        Dictionary<string, string> startQuery = ParseQuery(start.AuthorizationUrl);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MarkazorGitHubOAuthCallbackResult callback = await callbackService.CompleteCallbackAsync(
            "code-1",
            startQuery["state"],
            start.StateCookieValue,
            new Uri("https://internal.example/setup/github-callback"),
            FixedNow,
            cancellationToken);

        Assert.Equal("temporary-client", startQuery["client_id"]);
        Assert.Equal(callbackUri.ToString(), startQuery["redirect_uri"]);
        Assert.Contains("client_id=temporary-client", callbackHandler.LastBody, StringComparison.Ordinal);
        Assert.Contains(
            "redirect_uri=https%3A%2F%2Fpublic.example%2Fsetup%2Fgithub-callback",
            callbackHandler.LastBody,
            StringComparison.Ordinal);

        using FakeGitHubHandler refreshHandler = new(
            """
            {"access_token":"access-2","expires_in":28800,"refresh_token":"refresh-2","refresh_token_expires_in":15552000}
            """);
        using HttpClient refreshClient = new(refreshHandler);
        MarkazorGitHubOAuthService refreshService = CreateService(refreshClient, clientId: string.Empty);

        MarkazorGitHubOAuthRefreshResult refresh = await refreshService.RefreshAsync(
            callback.RefreshCookieValue,
            FixedNow,
            cancellationToken);

        Assert.Equal("access-2", refresh.AccessToken);
        Assert.Contains("client_id=temporary-client", refreshHandler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshUsesRefreshTokenGrantAndRotatesCookieWhenGitHubReturnsOne()
    {
        using FakeGitHubHandler callbackHandler = new(
            """
            {"access_token":"access-1","expires_in":28800,"refresh_token":"refresh-1","refresh_token_expires_in":15552000}
            """);
        using HttpClient callbackClient = new(callbackHandler);
        MarkazorGitHubOAuthService callbackService = CreateService(callbackClient);
        MarkazorGitHubOAuthStartResult start = callbackService.Start(CallbackUri, FixedNow);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MarkazorGitHubOAuthCallbackResult callback = await callbackService.CompleteCallbackAsync(
            "code-1",
            ParseQuery(start.AuthorizationUrl)["state"],
            start.StateCookieValue,
            CallbackUri,
            FixedNow,
            cancellationToken);

        using FakeGitHubHandler refreshHandler = new(
            """
            {"access_token":"access-2","expires_in":28800,"refresh_token":"refresh-2","refresh_token_expires_in":15552000}
            """);
        using HttpClient refreshClient = new(refreshHandler);
        MarkazorGitHubOAuthService refreshService = CreateService(refreshClient);

        MarkazorGitHubOAuthRefreshResult refresh = await refreshService.RefreshAsync(
            callback.RefreshCookieValue,
            FixedNow,
            cancellationToken);

        Assert.Equal("access-2", refresh.AccessToken);
        Assert.NotNull(refresh.AccessTokenExpiresAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(refresh.RefreshCookieValue));
        Assert.Contains("grant_type=refresh_token", refreshHandler.LastBody, StringComparison.Ordinal);
        Assert.Contains("refresh_token=refresh-1", refreshHandler.LastBody, StringComparison.Ordinal);
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly Uri CallbackUri = new("https://example.com/setup/github-callback");

    private static MarkazorGitHubOAuthService CreateService(
        HttpClient httpClient,
        string clientId = "client-id")
    {
        return new MarkazorGitHubOAuthService(
            httpClient,
            new MarkazorGitHubOAuthOptions
            {
                ClientId = clientId,
                ClientSecret = "client-secret",
                CookieProtectionSecret = "cookie-secret",
                AuthorizationEndpoint = new Uri("https://github.test/login/oauth/authorize"),
                TokenEndpoint = new Uri("https://github.test/login/oauth/access_token"),
            });
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        Dictionary<string, string> query = new(StringComparer.Ordinal);
        string trimmedQuery = uri.Query.TrimStart('?');

        foreach (string pair in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            query[Uri.UnescapeDataString(parts[0])] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return query;
    }

    private sealed class FakeGitHubHandler(string responseJson) : HttpMessageHandler, IDisposable
    {
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
