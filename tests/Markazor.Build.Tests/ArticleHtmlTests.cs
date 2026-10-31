using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Markazor.Build.Tests;

public sealed class ArticleHtmlTests
{
    [Fact]
    public void KeepsBodyHeadingsAndAssignsUniqueAnchors()
    {
        var result = ArticleHtml.Prepare("<h1>Journal</h1><h2>Café notes</h2><h3>Details</h3><h2>Café notes</h2><a href='#café-notes'>Jump</a>");
        using var document = new HtmlParser().ParseDocument(result.Html);
        Assert.Equal("Journal", document.QuerySelector("h1")!.TextContent);
        Assert.Equal(["mk-journal", "mk-café-notes", "mk-details", "mk-café-notes-2"], result.Headings.Select(static heading => heading.Id));
        Assert.Equal("#mk-caf%C3%A9-notes", document.Body!.LastElementChild!.GetAttribute("href"));
    }

    [Fact]
    public void RemovesActiveHtmlAndSiteClassesWhileKeepingPrismTokens()
    {
        const string Html = """
            <pre><code class="language-csharp"><span class="token keyword site-header">var</span> html = "&lt;script&gt;not markup&lt;/script&gt;";</code></pre>
            <div id="top" class="site-header markazor-prose" style="position:fixed;z-index:9999;color:red" onclick="alert(1)">Text</div>
            <script>alert('xss')</script><iframe src="https://example.test"></iframe>
            <form action="https://example.test"><input name="password" type="password"></form>
            <a href="javascript:alert(1)">unsafe</a><a href="https://example.test" target="_blank">external</a>
            <img src="x" onerror="alert(1)">
            """;
        using var document = new HtmlParser().ParseDocument(ArticleHtml.Prepare(Html).Html);
        Assert.NotNull(document.QuerySelector("pre code.language-csharp .token.keyword"));
        Assert.Contains("<script>not markup</script>", document.QuerySelector("code")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(document.QuerySelectorAll("script, iframe, form, [onclick], [onerror], .site-header, [name=password]"));
        Assert.NotNull(document.GetElementById("mk-top"));
        Assert.DoesNotContain("javascript:", document.Body!.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", document.Body.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("noopener noreferrer", document.QuerySelector("a[target='_blank']")!.GetAttribute("rel"));
    }

    [Fact]
    public void NamespacesRepeatedFootnotesWithoutDuplicateIds()
    {
        const string Html = "<sup><a id='fnref-1' href='#fn-1'>1</a></sup><sup><a id='fnref-1' href='#fn-1'>1</a></sup><section class='footnotes'><ol><li id='fn-1'>Source <a class='footnote-backref' href='#fnref-1'>↩</a></li></ol></section>";
        using var document = new HtmlParser().ParseDocument(ArticleHtml.Prepare(Html).Html);
        Assert.NotNull(document.QuerySelector(".footnotes .footnote-backref"));
        string[] ids = document.QuerySelectorAll("[id]").Select(static node => node.Id!).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(document.QuerySelectorAll("a[href^='#']"), link => Assert.NotNull(document.GetElementById(Uri.UnescapeDataString(link.GetAttribute("href")![1..]))));
    }

    [Fact]
    public void PreservesImagesAndTablesWithoutInventingCaptions()
    {
        const string Html = "<p><img src='/assets/book.png' width='640' height='480' title='A desk'></p><img src='/assets/second.png' width='-2' height='999999'><table><thead><tr><th align='right'>Amount</th></tr></thead><tbody><tr><td>100</td></tr></tbody></table><ul><li><input type='checkbox' checked>Done</li></ul>";
        using var document = new HtmlParser().ParseDocument(ArticleHtml.Prepare(Html).Html);
        IElement[] images = document.QuerySelectorAll("img").ToArray();
        Assert.Equal("640", images[0].GetAttribute("width"));
        Assert.Equal("480", images[0].GetAttribute("height"));
        Assert.Equal("eager", images[0].GetAttribute("loading"));
        Assert.Equal("lazy", images[1].GetAttribute("loading"));
        Assert.Null(images[1].GetAttribute("width"));
        Assert.Null(images[1].GetAttribute("height"));
        Assert.Empty(document.QuerySelectorAll("figcaption"));
        Assert.NotNull(document.QuerySelector(".markazor-table-scroll[tabindex='0'][role='region']"));
        Assert.NotNull(document.QuerySelector("th[align='right'][scope='col']"));
        Assert.NotNull(document.QuerySelector(".task-list-item input[disabled][aria-label='Done']"));
    }
}
