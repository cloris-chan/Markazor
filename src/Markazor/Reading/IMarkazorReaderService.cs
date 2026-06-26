using Markazor.Configuration;
using Markazor.Content;

namespace Markazor.Reading;

public interface IMarkazorReaderService
{
    MarkazorSiteOptions Site { get; }

    MarkazorArticlePage GetArticles(int pageNumber = 1);

    MarkazorArticlePage GetPosts(int pageNumber = 1);

    MarkazorArticlePage GetNotes(int pageNumber = 1);

    MarkazorArticlePage GetCategory(string category, int pageNumber = 1);

    MarkazorArticlePage GetTag(string tag, int pageNumber = 1);

    ArticleMeta? FindArticle(string slug);

    ArticleMeta? FindArticle(string slug, string kind);

    IReadOnlyList<MarkazorTaxonomyItem> GetCategories();

    IReadOnlyList<MarkazorTaxonomyItem> GetTags();

    IReadOnlyList<MarkazorArchiveGroup> GetArchive();

    MarkazorArticleNavigation GetNavigation(string slug);

    MarkazorArticleNavigation GetNavigation(string slug, string kind);

    Task<string> LoadSafeHtmlAsync(ArticleMeta article, CancellationToken cancellationToken = default);
}
