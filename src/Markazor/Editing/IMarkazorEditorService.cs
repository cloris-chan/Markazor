namespace Markazor.Editing;

public interface IMarkazorEditorService
{
    Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> LoadAsync(string path, CancellationToken cancellationToken = default);

    Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> SaveAsync(string path, string markdown, string? sha, IReadOnlyList<MarkazorEditorAsset>? assets = null, CancellationToken cancellationToken = default);

    Task<MarkazorEditorOperationResult> DeleteAsync(string path, string? sha, CancellationToken cancellationToken = default);

    Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> PublishDraftAsync(string draftPath, string markdown, string? draftSha, IReadOnlyList<MarkazorEditorAsset>? assets = null, CancellationToken cancellationToken = default);

    Task<MarkazorBatchPublishResult> PublishDraftsAsync(IReadOnlyList<string> draftPaths, CancellationToken cancellationToken = default);
}
