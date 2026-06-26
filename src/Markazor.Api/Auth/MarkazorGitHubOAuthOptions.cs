using Markazor.Core.Setup;

namespace Markazor.Api.Auth;

public sealed class MarkazorGitHubOAuthOptions
{
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string CookieProtectionSecret { get; init; } = string.Empty;

    public string StateCookieName { get; init; } = "markazor_oauth_state";

    public string RefreshCookieName { get; init; } = "markazor_refresh";

    public string SetupRedirectPath { get; init; } = "/setup?auth=github";

    public Uri AuthorizationEndpoint { get; init; } = new("https://github.com/login/oauth/authorize");

    public Uri TokenEndpoint { get; init; } = new("https://github.com/login/oauth/access_token");

    public TimeSpan StateLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public static MarkazorGitHubOAuthOptions FromEnvironment(Func<string, string?>? readEnvironment = null, MarkazorSiteSettings? settings = null)
    {
        Func<string, string?> read = readEnvironment ?? Environment.GetEnvironmentVariable;
        string clientSecret = read("GITHUB_APP_CLIENT_SECRET") ?? string.Empty;

        return new MarkazorGitHubOAuthOptions
        {
            ClientId = ReadOrDefault(read, "GITHUB_APP_CLIENT_ID", settings?.GitHub?.ClientId ?? string.Empty),
            ClientSecret = clientSecret,
            CookieProtectionSecret = read("MARKAZOR_AUTH_COOKIE_SECRET") ?? clientSecret,
        };
    }

    private static string ReadOrDefault(Func<string, string?> read, string name, string defaultValue)
    {
        string? value = read(name);

        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
