namespace Markazor.Api.Auth;

public sealed record MarkazorGitHubOAuthRefreshResult(string AccessToken, DateTimeOffset? AccessTokenExpiresAtUtc, string? RefreshCookieValue, DateTimeOffset? RefreshTokenExpiresAtUtc);
