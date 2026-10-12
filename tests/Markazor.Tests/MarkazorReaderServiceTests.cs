using System.Net;
using System.Text;
using Markazor.Configuration;
using Markazor.Content;
using Markazor.Reading;

namespace Markazor.Tests;

public sealed class MarkazorReaderServiceTests
{
    [Fact]
    public void PagesPublishedArticlesAndClampsRequestedPage()
    {
        using HttpClient httpClient = CreateHttpClient();
        MarkazorOptions options = CreateOptions();
        options.Site.PageSize = 2;
        MarkazorReaderService reader = new(httpClient, new MarkazorMarkdownRenderer(), options);

        MarkazorArticlePage first = reader.GetArticles(0);
        MarkazorArticlePage last = reader.GetArticles(99);

        Assert.Equal(1, first.PageNumber);
        Assert.Equal(["field-update", "newest"], first.Articles.Select(static article => article.Slug));
        Assert.False(first.HasPreviousPage);
        Assert.True(first.HasNextPage);
        Assert.Equal(2, last.PageNumber);
        Assert.Equal(["second", "oldest"], last.Articles.Select(static article => article.Slug));
        Assert.True(last.HasPreviousPage);
        Assert.False(last.HasNextPage);
        Assert.DoesNotContain(first.Articles.Concat(last.Articles), static article => article.IsDraft);
    }

    [Fact]
    public void AggregatesTaxonomyCaseInsensitivelyAndKeepsFirstDisplayValue()
    {
        using HttpClient httpClient = CreateHttpClient();
        MarkazorReaderService reader = new(
            httpClient,
            new MarkazorMarkdownRenderer(),
            CreateOptions());

        Assert.Equal(
            [
                new MarkazorTaxonomyItem("General", 2),
                new MarkazorTaxonomyItem("Journal", 1),
                new MarkazorTaxonomyItem("Updates", 1),
            ],
            reader.GetCategories());
        Assert.Equal(
            [
                new MarkazorTaxonomyItem("dotnet", 2),
                new MarkazorTaxonomyItem("journal", 1),
                new MarkazorTaxonomyItem("release", 1),
            ],
            reader.GetTags());
        Assert.Equal(2, reader.GetCategory("GENERAL").TotalItems);
        Assert.Equal(2, reader.GetTag("dotnet").TotalItems);
    }

    [Fact]
    public void BuildsArchiveAndChronologicalNavigation()
    {
        using HttpClient httpClient = CreateHttpClient();
        MarkazorReaderService reader = new(
            httpClient,
            new MarkazorMarkdownRenderer(),
            CreateOptions());

        IReadOnlyList<MarkazorArchiveGroup> archive = reader.GetArchive();
        MarkazorArticleNavigation navigation = reader.GetNavigation("second");

        Assert.Collection(
            archive,
            group =>
            {
                Assert.Equal((2026, 6), (group.Year, group.Month));
                Assert.Equal(["field-update", "newest", "second"], group.Articles.Select(static article => article.Slug));
            },
            group =>
            {
                Assert.Equal((2026, 5), (group.Year, group.Month));
                Assert.Equal("oldest", Assert.Single(group.Articles).Slug);
            });
        Assert.Equal("oldest", navigation.PreviousArticle?.Slug);
        Assert.Equal("newest", navigation.NextArticle?.Slug);
        Assert.Null(reader.FindArticle("draft"));
    }

    [Fact]
    public async Task LoadsAndSanitizesPublishedMarkdown()
    {
        using HttpClient httpClient = CreateHttpClient(
            """
            ---
            title: Newest
            ---
            # Hello
            <script>alert('no')</script>
            """);
        MarkazorReaderService reader = new(
            httpClient,
            new MarkazorMarkdownRenderer(),
            CreateOptions());
        ArticleMeta article = Assert.IsType<ArticleMeta>(reader.FindArticle("newest"));
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string html = (await reader.LoadContentAsync(article, cancellationToken)).Html;

        Assert.Contains("<h2 id=\"mk-hello\">Hello", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    private static MarkazorOptions CreateOptions()
    {
        return new MarkazorOptions
        {
            Articles =
            [
                Article("oldest", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), "General", ["DotNet"]),
                Article("draft", new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero), "Private", ["secret"], isDraft: true),
                Article("field-update", new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero), "Journal", ["journal"]),
                Article("newest", new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero), "General", ["dotnet"]),
                Article("second", new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), "Updates", ["release"]),
            ],
        };
    }

    private static ArticleMeta Article(
        string slug,
        DateTimeOffset publishedAt,
        string? category,
        IReadOnlyList<string> tags,
        bool isDraft = false)
    {
        string root = isDraft ? "drafts" : "posts";

        return new ArticleMeta(
            slug,
            slug,
            string.Empty,
            publishedAt,
            tags,
            category,
            ContentPath(root, string.Concat(slug, ".md")),
            RoutePath("posts", slug),
            isDraft);
    }

    private static string ContentPath(params string[] segments)
    {
        return string.Join(Path.AltDirectorySeparatorChar, segments);
    }

    private static string RoutePath(string routeRoot, string slug)
    {
        return string.Concat(
            Path.AltDirectorySeparatorChar,
            string.Join(Path.AltDirectorySeparatorChar, routeRoot, slug));
    }

    private static MarkdownClient CreateHttpClient(string markdown = "# Empty")
    {
        return new MarkdownClient(markdown);
    }

    private sealed class MarkdownClient : HttpClient
    {
        public MarkdownClient(string markdown)
#pragma warning disable CA2000
            : base(new MarkdownHandler(markdown), disposeHandler: true)
#pragma warning restore CA2000
        {
            BaseAddress = new Uri("https://markazor.test/");
        }
    }

    private sealed class MarkdownHandler(string markdown) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(markdown, Encoding.UTF8, "text/markdown"),
            });
        }
    }
}
