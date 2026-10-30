using System.Globalization;

namespace Markazor.Content;

public static class MarkdownContent
{
    private const string DraftsSegment = "drafts";
    private const string PostsSegment = "posts";

    private static readonly char SitePathSeparator = Path.AltDirectorySeparatorChar;
    private static readonly string SitePathSeparatorText = SitePathSeparator.ToString();
    private static readonly HashSet<string> KnownFrontMatterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "category",
        "date",
        "draft",
        "publishedAt",
        "slug",
        "summary",
        "tags",
        "title",
    };

    public static MarkdownDocument SplitDocument(string relativePath, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(markdown);

        MarkdownFrontMatter split = MarkdownFrontMatter.Parse(markdown);
        ArticleMeta article = ParseArticleMeta(relativePath, split);

        MarkdownDocumentMetadata documentMetadata = new(article.Slug, article.Title, article.Summary, article.PublishedAtUtc, article.IsDraft, article.Tags, article.Category, GetAdditionalFrontMatterLines(split.Lines));

        return new MarkdownDocument(documentMetadata, split.HasFrontMatter ? split.Body : markdown);
    }

    public static string ComposeDocument(MarkdownDocumentMetadata metadata, string body)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(body);

        List<string> lines =
        [
            "---",
            "slug: " + FormatFrontMatterScalar(metadata.Slug),
            "title: " + FormatQuotedFrontMatterScalar(metadata.Title),
            "summary: " + FormatQuotedFrontMatterScalar(metadata.Summary),
            "publishedAt: " + FormatPublishedAt(metadata.PublishedAtUtc),
            "draft: " + (metadata.IsDraft ? "true" : "false"),
            "tags: " + FormatTags(metadata.Tags),
        ];

        if (!string.IsNullOrWhiteSpace(metadata.Category))
        {
            lines.Add("category: " + FormatQuotedFrontMatterScalar(metadata.Category));
        }

        foreach (string line in metadata.AdditionalFrontMatterLines)
        {
            if (!IsKnownFrontMatterLine(line))
            {
                lines.Add(line);
            }
        }

        lines.Add("---");
        lines.Add(string.Empty);
        lines.Add(body.TrimStart('\r', '\n'));

        return string.Join('\n', lines);
    }

    public static string StripFrontMatter(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        MarkdownFrontMatter split = MarkdownFrontMatter.Parse(markdown);

        return split.HasFrontMatter ? split.Body.Trim() : markdown.Trim();
    }

    public static MarkdownRenderResult Render(string markdown, string? articleTitle = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        MarkdownFrontMatter document = MarkdownFrontMatter.Parse(markdown);
        return MarkdownDocumentRenderer.Render(document.Body.Trim(), articleTitle ?? document.Get("title"));
    }

    public static string MarkAsPublished(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        MarkdownFrontMatter document = MarkdownFrontMatter.Parse(markdown);
        if (!document.HasFrontMatter)
        {
            return markdown;
        }

        List<string> lines = ["---"];
        foreach (string line in document.Lines)
        {
            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || !string.Equals(line[..separator].Trim(), "draft", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(line);
            }
        }

        lines.AddRange(["draft: false", "---", string.Empty, document.Body]);
        return string.Join('\n', lines);
    }

    public static ArticleMeta ParseArticleMeta(string relativePath, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(markdown);

        return ParseArticleMeta(relativePath, MarkdownFrontMatter.Parse(markdown));
    }

    private static ArticleMeta ParseArticleMeta(string relativePath, MarkdownFrontMatter metadata)
    {
        string normalizedPath = NormalizeSitePath(relativePath);
        string slug = metadata.Get("slug") ?? Path.GetFileNameWithoutExtension(normalizedPath);
        string title = metadata.Get("title") ?? slug;
        string summary = metadata.Get("summary") ?? string.Empty;
        string? category = metadata.Get("category");
        DateTimeOffset publishedAtUtc = metadata.PublishedAtUtc;
        string[] tags = metadata.GetTags();
        bool isDraft = bool.TryParse(metadata.Get("draft"), out bool draft) ? draft : IsUnderRootSegment(normalizedPath, DraftsSegment);

        return new ArticleMeta(slug, title, summary, publishedAtUtc, tags, category, normalizedPath, CreateRoute(slug), isDraft);
    }

    public static bool TryCreatePublishedPathFromDraft(string draftPath, string draftRoot, string postRoot, out string? publishedPath)
    {
        publishedPath = null;

        if (string.IsNullOrWhiteSpace(draftPath) || string.IsNullOrWhiteSpace(draftRoot) || string.IsNullOrWhiteSpace(postRoot))
        {
            return false;
        }

        string normalizedDraftPath = NormalizeSitePath(draftPath);
        string normalizedDraftRoot = NormalizeSiteRoot(draftRoot);
        if (!normalizedDraftPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || !normalizedDraftPath.StartsWith(normalizedDraftRoot, StringComparison.Ordinal))
        {
            return false;
        }

        string suffix = normalizedDraftPath[normalizedDraftRoot.Length..];
        if (suffix.Length == 0)
        {
            return false;
        }

        if (suffix.Contains(SitePathSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        publishedPath = NormalizeSiteRoot(postRoot) + suffix;

        return true;
    }

    private static IReadOnlyList<string> GetAdditionalFrontMatterLines(IReadOnlyList<string> lines)
    {
        return
        [
            .. lines.Where(static line => !IsKnownFrontMatterLine(line)),
        ];
    }

    private static bool IsKnownFrontMatterLine(string line)
    {
        int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return false;
        }

        return KnownFrontMatterKeys.Contains(line[..separatorIndex].Trim());
    }

    private static string FormatFrontMatterScalar(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "\"\""
            : value.Trim();
    }

    internal static string FormatQuotedFrontMatterScalar(string? value)
    {
        string normalized = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return "\"" + normalized + "\"";
    }

    private static string FormatPublishedAt(DateTimeOffset value)
    {
        DateTimeOffset utcValue = value.ToUniversalTime();
        return utcValue.TimeOfDay == TimeSpan.Zero
            ? utcValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : utcValue.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatTags(IReadOnlyList<string> tags)
    {
        return tags.Count == 0 ? "[]" : "[" + string.Join(", ", tags.Select(FormatQuotedFrontMatterScalar)) + "]";
    }

    private static bool IsUnderRootSegment(string normalizedPath, string rootSegment)
    {
        string root = string.Concat(rootSegment, SitePathSeparatorText);

        return normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRoute(string slug)
    {
        return string.Concat(SitePathSeparatorText, CombineSitePath(PostsSegment, Uri.EscapeDataString(slug)));
    }

    private static string NormalizeSitePath(string path)
    {
        return string.Join(SitePathSeparatorText, path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeSiteRoot(string path)
    {
        return NormalizeSitePath(path).TrimEnd(SitePathSeparator) + SitePathSeparatorText;
    }

    private static string CombineSitePath(params string[] segments)
    {
        return string.Join(SitePathSeparatorText, segments);
    }
}
