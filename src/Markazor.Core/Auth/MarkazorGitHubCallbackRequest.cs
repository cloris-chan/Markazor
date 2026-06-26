namespace Markazor.Core.Auth;

public sealed record MarkazorGitHubCallbackRequest(string? Code, string? State);
