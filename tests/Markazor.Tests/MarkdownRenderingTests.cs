using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Markazor.Content;

namespace Markazor.Tests;

public sealed class MarkdownRenderingTests
{
    [Fact]
    public void RemovingDuplicateTitlePreservesLinksToItsAnchor()
    {
        MarkdownRenderResult result = MarkdownContent.Render("# Journal\n\n[Return to the opening](#journal)", "Journal");
        using IHtmlDocument document = Parse(result);
        Assert.Empty(document.QuerySelectorAll("h1"));
        Assert.Empty(result.Headings);
        Assert.NotNull(document.GetElementById("mk-journal"));
        Assert.Equal("#mk-journal", document.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void TitleBelongsToMetadataAndBodyHeadingsHaveStableUniqueAnchors()
    {
        MarkdownRenderResult result = MarkdownContent.Render("# Journal\n\n## Café notes\n\n### Details\n\n## Café notes\n\n# Another topic", "Journal");
        using IHtmlDocument document = Parse(result);
        Assert.Empty(document.QuerySelectorAll("h1"));
        Assert.Equal(["mk-café-notes", "mk-details", "mk-café-notes-1", "mk-another-topic"], result.Headings.Select(static heading => heading.Id));
        Assert.Equal([2, 3, 2, 2], result.Headings.Select(static heading => heading.Level));
        Assert.Equal(4, document.QuerySelectorAll("a.markazor-heading-link").Length);
        Assert.DoesNotContain("Journal", document.Body!.TextContent, StringComparison.Ordinal);
        Assert.All(document.QuerySelectorAll("a.markazor-heading-link"), static link => Assert.False(string.IsNullOrWhiteSpace(link.GetAttribute("aria-label"))));
    }

    [Fact]
    public void FootnotesAndTheirBacklinksKeepMatchingTargets()
    {
        MarkdownRenderResult result = MarkdownContent.Render("## Notes\n\nA fact.[^source] Another reference.[^source]\n\n[^source]: A source with **detail**.");
        using IHtmlDocument document = Parse(result);
        Assert.NotEmpty(document.QuerySelectorAll(".footnotes"));
        Assert.NotEmpty(document.QuerySelectorAll(".footnote-back-ref"));
        foreach (IElement link in document.QuerySelectorAll("a[href^='#']"))
        {
            string target = Uri.UnescapeDataString(link.GetAttribute("href")![1..]);
            Assert.NotNull(document.GetElementById(target));
            Assert.StartsWith("mk-", target, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreservesCodeLanguagesWhileRemovingActiveHtmlAndSiteClasses()
    {
        const string Markdown = """
            ```csharp
            var html = "<script>not markup</script>";
            ```

            <div id="top" class="site-header markazor-prose" style="position:fixed;z-index:9999;color:red" onclick="alert(1)">Text</div>
            <script>alert('xss')</script>
            <iframe src="https://example.test"></iframe>
            <form action="https://example.test"><input name="password" type="password"></form>
            <a href="javascript:alert(1)">unsafe</a>
            <a href="https://example.test" target="_blank">external</a>
            <img src="x" onerror="alert(1)">
            """;
        using IHtmlDocument document = Parse(MarkdownContent.Render(Markdown));
        Assert.NotNull(document.QuerySelector("pre code.language-csharp"));
        Assert.Contains("<script>not markup</script>", document.QuerySelector("pre code")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(document.QuerySelectorAll("script, iframe, form, [onclick], [onerror], .site-header, [name=password]"));
        Assert.Null(document.GetElementById("top"));
        Assert.NotNull(document.GetElementById("mk-top"));
        Assert.DoesNotContain("javascript:", document.Body!.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", document.Body.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("noopener noreferrer", document.QuerySelector("a[target='_blank']")!.GetAttribute("rel"));
    }

    [Fact]
    public void ImageCaptionsDimensionsAndLoadingSurviveSanitization()
    {
        const string Markdown = """
            ![Book](/assets/book.png "A quiet reading desk"){width=640 height=480}

            <p><img src="/assets/second.png" alt="Second" width="-2" height="999999" title="Another view"></p>
            """;
        using IHtmlDocument document = Parse(MarkdownContent.Render(Markdown));
        IElement first = document.QuerySelectorAll("img")[0];
        IElement second = document.QuerySelectorAll("img")[1];
        Assert.Equal("640", first.GetAttribute("width"));
        Assert.Equal("480", first.GetAttribute("height"));
        Assert.Equal("eager", first.GetAttribute("loading"));
        Assert.Equal("async", first.GetAttribute("decoding"));
        Assert.Equal("lazy", second.GetAttribute("loading"));
        Assert.Null(second.GetAttribute("width"));
        Assert.Null(second.GetAttribute("height"));
        Assert.Equal("A quiet reading desk", document.QuerySelector("figcaption")!.TextContent);
    }

    [Fact]
    public void TablesRetainAlignmentAndKeyboardAccessibleScrollRegion()
    {
        using IHtmlDocument document = Parse(MarkdownContent.Render("| Name | Amount |\n| :--- | ---: |\n| Example | 100 |"));
        IElement region = document.QuerySelector(".markazor-table-scroll")!;
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.NotNull(region.GetAttribute("aria-label"));
        Assert.Contains("right", document.QuerySelectorAll("th")[1].GetAttribute("style")!, StringComparison.Ordinal);
        Assert.All(document.QuerySelectorAll("thead th"), static cell => Assert.Equal("col", cell.GetAttribute("scope")));
    }

    [Fact]
    public void TaskCheckboxesAreReadOnlyAndNamed()
    {
        using IHtmlDocument document = Parse(MarkdownContent.Render("- [x] Finished\n- [ ] Pending"));
        Assert.Equal(2, document.QuerySelectorAll("input[type='checkbox'][disabled]").Length);
        Assert.All(document.QuerySelectorAll("input"), static input => Assert.False(string.IsNullOrWhiteSpace(input.GetAttribute("aria-label"))));
    }

    [Fact]
    public async Task ConcurrentRendersDoNotShareDocumentState()
    {
        Task<MarkdownRenderResult>[] renders = Enumerable.Range(0, 24).Select(index => Task.Run(() => MarkdownContent.Render($"## Section {index}\n\nText.[^one]\n\n[^one]: Source {index}"), TestContext.Current.CancellationToken)).ToArray();
        MarkdownRenderResult[] results = await Task.WhenAll(renders);
        for (int index = 0; index < results.Length; index++)
        {
            Assert.Equal("Section " + index, Assert.Single(results[index].Headings).Text);
        }
    }

    private static IHtmlDocument Parse(MarkdownRenderResult result) => new HtmlParser().ParseDocument(result.Html);
}
