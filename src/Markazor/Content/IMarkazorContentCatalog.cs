namespace Markazor.Content;

public interface IMarkazorContentCatalog
{
    IReadOnlyList<MarkazorContentEntry> Entries { get; }

    bool IsBuildIndexFallback { get; }

    string? Warning { get; }

    Task<IReadOnlyList<MarkazorContentEntry>> RefreshAsync(CancellationToken cancellationToken = default);

    void UpsertDocument(string path, string markdown, string? sha);

    void Remove(string path);
}
