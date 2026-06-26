using Markazor.Core.GitHub;
using Markazor.Core.Setup;

namespace Markazor.Client;

public sealed class MarkazorSetupDiagnosticsService(
    IMarkazorClientSession session) : IMarkazorSetupDiagnosticsService
{
    public async Task<MarkazorRepositoryDiagnostics> RunAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> errors = [];
        List<string> warnings = [];

        if (session.SetupStatus is null)
        {
            _ = await session.LoadSetupStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        MarkazorSetupStatus status = session.SetupStatus
            ?? throw new InvalidOperationException("Setup status could not be loaded.");
        if (!MarkazorSetupReadiness.HasRepositoryConfiguration(status))
        {
            errors.Add("Required environment settings are incomplete.");

            return CreateResult(errors, warnings);
        }

        string? token = await session.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            errors.Add("GitHub authorization is required.");

            return CreateResult(errors, warnings);
        }

        MarkazorGitHubClientResult<MarkazorGitHubRepository> repository = await session.ExecuteGitHubAsync(
            static (client, accessToken, tokenCancellation) => client.GetRepositoryAsync(
                accessToken,
                tokenCancellation),
            cancellationToken).ConfigureAwait(false);
        if (!repository.Succeeded || repository.Value is null)
        {
            errors.Add(Describe(repository, "The configured repository could not be opened."));

            return CreateResult(errors, warnings);
        }

        if (!repository.Value.CanPull)
        {
            errors.Add("The GitHub authorization cannot read repository contents.");
        }

        if (!repository.Value.CanPush)
        {
            errors.Add("The GitHub authorization cannot push repository content.");
        }

        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await session.ExecuteGitHubAsync(
            (client, accessToken, tokenCancellation) => client.GetBranchRefAsync(
                status.Repository.DefaultBranch,
                accessToken,
                tokenCancellation),
            cancellationToken).ConfigureAwait(false);
        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Value?.Sha))
        {
            errors.Add(Describe(
                branch,
                $"The configured branch '{status.Repository.DefaultBranch}' could not be read."));

            return CreateResult(
                errors,
                warnings,
                repository.Value);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync(
            (client, accessToken, tokenCancellation) => client.GetCommitAsync(
                branch.Value.Sha,
                accessToken,
                tokenCancellation),
            cancellationToken).ConfigureAwait(false);
        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.TreeSha))
        {
            errors.Add(Describe(commit, "The configured branch commit could not be read."));

            return CreateResult(
                errors,
                warnings,
                repository.Value,
                branchAccessible: true);
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await session.ExecuteGitHubAsync(
            (client, accessToken, tokenCancellation) => client.GetTreeAsync(
                commit.Value.TreeSha,
                accessToken,
                recursive: true,
                cancellationToken: tokenCancellation),
            cancellationToken).ConfigureAwait(false);
        if (!tree.Succeeded || tree.Value is null)
        {
            errors.Add(Describe(tree, "The repository tree could not be read."));

            return CreateResult(
                errors,
                warnings,
                repository.Value,
                branchAccessible: true);
        }

        if (tree.Value.Truncated)
        {
            warnings.Add("GitHub returned a truncated repository tree, so diagnostics may be incomplete.");
        }

        return CreateResult(
            errors,
            warnings,
            repository.Value,
            branchAccessible: true,
            treeReadable: true);
    }

    private static string Describe<T>(MarkazorGitHubClientResult<T> result, string fallback)
    {
        return result.Kind switch
        {
            MarkazorGitHubClientResultKind.Unauthorized => "GitHub authorization is required.",
            MarkazorGitHubClientResultKind.Forbidden => "The GitHub authorization does not have access to the configured repository.",
            MarkazorGitHubClientResultKind.NotFound => fallback,
            _ => string.IsNullOrWhiteSpace(result.Message) ? fallback : result.Message,
        };
    }

    private static MarkazorRepositoryDiagnostics CreateResult(
        List<string> errors,
        List<string> warnings,
        MarkazorGitHubRepository? repository = null,
        bool branchAccessible = false,
        bool treeReadable = false)
    {
        return new MarkazorRepositoryDiagnostics(
            Ready: errors.Count == 0 && repository is not null && branchAccessible && treeReadable,
            RepositoryAccessible: repository is not null,
            CanPull: repository?.CanPull == true,
            CanPush: repository?.CanPush == true,
            BranchAccessible: branchAccessible,
            TreeReadable: treeReadable,
            GitHubDefaultBranch: repository?.DefaultBranch,
            Errors: errors,
            Warnings: warnings);
    }
}
