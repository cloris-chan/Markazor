using Markazor.Core.GitHub;

namespace Markazor.Editing;

public sealed record MarkazorBatchPublishResult(bool Succeeded, IReadOnlyList<MarkazorEditorDocument> PublishedDocuments, string? CommitSha, string? Message, MarkazorGitHubClientResultKind? Kind = null, int AttemptCount = 1, bool Retryable = false);
