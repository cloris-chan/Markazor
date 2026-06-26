namespace Markazor.Core.Auth;

public sealed record MarkazorGitHubAuthorizationResponse(Uri AuthorizationUrl, DateTimeOffset StateExpiresAtUtc);
