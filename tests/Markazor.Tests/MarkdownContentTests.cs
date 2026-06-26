using Markazor.Content;

namespace Markazor.Tests;

public sealed class MarkdownContentTests
{
    [Fact]
    public void StripFrontMatterReturnsBodyOnly()
    {
        const string Markdown = """
            ---
            title: Hello
            ---

            # Hello
            """;

        string body = MarkdownContent.StripFrontMatter(Markdown);

        Assert.Equal("# Hello", body);
    }

    [Fact]
    public void ToSafeHtmlRendersMarkdownAndRemovesUnsafeHtml()
    {
        const string Markdown = """
            ---
            title: Hello
            ---

            Hello **world**

            <script>alert('xss')</script>
            <a href="javascript:alert('xss')" onclick="alert('xss')">bad</a>
            """;

        string html = MarkdownContent.ToSafeHtml(Markdown);

        Assert.Contains("<strong>world</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkAsPublishedReplacesDraftFlag()
    {
        const string Markdown = """
            ---
            title: Draft
            draft: true
            ---

            Body
            """;

        string published = MarkdownContent.MarkAsPublished(Markdown);

        Assert.Contains("draft: false", published, StringComparison.Ordinal);
        Assert.DoesNotContain("draft: true", published, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkAsPublishedAddsDraftFlagWhenFrontMatterExists()
    {
        const string Markdown = """
            ---
            title: Draft
            ---

            Body
            """;

        string published = MarkdownContent.MarkAsPublished(Markdown);

        Assert.Contains("draft: false", published, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitDocumentReturnsTypedMetadataAndBodyOnly()
    {
        const string Markdown = """
            ---
            slug: hello-note
            title: "Hello Note"
            kind: post
            summary: "Short summary"
            publishedAt: 2026-06-06T12:30:00Z
            draft: true
            tags: [dotnet, "blazor"]
            category: "Ideas"
            custom: keep
            ---

            # Body
            """;

        MarkdownDocument document = MarkdownContent.SplitDocument(
            ContentPath("drafts", "hello-note.md"),
            Markdown);

        Assert.Equal("# Body", document.Body);
        Assert.Equal("hello-note", document.Metadata.Slug);
        Assert.Equal("Hello Note", document.Metadata.Title);
        Assert.Equal(MarkazorArticleKind.Post, document.Metadata.Kind);
        Assert.Equal("Short summary", document.Metadata.Summary);
        Assert.True(document.Metadata.IsDraft);
        Assert.Equal(["dotnet", "blazor"], document.Metadata.Tags);
        Assert.Equal("Ideas", document.Metadata.Category);
        Assert.Equal(["custom: keep"], document.Metadata.AdditionalFrontMatterLines);
    }

    [Fact]
    public void ComposeDocumentWritesFrontMatterAndPreservesUnknownLines()
    {
        MarkdownDocumentMetadata metadata = new(
            "hello",
            "Hello \"World\"",
            MarkazorArticleKind.Note,
            "Summary",
            new DateTimeOffset(2026, 6, 6, 12, 30, 0, TimeSpan.Zero),
            IsDraft: true,
            ["dotnet", "blazor"],
            "Ideas",
            ["custom: keep"]);

        string markdown = MarkdownContent.ComposeDocument(metadata, "# Body");

        Assert.Contains("slug: hello", markdown, StringComparison.Ordinal);
        Assert.Contains("title: \"Hello \\\"World\\\"\"", markdown, StringComparison.Ordinal);
        Assert.Contains("kind: note", markdown, StringComparison.Ordinal);
        Assert.Contains("publishedAt: 2026-06-06T12:30:00Z", markdown, StringComparison.Ordinal);
        Assert.Contains("draft: true", markdown, StringComparison.Ordinal);
        Assert.Contains("tags: [\"dotnet\", \"blazor\"]", markdown, StringComparison.Ordinal);
        Assert.Contains("category: \"Ideas\"", markdown, StringComparison.Ordinal);
        Assert.Contains("custom: keep", markdown, StringComparison.Ordinal);
        Assert.EndsWith("# Body", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseArticleMetaUsesFrontMatterAndPathDefaults()
    {
        const string Markdown = """
            ---
            title: "Runtime title"
            summary: Updated
            publishedAt: 2026-06-05
            draft: true
            tags: [one, two]
            ---

            Body
            """;

        ArticleMeta article = MarkdownContent.ParseArticleMeta(
            "drafts/runtime.md",
            Markdown);

        Assert.Equal("runtime", article.Slug);
        Assert.Equal("Runtime title", article.Title);
        Assert.Equal(["one", "two"], article.Tags);
        Assert.True(article.IsDraft);
        Assert.Equal(RoutePath("posts", "runtime"), article.Route);
        Assert.Equal(MarkazorArticleKind.Post, article.Kind);
    }

    [Fact]
    public void ParseArticleMetaInfersNoteKindAndRouteFromRoot()
    {
        const string Markdown = """
            ---
            title: "Runtime note"
            publishedAt: 2026-06-06
            ---

            Body
            """;

        ArticleMeta article = MarkdownContent.ParseArticleMeta(
            ContentPath("notes", "runtime-note.md"),
            Markdown);

        Assert.Equal("runtime-note", article.Slug);
        Assert.Equal("Runtime note", article.Title);
        Assert.False(article.IsDraft);
        Assert.Equal(RoutePath("notes", "runtime-note"), article.Route);
        Assert.Equal(MarkazorArticleKind.Note, article.Kind);
    }

    [Fact]
    public void ParseArticleMetaAllowsDraftsToDeclareNoteKind()
    {
        const string Markdown = """
            ---
            title: "Draft note"
            kind: note
            ---

            Body
            """;

        ArticleMeta article = MarkdownContent.ParseArticleMeta(
            ContentPath("drafts", "draft-note.md"),
            Markdown);

        Assert.True(article.IsDraft);
        Assert.Equal(RoutePath("notes", "draft-note"), article.Route);
        Assert.Equal(MarkazorArticleKind.Note, article.Kind);
    }

    [Fact]
    public void ParseArticleMetaUsesDraftFrontMatterKindForFlatDrafts()
    {
        const string Markdown = """
            ---
            title: "Draft note"
            kind: note
            ---

            Body
            """;

        ArticleMeta article = MarkdownContent.ParseArticleMeta(
            ContentPath("drafts", "draft-note.md"),
            Markdown);

        Assert.True(article.IsDraft);
        Assert.Equal(RoutePath("notes", "draft-note"), article.Route);
        Assert.Equal(MarkazorArticleKind.Note, article.Kind);
    }

    [Fact]
    public void TryCreatePublishedPathFromDraftUsesFrontMatterKind()
    {
        bool mapped = MarkdownContent.TryCreatePublishedPathFromDraft(
            ContentPath("drafts", "draft-note.md"),
            """
            ---
            kind: note
            ---
            """,
            ContentPath("drafts"),
            ContentPath("posts"),
            ContentPath("notes"),
            out string? publishedPath);

        Assert.True(mapped);
        Assert.Equal(ContentPath("notes", "draft-note.md"), publishedPath);
    }

    [Fact]
    public void TryCreatePublishedPathFromDraftRejectsNestedDraftFolders()
    {
        bool mapped = MarkdownContent.TryCreatePublishedPathFromDraft(
            ContentPath("drafts", "notes", "idea.md"),
            """
            ---
            kind: note
            ---
            """,
            ContentPath("drafts"),
            ContentPath("posts"),
            ContentPath("notes"),
            out string? publishedPath);

        Assert.False(mapped);
        Assert.Null(publishedPath);
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
}
