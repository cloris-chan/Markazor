namespace Markazor.Core.Auth;

public sealed record MarkazorGitHubAccessTokenResponse(string AccessToken, DateTimeOffset? AccessTokenExpiresAtUtc);
