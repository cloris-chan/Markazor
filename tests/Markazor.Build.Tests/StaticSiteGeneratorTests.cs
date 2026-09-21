using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;

namespace Markazor.Build.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1849", Justification = "The fixture uses small local files and in-memory DOM parsing; only component lifecycle execution is asynchronous.")]
public sealed class StaticSiteGeneratorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "markazor-static-tests-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(root, "output");

    public StaticSiteGeneratorTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "public"));
        Directory.CreateDirectory(Path.Combine(root, "posts"));
        Directory.CreateDirectory(Path.Combine(root, "drafts"));
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "index.html"), "<!doctype html><html lang='en'><head><title>Old shell</title><base href='/'></head><body><div id='app'></div><script src='_framework/blazor.webassembly.js'></script></body></html>");
        File.WriteAllText(Path.Combine(Output, "staticwebapp.config.json"), "{\"globalHeaders\":{\"X-Frame-Options\":\"DENY\"},\"navigationFallback\":{\"rewrite\":\"/index.html\",\"exclude\":[\"/api/*\"]}}");
        File.WriteAllText(Path.Combine(Output, "manifest.webmanifest"), "{\"lang\":\"en\"}");
        File.WriteAllText(Path.Combine(root, "public", "markazor.settings.json"), "{\"site\":{\"name\":\"My journal\",\"description\":\"A & B\",\"baseUrls\":[\"https://example.test\"]}}");
    }

    [Fact]
    public async Task GeneratesRealArticleHtmlMetadataAndStaticTaxonomyRoutes()
    {
        File.WriteAllText(Path.Combine(root, "posts", "hello.md"), "---\nslug: hello\ntitle: Hello & welcome\nsummary: A <thought> & a story\npublishedAt: 2026-09-21\ntags: [dotnet, C#]\ncategory: Reading\n---\n\n# Hello & welcome\n\n## A section\n\nBody with **meaning** and a note.[^a]\n\n[^a]: A source.");
        File.WriteAllText(Path.Combine(root, "drafts", "private.md"), "---\ntitle: SECRET DRAFT\n---\nNever publish this.");
        int count = await StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken);
        Dictionary<string, string> routes = ReadRoutes();
        Assert.Equal(count, routes.Count);
        Assert.Contains("/tags/c%23", routes.Keys);
        Assert.True(File.Exists(Path.Combine(Output, "tags", "c#", "index.html")));
        Assert.Contains("/categories/reading", routes.Keys);
        string html = File.ReadAllText(Path.Combine(Output, routes["/posts/hello"].TrimStart('/')));
        using var document = new HtmlParser().ParseDocument(html);
        Assert.Single(document.QuerySelectorAll("h1"));
        Assert.Equal("Hello & welcome", document.QuerySelector("h1")!.TextContent);
        Assert.Equal("meaning", document.QuerySelector(".markazor-prose strong")!.TextContent);
        Assert.Equal("en", document.DocumentElement.GetAttribute("lang"));
        Assert.Contains("Monday, 21 September 2026", document.QuerySelector("time")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("https://example.test/posts/hello", document.QuerySelector("link[rel='canonical']")!.GetAttribute("href"));
        Assert.Equal("A <thought> & a story", document.QuerySelector("meta[name='description']")!.GetAttribute("content"));
        Assert.NotNull(document.QuerySelector("meta[property='og:image']"));
        Assert.NotNull(document.QuerySelector("script[type='application/ld+json']"));
        Assert.Empty(document.QuerySelectorAll("script[src*='blazor']"));
        Assert.DoesNotContain("SECRET DRAFT", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET DRAFT", File.ReadAllText(Path.Combine(Output, "index.html")), StringComparison.Ordinal);
        Assert.Contains("blazor.webassembly.js", File.ReadAllText(Path.Combine(Output, "_markazor", "app.html")), StringComparison.Ordinal);
        Assert.Contains("markazor-reader.js", File.ReadAllText(Path.Combine(Output, "studio", "write", "index.html")), StringComparison.Ordinal);
        Assert.Single(document.QuerySelectorAll("script[src$='markazor-reader.js']"));
        Assert.Empty(document.QuerySelectorAll("a[href^='#']"));
        Assert.NotNull(document.QuerySelector("a[href='/posts/hello#mk-fn%3A1']"));
        Assert.NotNull(document.QuerySelector("a[href='/posts/hello#main-content']"));
        JsonNode configuration = JsonNode.Parse(File.ReadAllText(Path.Combine(Output, "staticwebapp.config.json")))!;
        Assert.Equal("/_markazor/app.html", configuration["navigationFallback"]!["rewrite"]!.GetValue<string>());
        Assert.Equal("DENY", configuration["globalHeaders"]!["X-Frame-Options"]!.GetValue<string>());
        Assert.Contains("https://example.test/posts/hello", File.ReadAllText(Path.Combine(Output, "sitemap.xml")), StringComparison.Ordinal);
        Assert.Equal("en", JsonNode.Parse(File.ReadAllText(Path.Combine(Output, "manifest.webmanifest")))!["lang"]!.GetValue<string>());
        Assert.Equal("never", configuration["trailingSlash"]!.GetValue<string>());
    }

    [Fact]
    public async Task GeneratesPaginationWithoutQueryDependentStaticPages()
    {
        for (int index = 0; index < 11; index++)
        {
            File.WriteAllText(Path.Combine(root, "posts", $"post-{index}.md"), $"---\ntitle: Post {index}\ntags: [shared]\n---\n\nBody {index}");
        }

        await StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken);
        Dictionary<string, string> routes = ReadRoutes();
        Assert.Contains("/page/2", routes.Keys);
        Assert.Contains("/posts/page/2", routes.Keys);
        Assert.Contains("/tags/shared/page/2", routes.Keys);
        using var document = new HtmlParser().ParseDocument(File.ReadAllText(Path.Combine(Output, routes["/posts/page/2"].TrimStart('/'))));
        Assert.Single(document.QuerySelectorAll(".markazor-reader-card"));
        Assert.Equal("/posts", document.QuerySelector("a[rel=prev]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task LoadsMarkdownWhoseFilenameContainsReservedUriCharacters()
    {
        File.WriteAllText(
            Path.Combine(root, "posts", "C# notes%.md"),
            "---\nslug: reserved-file\ntitle: Reserved file\n---\n\nReserved filename body.");

        await StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken);

        Dictionary<string, string> routes = ReadRoutes();
        string html = File.ReadAllText(Path.Combine(Output, routes["/posts/reserved-file"].TrimStart('/')));
        Assert.Contains("Reserved filename body.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Article unavailable", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaxonomyRoutesUseOneCanonicalCaseAcrossArticles()
    {
        File.WriteAllText(Path.Combine(root, "posts", "one.md"), "---\nslug: one\ntags: [FieldNotes]\ncategory: ReadingList\n---\nOne");
        File.WriteAllText(Path.Combine(root, "posts", "two.md"), "---\nslug: two\ntags: [fieldnotes]\ncategory: readinglist\n---\nTwo");

        await StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken);

        Dictionary<string, string> routes = ReadRoutes();
        Assert.Contains("/tags/fieldnotes", routes.Keys);
        Assert.DoesNotContain("/tags/FieldNotes", routes.Keys);
        Assert.Contains("/categories/readinglist", routes.Keys);
        Assert.DoesNotContain("/categories/ReadingList", routes.Keys);

        string firstArticle = File.ReadAllText(Path.Combine(Output, routes["/posts/one"].TrimStart('/')));
        string secondArticle = File.ReadAllText(Path.Combine(Output, routes["/posts/two"].TrimStart('/')));
        Assert.Contains("href=\"/tags/fieldnotes\"", firstArticle, StringComparison.Ordinal);
        Assert.Contains("href=\"/tags/fieldnotes\"", secondArticle, StringComparison.Ordinal);
        Assert.Contains("href=\"/categories/readinglist\"", firstArticle, StringComparison.Ordinal);
        Assert.Contains("href=\"/categories/readinglist\"", secondArticle, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("---\nslug: duplicate\ndraft: true\n---\nBody")]
    [InlineData("---\nslug: ../escape\n---\nBody")]
    public async Task RejectsInvalidPublishedContent(string markdown)
    {
        File.WriteAllText(Path.Combine(root, "posts", "invalid.md"), markdown);
        await Assert.ThrowsAsync<InvalidDataException>(() => StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateRoutesCannotSilentlyOverwriteArticles()
    {
        File.WriteAllText(Path.Combine(root, "posts", "one.md"), "---\nslug: same\n---\nOne");
        File.WriteAllText(Path.Combine(root, "posts", "two.md"), "---\nslug: same\n---\nTwo");
        await Assert.ThrowsAsync<InvalidDataException>(() => StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingBaseUrlDoesNotInventCanonicalUrls()
    {
        File.WriteAllText(Path.Combine(root, "public", "markazor.settings.json"), "{\"site\":{\"name\":\"Local site\"}}");
        await StaticSiteGenerator.GenerateAsync(root, Output, TestContext.Current.CancellationToken);
        using var document = new HtmlParser().ParseDocument(File.ReadAllText(Path.Combine(Output, "index.html")));
        Assert.Null(document.QuerySelector("link[rel=canonical]"));
        Assert.False(File.Exists(Path.Combine(Output, "sitemap.xml")));
        Assert.Equal("Local site", document.Title);
    }

    private Dictionary<string, string> ReadRoutes() => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Output, "_markazor", "routes.json")))!;

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
