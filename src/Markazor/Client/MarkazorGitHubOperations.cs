using Markazor.Core.GitHub;
using Markazor.Core.Setup;

namespace Markazor.Client;

internal static class MarkazorGitHubOperations
{
    extension(IMarkazorClientSession session)
    {
        public async Task<MarkazorGitHubClientResult<MarkazorGitHubCommit>> ReadBranchAsync(MarkazorRepositorySettings? repository = null, CancellationToken cancellationToken = default)
        {
            string branch = repository?.DefaultBranch ?? session.SetupStatus!.Repository.DefaultBranch;
            MarkazorGitHubClientResult<MarkazorGitHubRef> head = await session.ExecuteGitHubAsync(
                (client, token, ct) => client.GetBranchRefAsync(branch, token, ct), repository, cancellationToken).ConfigureAwait(false);
            if (!head.Succeeded || string.IsNullOrWhiteSpace(head.Value?.Sha))
            {
                return new(head.Succeeded ? MarkazorGitHubClientResultKind.Error : head.Kind, null, head.StatusCode, head.Message ?? "GitHub returned an incomplete branch reference.");
            }

            MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync(
                (client, token, ct) => client.GetCommitAsync(head.Value.Sha, token, ct), repository, cancellationToken).ConfigureAwait(false);
            return commit.Succeeded && !string.IsNullOrWhiteSpace(commit.Value?.TreeSha)
                ? commit with { Value = commit.Value with { Sha = head.Value.Sha } }
                : commit with { Kind = commit.Succeeded ? MarkazorGitHubClientResultKind.Error : commit.Kind, Value = null, Message = commit.Message ?? "GitHub returned an incomplete commit." };
        }

        public async Task<MarkazorGitHubClientResult<MarkazorGitHubCommitChanges>> CommitChangesAsync(
            MarkazorGitHubCommit parent, IReadOnlyCollection<MarkazorGitHubTreeFileChange> changes, string message,
            MarkazorRepositorySettings? repository = null, CancellationToken cancellationToken = default)
        {
            MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await session.ExecuteGitHubAsync(
                (client, token, ct) => client.CreateTreeAsync(parent.TreeSha, changes, token, ct), repository, cancellationToken).ConfigureAwait(false);
            if (!tree.Succeeded || string.IsNullOrWhiteSpace(tree.Value?.Sha))
            {
                return Failed(tree);
            }

            MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync(
                (client, token, ct) => client.CreateCommitAsync(message, tree.Value.Sha, parent.Sha, token, ct), repository, cancellationToken).ConfigureAwait(false);
            if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.Sha))
            {
                return Failed(commit);
            }

            string branch = repository?.DefaultBranch ?? session.SetupStatus!.Repository.DefaultBranch;
            MarkazorGitHubClientResult<MarkazorGitHubRef> update = await session.ExecuteGitHubAsync(
                (client, token, ct) => client.UpdateBranchRefAsync(branch, commit.Value.Sha, token, cancellationToken: ct), repository, cancellationToken).ConfigureAwait(false);
            return update.Succeeded
                ? new(MarkazorGitHubClientResultKind.Success, new(commit.Value.Sha, tree.Value), update.StatusCode, null)
                : Failed(update, refUpdate: true);
        }
    }

    private static MarkazorGitHubClientResult<MarkazorGitHubCommitChanges> Failed<T>(MarkazorGitHubClientResult<T> result, bool refUpdate = false)
    {
        MarkazorGitHubClientResultKind kind = result.Succeeded || (result.Kind == MarkazorGitHubClientResultKind.Conflict && !refUpdate)
            ? MarkazorGitHubClientResultKind.Error : result.Kind;
        return new(kind, null, result.StatusCode, result.Message ?? "GitHub returned incomplete commit data.");
    }
}

internal sealed record MarkazorGitHubCommitChanges(string CommitSha, MarkazorGitHubTree Tree);
