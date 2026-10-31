using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Ganss.Xss;
using Markazor.Content;

namespace Markazor.Build;

internal static class ArticleHtml
{
    private static readonly SearchValues<char> CodeLanguageCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_+-#");
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static MarkdownRenderResult Prepare(string html)
    {
        using IHtmlDocument document = Sanitizer.SanitizeDom(html);
        IHtmlElement root = document.Body!;
        NormalizeClasses(root);
        Dictionary<string, string> anchors = new(StringComparer.Ordinal);
        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (IElement element in root.QuerySelectorAll("[id], h1, h2, h3, h4, h5, h6"))
        {
            string? oldId = element.GetAttribute("id");
            string slug = Slug(string.IsNullOrWhiteSpace(oldId) ? element.TextContent : oldId);
            string stem = "mk-" + slug;
            string id = stem;
            for (int suffix = 2; !identifiers.Add(id); suffix++)
            {
                id = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            }

            element.Id = id;
            anchors.TryAdd(string.IsNullOrWhiteSpace(oldId) ? slug : oldId, id);
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
        foreach (IElement heading in root.QuerySelectorAll("h1, h2, h3, h4, h5, h6"))
        {
            string text = heading.TextContent.Trim();
            int level = heading.LocalName[1] - '0';
            if (level <= 3)
            {
                headings.Add(new MarkdownHeading(heading.Id!, text, level));
            }
        }

        foreach (IElement checkbox in root.QuerySelectorAll("input"))
        {
            checkbox.SetAttribute("type", "checkbox");
            checkbox.SetAttribute("disabled", "disabled");
            checkbox.SetAttribute("aria-label", checkbox.ParentElement?.TextContent.Trim() ?? "Task");
            checkbox.Closest("li")?.ClassList.Add("task-list-item");
            if (checkbox.ParentElement is { LocalName: "p", ParentElement.LocalName: "li" } paragraph)
            {
                paragraph.Before(checkbox);
            }
        }

        int imageIndex = 0;
        foreach (IElement image in root.QuerySelectorAll("img"))
        {
            image.SetAttribute("decoding", "async");
            image.SetAttribute("loading", imageIndex++ == 0 || image.GetAttribute("loading") == "eager" ? "eager" : "lazy");
            NormalizeDimension(image, "width");
            NormalizeDimension(image, "height");
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
        using Stream stream = typeof(ArticleHtml).Assembly.GetManifestResourceStream("Markazor.MarkdownProfile.json")!;
        using JsonDocument profile = JsonDocument.Parse(stream);
        JsonElement html = profile.RootElement.GetProperty("html");
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(html.GetProperty("ALLOWED_TAGS").EnumerateArray().Select(static item => item.GetString()!));
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(html.GetProperty("ALLOWED_ATTR").EnumerateArray().Select(static item => item.GetString()!));
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }

    private static void NormalizeClasses(IElement root)
    {
        foreach (IElement element in root.QuerySelectorAll("[class]"))
        {
            bool token = element.LocalName == "span" && element.ClassList.Contains("token") && element.Closest("pre > code") is not null;
            foreach (string name in element.ClassList.ToArray())
            {
                bool language = element.LocalName == "code" && name.StartsWith("language-", StringComparison.Ordinal) && name.Length is > 9 and <= 41
                    && name.AsSpan(9).IndexOfAnyExcept(CodeLanguageCharacters) < 0;
                bool syntax = token && name is ("token" or "comment" or "keyword" or "tag" or "atrule" or "string" or "char" or "regex" or "number" or "boolean"
                    or "constant" or "function" or "class-name" or "attr-name" or "punctuation" or "operator" or "important" or "bold" or "italic"
                    or "prolog" or "doctype" or "cdata" or "attr-value" or "selector" or "builtin" or "property" or "symbol" or "entity" or "url" or "inserted" or "deleted" or "variable");
                if (!language && !syntax && name is not ("footnotes" or "footnote-ref" or "footnote-backref" or "task-list-item"))
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
