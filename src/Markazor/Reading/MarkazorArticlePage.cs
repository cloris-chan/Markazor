using Markazor.Content;

namespace Markazor.Reading;

public sealed record MarkazorArticlePage(IReadOnlyList<ArticleMeta> Articles, int PageNumber, int PageSize, int TotalItems, int TotalPages)
{
    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}
