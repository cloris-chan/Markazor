using Markazor.Core.GitHub;

namespace Markazor.Client;

public sealed record MarkazorSettingsSyncResult(
    bool Succeeded,
    string? CommitSha,
    string? Message,
    MarkazorGitHubClientResultKind? Kind = null);
