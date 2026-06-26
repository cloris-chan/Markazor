namespace Markazor.Client;

public sealed record MarkazorRepositoryDiagnostics(
    bool Ready,
    bool RepositoryAccessible,
    bool CanPull,
    bool CanPush,
    bool BranchAccessible,
    bool TreeReadable,
    string? GitHubDefaultBranch,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
