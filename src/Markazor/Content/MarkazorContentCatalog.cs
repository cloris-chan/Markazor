using Markazor.Client;
using Markazor.Configuration;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;

namespace Markazor.Content;

public sealed class MarkazorContentCatalog(IMarkazorClientSession session, MarkazorOptions options) : IMarkazorContentCatalog
{
    private IReadOnlyList<MarkazorContentEntry> entries = CreateFallbackEntries(options.Articles);

    public IReadOnlyList<MarkazorContentEntry> Entries => entries;

    public bool IsBuildIndexFallback { get; private set; } = true;

    public string? Warning { get; private set; }

    public async Task<IReadOnlyList<MarkazorContentEntry>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (session.SetupStatus is null)
        {
            _ = await session.LoadSetupStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!session.IsReady)
        {
            UseFallback("Setup is incomplete. Showing the build-time content index.");

            return entries;
        }

        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetBranchRefAsync(session.SetupStatus!.Repository.DefaultBranch, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Value?.Sha))
        {
            UseFallback(DescribeFallback(branch));

            return entries;
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetCommitAsync(branch.Value.Sha, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.TreeSha))
        {
            UseFallback(DescribeFallback(commit));

            return entries;
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.GetTreeAsync(commit.Value.TreeSha, token, recursive: true, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!tree.Succeeded || tree.Value is null)
        {
            UseFallback(DescribeFallback(tree));

            return entries;
        }

        if (tree.Value.Truncated)
        {
            Warning = "GitHub returned a truncated repository tree. The previous content list was preserved.";

            return entries;
        }

        entries = MergeTree(tree.Value);
        IsBuildIndexFallback = false;
        Warning = null;

        return entries;
    }

    public void UpsertDocument(string path, string markdown, string? sha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(markdown);

        string normalizedPath = ContentPathPolicy.NormalizeGitHubPath(path);
        MarkazorContentEntry entry = new(MarkdownContent.ParseArticleMeta(normalizedPath, markdown), sha, ExistsOnGitHub: !string.IsNullOrWhiteSpace(sha));
        Dictionary<string, MarkazorContentEntry> updated = entries.ToDictionary(static item => item.RelativePath, StringComparer.Ordinal);
        updated[normalizedPath] = entry;
        entries = Sort(updated.Values);
    }

    public void Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string normalizedPath = ContentPathPolicy.NormalizeGitHubPath(path);
        entries = [.. entries.Where(entry => !string.Equals(entry.RelativePath, normalizedPath, StringComparison.Ordinal))];
    }

    private IReadOnlyList<MarkazorContentEntry> MergeTree(MarkazorGitHubTree tree)
    {
        Dictionary<string, ArticleMeta> buildMetadata = options.Articles.ToDictionary(static article => article.RelativePath.Replace('\\', '/'), StringComparer.Ordinal);
        List<MarkazorContentEntry> merged = [];

        foreach (MarkazorGitHubTreeEntry treeEntry in tree.Entries)
        {
            string path = treeEntry.Path.Replace('\\', '/');
            if (!string.Equals(treeEntry.Type, "blob", StringComparison.Ordinal)
                || !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || (!path.StartsWith(MarkazorRepositoryPaths.DraftsRoot, StringComparison.Ordinal)
                    && !path.StartsWith(MarkazorRepositoryPaths.PostsRoot, StringComparison.Ordinal)
                    && !path.StartsWith(MarkazorRepositoryPaths.NotesRoot, StringComparison.Ordinal)))
            {
                continue;
            }

            ArticleMeta metadata = buildMetadata.TryGetValue(path, out ArticleMeta? article) ? article : MarkdownContent.ParseArticleMeta(path, string.Empty);
            merged.Add(new MarkazorContentEntry(metadata, treeEntry.Sha, ExistsOnGitHub: true));
        }

        return Sort(merged);
    }

    private void UseFallback(string warning)
    {
        if (!IsBuildIndexFallback)
        {
            Warning = warning;

            return;
        }

        entries = CreateFallbackEntries(options.Articles);
        IsBuildIndexFallback = true;
        Warning = warning;
    }

    private static string DescribeFallback<T>(MarkazorGitHubClientResult<T> result)
    {
        return result.Kind == MarkazorGitHubClientResultKind.Unauthorized
            ? "GitHub authorization is required. Showing the build-time content index."
            : "The GitHub content tree could not be loaded. Showing the last available content list.";
    }

    private static IReadOnlyList<MarkazorContentEntry> CreateFallbackEntries(
        IReadOnlyList<ArticleMeta> articles)
    {
        return Sort(articles.Select(static article => new MarkazorContentEntry(article, Sha: null, ExistsOnGitHub: false)));
    }

    private static IReadOnlyList<MarkazorContentEntry> Sort(
        IEnumerable<MarkazorContentEntry> contentEntries)
    {
        return
        [
            .. contentEntries
                .OrderByDescending(static entry => entry.IsDraft)
                .ThenByDescending(static entry => entry.Article.PublishedAtUtc)
                .ThenBy(static entry => entry.RelativePath, StringComparer.Ordinal),
        ];
    }
}
