using Markazor.Configuration;
using Markazor.Content;

namespace Markazor.Reading;

public sealed class MarkazorReaderService : IMarkazorReaderService
{
    private readonly HttpClient httpClient;
    private readonly IMarkazorMarkdownRenderer markdownRenderer;
    private readonly IReadOnlyList<ArticleMeta> publishedArticles;
    private readonly IReadOnlyList<ArticleMeta> publishedNotes;
    private readonly IReadOnlyList<ArticleMeta> publishedPosts;

    public MarkazorReaderService(HttpClient httpClient, IMarkazorMarkdownRenderer markdownRenderer, MarkazorOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(markdownRenderer);
        ArgumentNullException.ThrowIfNull(options);

        this.httpClient = httpClient;
        this.markdownRenderer = markdownRenderer;
        Site = options.Site;
        publishedArticles = [.. options.Articles.Where(static article => !article.IsDraft).OrderByDescending(static article => article.PublishedAtUtc).ThenBy(static article => article.RelativePath, StringComparer.Ordinal),];
        publishedPosts = FilterByKind(publishedArticles, MarkazorArticleKind.Post);
        publishedNotes = FilterByKind(publishedArticles, MarkazorArticleKind.Note);
    }

    public MarkazorSiteOptions Site { get; }

    public MarkazorArticlePage GetArticles(int pageNumber = 1)
    {
        return CreatePage(publishedArticles, pageNumber);
    }

    public MarkazorArticlePage GetPosts(int pageNumber = 1)
    {
        return CreatePage(publishedPosts, pageNumber);
    }

    public MarkazorArticlePage GetNotes(int pageNumber = 1)
    {
        return CreatePage(publishedNotes, pageNumber);
    }

    public MarkazorArticlePage GetCategory(string category, int pageNumber = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return CreatePage(publishedArticles.Where(article => string.Equals(article.Category, category, StringComparison.OrdinalIgnoreCase)),
            pageNumber);
    }

    public MarkazorArticlePage GetTag(string tag, int pageNumber = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        return CreatePage(publishedArticles.Where(article => article.Tags.Any(candidate => string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))), pageNumber);
    }

    public ArticleMeta? FindArticle(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return FindArticle(publishedArticles, slug);
    }

    public ArticleMeta? FindArticle(string slug, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return FindArticle(FilterByKind(publishedArticles, kind), slug);
    }

    public IReadOnlyList<MarkazorTaxonomyItem> GetCategories()
    {
        return AggregateTaxonomy(
            publishedArticles
                .Select(static article => article.Category)
                .Where(static category => !string.IsNullOrWhiteSpace(category))
                .Select(static category => category!));
    }

    public IReadOnlyList<MarkazorTaxonomyItem> GetTags()
    {
        return AggregateTaxonomy(publishedArticles.SelectMany(static article => article.Tags));
    }

    public IReadOnlyList<MarkazorArchiveGroup> GetArchive()
    {
        return
        [
            .. publishedArticles
                .GroupBy(static article => new
                {
                    article.PublishedAtUtc.Year,
                    article.PublishedAtUtc.Month,
                })
                .OrderByDescending(static group => group.Key.Year)
                .ThenByDescending(static group => group.Key.Month)
                .Select(static group => new MarkazorArchiveGroup(
                    group.Key.Year,
                    group.Key.Month,
                    [.. group])),
        ];
    }

    public MarkazorArticleNavigation GetNavigation(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return CreateNavigation(publishedArticles, slug);
    }

    public MarkazorArticleNavigation GetNavigation(string slug, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return CreateNavigation(FilterByKind(publishedArticles, kind), slug);
    }

    private static MarkazorArticleNavigation CreateNavigation(IReadOnlyList<ArticleMeta> articles, string slug)
    {
        int index = FindIndex(articles, slug);

        return index < 0
            ? new MarkazorArticleNavigation(null, null)
            : new MarkazorArticleNavigation(
                PreviousArticle: index + 1 < articles.Count ? articles[index + 1] : null,
                NextArticle: index > 0 ? articles[index - 1] : null);
    }

    public async Task<string> LoadSafeHtmlAsync(ArticleMeta article, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(article);

        ArticleMeta? publishedArticle = publishedArticles.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, article.RelativePath, StringComparison.Ordinal)) ?? throw new InvalidOperationException("Only published articles can be loaded by the reader service.");
        string markdown = await httpClient.GetStringAsync(new Uri(publishedArticle.ContentPath, UriKind.Relative), cancellationToken).ConfigureAwait(false);

        return markdownRenderer.ToSafeHtml(markdown);
    }

    private MarkazorArticlePage CreatePage(IEnumerable<ArticleMeta> source, int requestedPage)
    {
        int pageSize = Math.Max(1, Site.PageSize);
        IReadOnlyList<ArticleMeta> articles = [.. source];
        int totalPages = Math.Max(1, (int)Math.Ceiling(articles.Count / (double)pageSize));
        int pageNumber = Math.Clamp(requestedPage, 1, totalPages);

        return new MarkazorArticlePage([.. articles.Skip((pageNumber - 1) * pageSize).Take(pageSize)], pageNumber, pageSize, articles.Count, totalPages);
    }

    private static ArticleMeta? FindArticle(IReadOnlyList<ArticleMeta> articles, string slug)
    {
        return articles.FirstOrDefault(article => string.Equals(article.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    private static int FindIndex(IReadOnlyList<ArticleMeta> articles, string slug)
    {
        for (int index = 0; index < articles.Count; index++)
        {
            if (string.Equals(articles[index].Slug, slug, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<ArticleMeta> FilterByKind(IReadOnlyList<ArticleMeta> articles, string kind)
    {
        string normalizedKind = MarkazorArticleKind.Normalize(kind);

        return [.. articles.Where(article => string.Equals(article.Kind, normalizedKind, StringComparison.Ordinal))];
    }

    private static IReadOnlyList<MarkazorTaxonomyItem> AggregateTaxonomy(IEnumerable<string> values)
    {
        Dictionary<string, MarkazorTaxonomyItem> items = new(StringComparer.OrdinalIgnoreCase);

        foreach (string value in values.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            string normalized = value.Trim();
            items[normalized] = items.TryGetValue(normalized, out MarkazorTaxonomyItem? item)
                ? item with { Count = item.Count + 1 }
                : new MarkazorTaxonomyItem(normalized, 1);
        }

        return [.. items.Values.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase),];
    }
}
