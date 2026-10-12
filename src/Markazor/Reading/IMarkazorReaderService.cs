using Markazor.Configuration;
using Markazor.Content;

namespace Markazor.Reading;

public interface IMarkazorReaderService
{
    MarkazorSiteOptions Site { get; }

    MarkazorArticlePage GetArticles(int pageNumber = 1);

    MarkazorArticlePage GetCategory(string category, int pageNumber = 1);

    MarkazorArticlePage GetTag(string tag, int pageNumber = 1);

    ArticleMeta? FindArticle(string slug);

    IReadOnlyList<MarkazorTaxonomyItem> GetCategories();

    IReadOnlyList<MarkazorTaxonomyItem> GetTags();

    IReadOnlyList<MarkazorArchiveGroup> GetArchive();

    MarkazorArticleNavigation GetNavigation(string slug);

    Task<MarkdownRenderResult> LoadContentAsync(ArticleMeta article, CancellationToken cancellationToken = default);
}
