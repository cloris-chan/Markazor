namespace Markazor.Core.GitHub;

public sealed record MarkazorGitHubTree(string Sha, IReadOnlyList<MarkazorGitHubTreeEntry> Entries, bool Truncated);
