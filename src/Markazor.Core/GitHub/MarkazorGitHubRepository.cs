namespace Markazor.Core.GitHub;

public sealed record MarkazorGitHubRepository(string Name, string FullName, string DefaultBranch, bool IsPrivate, bool CanPull, bool CanPush);
