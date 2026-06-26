namespace Markazor.Core.Auth;

public sealed record MarkazorGitHubAuthorizationRequest(string? ClientId, Uri? SiteBaseUrl);
