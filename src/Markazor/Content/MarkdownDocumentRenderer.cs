using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Ganss.Xss;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace Markazor.Content;

internal static class MarkdownDocumentRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).UseAdvancedExtensions().Build();
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static MarkdownRenderResult Render(string body, string? title)
    {
        using IHtmlDocument document = Sanitizer.SanitizeDom(Markdown.ToHtml(body, Pipeline));
        IHtmlElement root = document.Body!;
        if (root.FirstElementChild is { LocalName: "h1" } first && title is not null && string.Equals(first.TextContent.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            IElement anchor = document.CreateElement("span");
            anchor.Id = first.Id;
            first.Replace(anchor);
        }

        foreach (IElement heading in root.QuerySelectorAll("h1"))
        {
            IElement replacement = document.CreateElement("h2");
            foreach (IAttr attribute in heading.Attributes)
            {
                replacement.SetAttribute(attribute.Name, attribute.Value);
            }

            while (heading.FirstChild is { } child)
            {
                replacement.AppendChild(child);
            }

            heading.Replace(replacement);
        }

        NormalizeClasses(root);
        Dictionary<string, string> anchors = new(StringComparer.Ordinal);
        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (IElement element in root.QuerySelectorAll("[id], h2, h3, h4, h5, h6"))
        {
            string? oldId = element.GetAttribute("id");
            string stem = "mk-" + Slug(string.IsNullOrWhiteSpace(oldId) ? element.TextContent : oldId);
            string id = stem;
            for (int suffix = 2; !identifiers.Add(id); suffix++)
            {
                id = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            }

            element.Id = id;
            if (!string.IsNullOrWhiteSpace(oldId))
            {
                anchors.TryAdd(oldId, id);
            }
        }

        foreach (IElement link in root.QuerySelectorAll("a[href]"))
        {
            string href = link.GetAttribute("href")!;
            if (href.StartsWith('#') && anchors.TryGetValue(Uri.UnescapeDataString(href[1..]), out string? target))
            {
                link.SetAttribute("href", "#" + Uri.EscapeDataString(target));
            }

            if (string.Equals(link.GetAttribute("target"), "_blank", StringComparison.Ordinal))
            {
                link.SetAttribute("rel", "noopener noreferrer");
            }
        }

        ImmutableArray<MarkdownHeading>.Builder headings = ImmutableArray.CreateBuilder<MarkdownHeading>();
        foreach (IElement heading in root.QuerySelectorAll("h2, h3, h4, h5, h6"))
        {
            string text = heading.TextContent.Trim();
            int level = heading.LocalName[1] - '0';
            if (level <= 3)
            {
                headings.Add(new MarkdownHeading(heading.Id!, text, level));
            }

            IElement permalink = document.CreateElement("a");
            permalink.ClassName = "markazor-heading-link";
            permalink.SetAttribute("href", "#" + Uri.EscapeDataString(heading.Id!));
            permalink.SetAttribute("aria-label", "Permalink: " + text);
            permalink.TextContent = "#";
            heading.AppendChild(permalink);
        }

        foreach (IElement checkbox in root.QuerySelectorAll("input"))
        {
            checkbox.SetAttribute("type", "checkbox");
            checkbox.SetAttribute("disabled", "disabled");
            checkbox.SetAttribute("aria-label", checkbox.ParentElement?.TextContent.Trim() ?? "Task");
        }

        int imageIndex = 0;
        foreach (IElement image in root.QuerySelectorAll("img"))
        {
            image.SetAttribute("decoding", "async");
            image.SetAttribute("loading", imageIndex++ == 0 || image.GetAttribute("loading") == "eager" ? "eager" : "lazy");
            NormalizeDimension(image, "width");
            NormalizeDimension(image, "height");
            if (image.ParentElement is { LocalName: "p" } paragraph && paragraph.Children.Length == 1 && string.IsNullOrWhiteSpace(paragraph.TextContent)
                && image.GetAttribute("title") is { Length: > 0 } caption)
            {
                IElement figure = document.CreateElement("figure");
                paragraph.Replace(figure);
                figure.AppendChild(image);
                IElement figcaption = document.CreateElement("figcaption");
                figcaption.TextContent = caption;
                figure.AppendChild(figcaption);
            }
        }

        foreach (IElement table in root.QuerySelectorAll("table"))
        {
            IElement wrapper = document.CreateElement("div");
            wrapper.ClassName = "markazor-table-scroll";
            wrapper.SetAttribute("tabindex", "0");
            wrapper.SetAttribute("role", "region");
            wrapper.SetAttribute("aria-label", table.QuerySelector("caption")?.TextContent ?? "Table (scroll horizontally for more columns)");
            table.Replace(wrapper);
            wrapper.AppendChild(table);
            foreach (IElement cell in table.QuerySelectorAll("thead th"))
            {
                cell.SetAttribute("scope", "col");
            }
        }

        foreach (IElement pre in root.QuerySelectorAll("pre"))
        {
            pre.SetAttribute("tabindex", "0");
        }

        return new MarkdownRenderResult(root.InnerHtml, headings.ToImmutable());
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        HtmlSanitizer sanitizer = new();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "h1", "h2", "h3", "h4", "h5", "h6", "a", "ul", "ol", "li", "blockquote", "pre", "code", "strong", "em", "b", "i", "s", "del", "u", "sup", "sub", "br", "hr", "img", "figure", "figcaption", "table", "caption", "thead", "tbody", "tfoot", "tr", "th", "td", "dl", "dt", "dd", "details", "summary", "kbd", "abbr", "div", "span", "input", "ruby", "rt", "rp"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["id", "class", "href", "src", "alt", "title", "width", "height", "loading", "lang", "dir", "start", "colspan", "rowspan", "scope", "style", "type", "checked", "disabled", "open", "target", "rel"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedCssProperties.Add("text-align");
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }

    private static void NormalizeClasses(IElement root)
    {
        foreach (IElement element in root.QuerySelectorAll("[class]"))
        {
            foreach (string name in element.ClassList.ToArray())
            {
                bool language = element.LocalName == "code" && name.StartsWith("language-", StringComparison.Ordinal) && name.Length is > 9 and <= 41
                    && name.AsSpan(9).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_+-#".AsSpan()) < 0;
                if (!language && name is not ("footnotes" or "footnote-ref" or "footnote-back-ref" or "task-list-item" or "contains-task-list"))
                {
                    element.ClassList.Remove(name);
                }
            }

            if (element.ClassList.Length == 0)
            {
                element.RemoveAttribute("class");
            }
        }
    }

    private static void NormalizeDimension(IElement image, string attribute)
    {
        string? value = image.GetAttribute(attribute);
        if (value is not null && (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int dimension) || dimension is < 1 or > 32768))
        {
            image.RemoveAttribute(attribute);
        }
    }

    private static string Slug(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (Rune rune in value.Normalize().EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value is '_' or ':' or '-')
            {
                builder.Append(Rune.ToLowerInvariant(rune));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.Length == 0 ? "section" : builder.ToString().TrimEnd('-');
    }
}
