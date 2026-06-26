using System.Text;
using Markazor.Client;
using Markazor.Content;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;

namespace Markazor.Editing;

public sealed class MarkazorEditorService(IMarkazorClientSession session, IMarkazorContentCatalog catalog) : IMarkazorEditorService
{
    public async Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return UnauthorizedDocument();
        }

        MarkazorGitHubClientResult<MarkazorGitHubContentFile> result = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.ReadFileAsync(path, token, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            return FailedDocument(result);
        }

        MarkazorEditorDocument document = new(result.Value.Path, result.Value.ContentText, result.Value.Sha);
        catalog.UpsertDocument(document.Path, document.Markdown, document.Sha);

        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(true, document, null);
    }

    public async Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> SaveAsync(string path, string markdown, string? sha, IReadOnlyList<MarkazorEditorAsset>? assets = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(markdown);

        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return UnauthorizedDocument();
        }

        IReadOnlyList<MarkazorEditorAsset> referencedAssets = GetReferencedAssets(markdown, assets);
        if (referencedAssets.Count > 0)
        {
            return await SaveWithAssetsAsync(path, markdown, sha, referencedAssets, cancellationToken).ConfigureAwait(false);
        }

        MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult> result = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => string.IsNullOrWhiteSpace(sha)
                ? client.CreateFileAsync(path, markdown, $"Create {path}", token, cancellationToken: tokenCancellation)
                : client.UpdateFileAsync(path, markdown, $"Update {path}", sha, token, cancellationToken: tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            MarkazorEditorConflict? conflict = result.Kind == MarkazorGitHubClientResultKind.Conflict ? await LoadConflictAsync(path, markdown, cancellationToken).ConfigureAwait(false) : null;

            return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, Describe(result), result.Kind, conflict);
        }

        MarkazorEditorDocument document = new(path, markdown, result.Value.Sha);
        catalog.UpsertDocument(document.Path, document.Markdown, document.Sha);

        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(true, document, null);
    }

    public async Task<MarkazorEditorOperationResult> DeleteAsync(string path, string? sha, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (string.IsNullOrWhiteSpace(sha))
        {
            return new MarkazorEditorOperationResult(false, "The current file sha is required before delete.");
        }

        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new MarkazorEditorOperationResult(false, "GitHub authorization is required.");
        }

        MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult> result = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.DeleteFileAsync(path, $"Delete {path}", sha, token, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            catalog.Remove(path);

            return new MarkazorEditorOperationResult(true, null);
        }

        MarkazorEditorConflict? conflict = result.Kind == MarkazorGitHubClientResultKind.Conflict ? await LoadConflictAsync(path, string.Empty, cancellationToken).ConfigureAwait(false) : null;

        return new MarkazorEditorOperationResult(false, Describe(result), result.Kind, conflict);
    }

    public async Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> PublishDraftAsync(string draftPath, string markdown, string? draftSha, IReadOnlyList<MarkazorEditorAsset>? assets = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftPath);
        ArgumentNullException.ThrowIfNull(markdown);

        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return UnauthorizedDocument();
        }

        string normalizedDraftPath;
        try
        {
            normalizedDraftPath = ContentPathPolicy.NormalizeGitHubPath(draftPath);
        }
        catch (ArgumentException ex)
        {
            return FailedDocument(ex.Message);
        }

        string draftRoot = MarkazorRepositoryPaths.DraftsRoot;
        string postRoot = MarkazorRepositoryPaths.PostsRoot;
        string noteRoot = MarkazorRepositoryPaths.NotesRoot;
        if (!IsFlatDraftMarkdownPath(normalizedDraftPath, draftRoot))
        {
            return FailedDocument($"Only flat Markdown files under {draftRoot} can be published.");
        }

        if (!MarkdownContent.TryCreatePublishedPathFromDraft(normalizedDraftPath, markdown, draftRoot, postRoot, noteRoot, out string? publishedPath)
            || string.IsNullOrWhiteSpace(publishedPath))
        {
            return FailedDocument($"Draft {normalizedDraftPath} cannot be mapped to a published content path.");
        }

        IReadOnlyList<MarkazorEditorAsset> referencedAssets = GetReferencedAssets(markdown, assets);
        MarkazorSetupStatus setupStatus = session.SetupStatus
            ?? throw new InvalidOperationException("Setup status has not been loaded.");
        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.GetBranchRefAsync(setupStatus.Repository.DefaultBranch, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Value?.Sha))
        {
            return FailedDocument(branch);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> baseCommit = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.GetCommitAsync(branch.Value.Sha, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!baseCommit.Succeeded || string.IsNullOrWhiteSpace(baseCommit.Value?.TreeSha))
        {
            return FailedDocument(baseCommit);
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> baseTree = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.GetTreeAsync(baseCommit.Value.TreeSha, token, recursive: true, cancellationToken: tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!baseTree.Succeeded || baseTree.Value is null)
        {
            return FailedDocument(baseTree);
        }

        if (baseTree.Value.Truncated)
        {
            return FailedDocument("GitHub returned a truncated repository tree. Publish was stopped.");
        }

        Dictionary<string, MarkazorGitHubTreeEntry> treeEntries = baseTree.Value.Entries.ToDictionary(static entry => entry.Path, StringComparer.Ordinal);
        bool removeDraft;
        if (treeEntries.TryGetValue(normalizedDraftPath, out MarkazorGitHubTreeEntry? draftEntry))
        {
            if (!string.Equals(draftEntry.Type, "blob", StringComparison.Ordinal))
            {
                return FailedDocument($"Draft {normalizedDraftPath} is not a file.");
            }

            if (string.IsNullOrWhiteSpace(draftSha))
            {
                return new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                    false,
                    null,
                    $"Draft {normalizedDraftPath} already exists on GitHub. Load it before publishing.",
                    MarkazorGitHubClientResultKind.Conflict);
            }

            if (!string.Equals(draftEntry.Sha, draftSha, StringComparison.Ordinal))
            {
                return new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                    false,
                    null,
                    DescribeConflict(),
                    MarkazorGitHubClientResultKind.Conflict,
                    await LoadConflictAsync(normalizedDraftPath, markdown, cancellationToken).ConfigureAwait(false));
            }

            removeDraft = true;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(draftSha))
            {
                return new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                    false,
                    null,
                    "The file was not found on GitHub.",
                    MarkazorGitHubClientResultKind.NotFound);
            }

            removeDraft = false;
        }

        if (treeEntries.ContainsKey(publishedPath))
        {
            return new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                false,
                null,
                $"Publish destination {publishedPath} already exists.",
                MarkazorGitHubClientResultKind.Conflict);
        }

        string publishedMarkdown = MarkdownContent.MarkAsPublished(markdown);
        List<MarkazorGitHubTreeFileChange> changes = [MarkazorGitHubTreeFileChange.Upsert(publishedPath, publishedMarkdown),];
        if (removeDraft)
        {
            changes.Add(MarkazorGitHubTreeFileChange.Remove(normalizedDraftPath));
        }

        foreach (MarkazorEditorAsset asset in referencedAssets)
        {
            string normalizedAssetPath;
            try
            {
                normalizedAssetPath = ContentPathPolicy.NormalizeGitHubPath(asset.RepositoryPath);
            }
            catch (ArgumentException ex)
            {
                return FailedDocument(ex.Message);
            }

            if (!IsAssetPathAllowed(normalizedAssetPath))
            {
                return FailedDocument($"Asset path '{normalizedAssetPath}' is not under the configured asset root.");
            }

            if (treeEntries.ContainsKey(normalizedAssetPath))
            {
                continue;
            }

            MarkazorGitHubClientResult<MarkazorGitHubBlob> blob = await session.ExecuteGitHubAsync(
                (client, token, tokenCancellation) => client.CreateBlobAsync(asset.Content, token, tokenCancellation),
                cancellationToken).ConfigureAwait(false);

            if (!blob.Succeeded || string.IsNullOrWhiteSpace(blob.Value?.Sha))
            {
                return FailedDocument(blob);
            }

            changes.Add(MarkazorGitHubTreeFileChange.UpsertBlob(normalizedAssetPath, blob.Value.Sha));
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.CreateTreeAsync(baseCommit.Value.TreeSha, changes, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!tree.Succeeded || string.IsNullOrWhiteSpace(tree.Value?.Sha))
        {
            return FailedDocument(tree);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.CreateCommitAsync($"Publish {publishedPath}", tree.Value.Sha, branch.Value.Sha, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.Sha))
        {
            return FailedDocument(commit);
        }

        MarkazorGitHubClientResult<MarkazorGitHubRef> updatedRef = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.UpdateBranchRefAsync(setupStatus.Repository.DefaultBranch, commit.Value.Sha, token, cancellationToken: tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!updatedRef.Succeeded)
        {
            MarkazorEditorConflict? updateConflict = updatedRef.Kind == MarkazorGitHubClientResultKind.Conflict && removeDraft
                ? await LoadConflictAsync(normalizedDraftPath, markdown, cancellationToken).ConfigureAwait(false)
                : null;

            return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, Describe(updatedRef), updatedRef.Kind, updateConflict);
        }

        string? publishedSha = tree.Value.Entries
            .FirstOrDefault(entry => string.Equals(entry.Path, publishedPath, StringComparison.Ordinal)
                && string.Equals(entry.Type, "blob", StringComparison.Ordinal))
            ?.Sha;
        MarkazorEditorDocument document = new(publishedPath, publishedMarkdown, publishedSha);
        if (removeDraft)
        {
            catalog.Remove(normalizedDraftPath);
        }

        catalog.UpsertDocument(document.Path, document.Markdown, document.Sha);

        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(true, document, null);
    }

    public async Task<MarkazorBatchPublishResult> PublishDraftsAsync(IReadOnlyList<string> draftPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draftPaths);

        if (draftPaths.Count == 0)
        {
            return FailedBatch("Select at least one draft to publish.");
        }

        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return FailedBatch("GitHub authorization is required.", MarkazorGitHubClientResultKind.Unauthorized);
        }

        string[] normalizedDraftPaths;
        try
        {
            normalizedDraftPaths = [.. draftPaths.Select(ContentPathPolicy.NormalizeGitHubPath).Distinct(StringComparer.Ordinal),];
        }
        catch (ArgumentException ex)
        {
            return FailedBatch(ex.Message);
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            MarkazorBatchPublishResult result = await PublishDraftsAttemptAsync(normalizedDraftPaths, attempt, cancellationToken).ConfigureAwait(false);

            if (result.Succeeded || !result.Retryable || attempt == 2)
            {
                return result;
            }
        }

        return FailedBatch("The branch changed while publishing.", MarkazorGitHubClientResultKind.Conflict, attemptCount: 2);
    }

    private async Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> SaveWithAssetsAsync(string path, string markdown, string? sha, IReadOnlyList<MarkazorEditorAsset> assets, CancellationToken cancellationToken)
    {
        string normalizedPath;
        try
        {
            normalizedPath = ContentPathPolicy.NormalizeGitHubPath(path);
        }
        catch (ArgumentException ex)
        {
            return FailedDocument(ex.Message);
        }

        MarkazorSetupStatus setupStatus = session.SetupStatus
            ?? throw new InvalidOperationException("Setup status has not been loaded.");
        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.GetBranchRefAsync(setupStatus.Repository.DefaultBranch, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Value?.Sha))
        {
            return FailedDocument(branch);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> baseCommit = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetCommitAsync(branch.Value.Sha, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!baseCommit.Succeeded || string.IsNullOrWhiteSpace(baseCommit.Value?.TreeSha))
        {
            return FailedDocument(baseCommit);
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> baseTree = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetTreeAsync(baseCommit.Value.TreeSha, token, recursive: true, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!baseTree.Succeeded || baseTree.Value is null)
        {
            return FailedDocument(baseTree);
        }

        if (baseTree.Value.Truncated)
        {
            return FailedDocument("GitHub returned a truncated repository tree. Save was stopped.");
        }

        Dictionary<string, MarkazorGitHubTreeEntry> treeEntries = baseTree.Value.Entries.ToDictionary(static entry => entry.Path, StringComparer.Ordinal);

        if (treeEntries.TryGetValue(normalizedPath, out MarkazorGitHubTreeEntry? currentEntry))
        {
            if (string.IsNullOrWhiteSpace(sha)
                || !string.Equals(currentEntry.Sha, sha, StringComparison.Ordinal))
            {
                return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, DescribeConflict(), MarkazorGitHubClientResultKind.Conflict, await LoadConflictAsync(normalizedPath, markdown, cancellationToken).ConfigureAwait(false));
            }
        }
        else if (!string.IsNullOrWhiteSpace(sha))
        {
            return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, "The file was not found on GitHub.", MarkazorGitHubClientResultKind.NotFound);
        }

        MarkazorGitHubClientResult<MarkazorGitHubBlob> documentBlob = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.CreateBlobAsync(Encoding.UTF8.GetBytes(markdown), token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!documentBlob.Succeeded || string.IsNullOrWhiteSpace(documentBlob.Value?.Sha))
        {
            return FailedDocument(documentBlob);
        }

        List<MarkazorGitHubTreeFileChange> changes = [MarkazorGitHubTreeFileChange.UpsertBlob(normalizedPath, documentBlob.Value.Sha),];
        foreach (MarkazorEditorAsset asset in assets)
        {
            string normalizedAssetPath;
            try
            {
                normalizedAssetPath = ContentPathPolicy.NormalizeGitHubPath(asset.RepositoryPath);
            }
            catch (ArgumentException ex)
            {
                return FailedDocument(ex.Message);
            }

            if (!IsAssetPathAllowed(normalizedAssetPath))
            {
                return FailedDocument($"Asset path '{normalizedAssetPath}' is not under the configured asset root.");
            }

            if (treeEntries.ContainsKey(normalizedAssetPath))
            {
                continue;
            }

            MarkazorGitHubClientResult<MarkazorGitHubBlob> blob = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.CreateBlobAsync(asset.Content, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

            if (!blob.Succeeded || string.IsNullOrWhiteSpace(blob.Value?.Sha))
            {
                return FailedDocument(blob);
            }

            changes.Add(MarkazorGitHubTreeFileChange.UpsertBlob(normalizedAssetPath, blob.Value.Sha));
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.CreateTreeAsync(baseCommit.Value.TreeSha, changes, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!tree.Succeeded || string.IsNullOrWhiteSpace(tree.Value?.Sha))
        {
            return FailedDocument(tree);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.CreateCommitAsync(string.IsNullOrWhiteSpace(sha) ? $"Create {normalizedPath}" : $"Update {normalizedPath}", tree.Value.Sha, branch.Value.Sha, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.Sha))
        {
            return FailedDocument(commit);
        }

        string commitSha = commit.Value.Sha;
        MarkazorGitHubClientResult<MarkazorGitHubRef> updatedRef = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.UpdateBranchRefAsync(setupStatus.Repository.DefaultBranch, commitSha, token, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!updatedRef.Succeeded)
        {
            MarkazorEditorConflict? updateConflict = updatedRef.Kind == MarkazorGitHubClientResultKind.Conflict
                ? await LoadConflictAsync(normalizedPath, markdown, cancellationToken).ConfigureAwait(false)
                : null;

            return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, Describe(updatedRef), updatedRef.Kind, updateConflict);
        }

        MarkazorEditorDocument document = new(normalizedPath, markdown, documentBlob.Value.Sha);
        catalog.UpsertDocument(document.Path, document.Markdown, document.Sha);

        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(true, document, null);
    }

    private async Task<MarkazorBatchPublishResult> PublishDraftsAttemptAsync(string[] draftPaths, int attempt, CancellationToken cancellationToken)
    {
        string draftRoot = MarkazorRepositoryPaths.DraftsRoot;
        string postRoot = MarkazorRepositoryPaths.PostsRoot;
        string noteRoot = MarkazorRepositoryPaths.NotesRoot;
        MarkazorSetupStatus setupStatus = session.SetupStatus
            ?? throw new InvalidOperationException("Setup status has not been loaded.");
        Dictionary<string, string> destinations = new(StringComparer.Ordinal);

        foreach (string draftPath in draftPaths)
        {
            if (!IsFlatDraftMarkdownPath(draftPath, draftRoot))
            {
                return FailedBatch($"Only flat Markdown files under {draftRoot} can be published.", attemptCount: attempt);
            }
        }

        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetBranchRefAsync(setupStatus.Repository.DefaultBranch, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Value?.Sha))
        {
            return FailedBatch(branch, attempt);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> baseCommit = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetCommitAsync(branch.Value.Sha, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!baseCommit.Succeeded || string.IsNullOrWhiteSpace(baseCommit.Value?.TreeSha))
        {
            return FailedBatch(baseCommit, attempt);
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> baseTree = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.GetTreeAsync(baseCommit.Value.TreeSha, token, recursive: true, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!baseTree.Succeeded || baseTree.Value is null)
        {
            return FailedBatch(baseTree, attempt);
        }

        if (baseTree.Value.Truncated)
        {
            return FailedBatch("GitHub returned a truncated repository tree. Batch publish was stopped.", attemptCount: attempt);
        }

        Dictionary<string, MarkazorGitHubTreeEntry> treeEntries = baseTree.Value.Entries.ToDictionary(static entry => entry.Path, StringComparer.Ordinal);
        List<MarkazorGitHubTreeFileChange> changes = new(draftPaths.Length * 2);
        List<MarkazorEditorDocument> publishedDocuments = new(draftPaths.Length);

        foreach (string draftPath in draftPaths)
        {
            if (!treeEntries.TryGetValue(draftPath, out MarkazorGitHubTreeEntry? draftEntry)
                || !string.Equals(draftEntry.Type, "blob", StringComparison.Ordinal))
            {
                return FailedBatch($"Draft {draftPath} was not found on the branch.", attemptCount: attempt);
            }

            MarkazorGitHubClientResult<MarkazorGitHubBlob> blob = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.ReadBlobAsync(draftEntry.Sha, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

            if (!blob.Succeeded || blob.Value is null)
            {
                return FailedBatch(blob, attempt);
            }

            if (!MarkdownContent.TryCreatePublishedPathFromDraft(draftPath, blob.Value.ContentText, draftRoot, postRoot, noteRoot, out string? publishedPath) || string.IsNullOrWhiteSpace(publishedPath))
            {
                return FailedBatch($"Draft {draftPath} cannot be mapped to a published content path.", attemptCount: attempt);
            }

            if (treeEntries.ContainsKey(publishedPath) || destinations.Values.Contains(publishedPath, StringComparer.Ordinal))
            {
                return FailedBatch($"Publish destination {publishedPath} already exists.", MarkazorGitHubClientResultKind.Conflict, attempt);
            }

            destinations[draftPath] = publishedPath;
            string publishedMarkdown = MarkdownContent.MarkAsPublished(blob.Value.ContentText);
            changes.Add(MarkazorGitHubTreeFileChange.Upsert(publishedPath, publishedMarkdown));
            changes.Add(MarkazorGitHubTreeFileChange.Remove(draftPath));
            publishedDocuments.Add(new MarkazorEditorDocument(publishedPath, publishedMarkdown, null));
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.CreateTreeAsync(baseCommit.Value.TreeSha, changes, token, tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!tree.Succeeded || string.IsNullOrWhiteSpace(tree.Value?.Sha))
        {
            return FailedBatch(tree, attempt);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.CreateCommitAsync(
                draftPaths.Length == 1 ? $"Publish {destinations[draftPaths[0]]}" : $"Publish {draftPaths.Length} drafts",
                tree.Value.Sha,
                branch.Value.Sha,
                token,
                tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.Sha))
        {
            return FailedBatch(commit, attempt);
        }

        MarkazorGitHubClientResult<MarkazorGitHubRef> updatedRef = await session.ExecuteGitHubAsync(
            (client, token, tokenCancellation) => client.UpdateBranchRefAsync(setupStatus.Repository.DefaultBranch, commit.Value.Sha, token, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        if (!updatedRef.Succeeded)
        {
            return FailedBatch(updatedRef, attempt, retryable: updatedRef.Kind == MarkazorGitHubClientResultKind.Conflict);
        }

        Dictionary<string, string> createdShas = tree.Value.Entries
            .Where(static entry => string.Equals(entry.Type, "blob", StringComparison.Ordinal))
            .ToDictionary(static entry => entry.Path, static entry => entry.Sha, StringComparer.Ordinal);

        for (int index = 0; index < publishedDocuments.Count; index++)
        {
            MarkazorEditorDocument document = publishedDocuments[index];
            string? publishedSha = createdShas.GetValueOrDefault(document.Path);
            document = document with { Sha = publishedSha };
            publishedDocuments[index] = document;
            catalog.Remove(draftPaths[index]);
            catalog.UpsertDocument(document.Path, document.Markdown, document.Sha);
        }

        return new MarkazorBatchPublishResult(true, publishedDocuments, commit.Value.Sha, null, AttemptCount: attempt);
    }

    private async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (session.SetupStatus is null)
        {
            _ = await session.LoadSetupStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        return session.IsReady && await session.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private async Task<MarkazorEditorConflict?> LoadConflictAsync(string path, string localMarkdown, CancellationToken cancellationToken)
    {
        MarkazorGitHubClientResult<MarkazorGitHubContentFile> remote = await session.ExecuteGitHubAsync((client, token, tokenCancellation) => client.ReadFileAsync(path, token, cancellationToken: tokenCancellation), cancellationToken).ConfigureAwait(false);

        return remote.Succeeded && remote.Value is not null
            ? new MarkazorEditorConflict(remote.Value.Path, localMarkdown, remote.Value.ContentText, remote.Value.Sha)
            : null;
    }

    private static MarkazorEditorOperationResult<MarkazorEditorDocument> UnauthorizedDocument()
    {
        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, "GitHub authorization is required.", MarkazorGitHubClientResultKind.Unauthorized);
    }

    private static MarkazorEditorOperationResult<MarkazorEditorDocument> FailedDocument<T>(
        MarkazorGitHubClientResult<T> result)
    {
        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, Describe(result), result.Kind);
    }

    private static MarkazorEditorOperationResult<MarkazorEditorDocument> FailedDocument(string message)
    {
        return new MarkazorEditorOperationResult<MarkazorEditorDocument>(false, null, message);
    }

    private static MarkazorBatchPublishResult FailedBatch<T>(MarkazorGitHubClientResult<T> result, int attemptCount, bool retryable = false)
    {
        return FailedBatch(Describe(result), result.Kind, attemptCount, retryable);
    }

    private static MarkazorBatchPublishResult FailedBatch(string message, MarkazorGitHubClientResultKind? kind = null, int attemptCount = 1, bool retryable = false)
    {
        return new MarkazorBatchPublishResult(false, [], null, message, kind, attemptCount, retryable);
    }

    private static string Describe<T>(MarkazorGitHubClientResult<T> result)
    {
        return result.Kind switch
        {
            MarkazorGitHubClientResultKind.Conflict => DescribeConflict(),
            MarkazorGitHubClientResultKind.NotFound => "The file was not found on GitHub.",
            MarkazorGitHubClientResultKind.Unauthorized => "GitHub authorization is required.",
            MarkazorGitHubClientResultKind.Forbidden => "The GitHub authorization does not have permission for this operation.",
            _ => string.IsNullOrWhiteSpace(result.Message) ? "GitHub request failed." : result.Message,
        };
    }

    private static string DescribeConflict()
    {
        return "The file changed on GitHub. Compare the remote version before continuing.";
    }
    private static IReadOnlyList<MarkazorEditorAsset> GetReferencedAssets(
        string markdown,
        IReadOnlyList<MarkazorEditorAsset>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return [];
        }

        return [.. assets.Where(asset => markdown.Contains(asset.MarkdownPath, StringComparison.Ordinal)).DistinctBy(static asset => asset.RepositoryPath, StringComparer.Ordinal),];
    }

    private static bool IsAssetPathAllowed(string normalizedPath)
    {
        return normalizedPath.Length > MarkazorRepositoryPaths.AssetsRoot.Length && normalizedPath.StartsWith(MarkazorRepositoryPaths.AssetsRoot, StringComparison.Ordinal);
    }

    private static bool IsFlatDraftMarkdownPath(string normalizedPath, string draftRoot)
    {
        if (!normalizedPath.StartsWith(draftRoot, StringComparison.Ordinal)
            || !normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = normalizedPath[draftRoot.Length..];

        return suffix.Length > 0 && !suffix.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
