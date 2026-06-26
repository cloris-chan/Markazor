namespace Markazor.Api.Auth;

public sealed record MarkazorGitHubOAuthCallbackResult(string AccessToken, DateTimeOffset? AccessTokenExpiresAtUtc, string RefreshCookieValue, DateTimeOffset? RefreshTokenExpiresAtUtc);
