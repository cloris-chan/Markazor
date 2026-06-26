namespace Markazor.Api.Auth;

public sealed record MarkazorGitHubOAuthStartResult(Uri AuthorizationUrl, string StateCookieValue, DateTimeOffset StateExpiresAtUtc);
