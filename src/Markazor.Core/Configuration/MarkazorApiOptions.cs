namespace Markazor.Core.Configuration;

public sealed class MarkazorApiOptions
{
    public Uri GitHubApiBaseUri { get; init; } = new("https://api.github.com/");

    public string RepositoryOwner { get; init; } = string.Empty;

    public string RepositoryName { get; init; } = string.Empty;

    public string DefaultBranch { get; init; } = "main";
}
