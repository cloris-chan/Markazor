using System.Globalization;
using Ganss.Xss;
using Markdig;

namespace Markazor.Content;

public static class MarkdownContent
{
    private const string DraftsSegment = "drafts";
    private const string NotesSegment = "notes";
    private const string PostsSegment = "posts";

    private static readonly char SitePathSeparator = Path.AltDirectorySeparatorChar;
    private static readonly string SitePathSeparatorText = SitePathSeparator.ToString();
    private static readonly DateTimeOffset UnixEpoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> KnownFrontMatterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "category",
        "date",
        "draft",
        "kind",
        "publishedAt",
        "slug",
        "summary",
        "tags",
        "title",
    };

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly HtmlSanitizer Sanitizer = new();

    public static MarkdownDocument SplitDocument(string relativePath, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(markdown);

        FrontMatterDocument split = SplitFrontMatter(markdown);
        ArticleMeta article = ParseArticleMeta(relativePath, markdown);

        MarkdownDocumentMetadata documentMetadata = new(article.Slug, article.Title, article.Kind, article.Summary, article.PublishedAtUtc, article.IsDraft, article.Tags, article.Category, GetAdditionalFrontMatterLines(split.FrontMatterLines));

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
            "kind: " + MarkazorArticleKind.Normalize(metadata.Kind),
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

        FrontMatterDocument split = SplitFrontMatter(markdown);

        return split.HasFrontMatter ? split.Body.Trim() : markdown.Trim();
    }

    public static string ToSafeHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string body = StripFrontMatter(markdown);
        string html = Markdown.ToHtml(body, Pipeline);

        return Sanitizer.Sanitize(html);
    }

    public static string MarkAsPublished(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalized.Split('\n');

        if (lines.Length < 2 || !string.Equals(lines[0], "---", StringComparison.Ordinal))
        {
            return markdown;
        }

        for (int index = 1; index < lines.Length; index++)
        {
            if (string.Equals(lines[index], "---", StringComparison.Ordinal))
            {
                return string.Join('\n', InsertDraftFlag(lines, index));
            }

            int separatorIndex = lines[index].IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex > 0 && string.Equals(lines[index][..separatorIndex].Trim(), "draft", StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = "draft: false";

                return string.Join('\n', lines);
            }
        }

        return markdown;
    }

    public static ArticleMeta ParseArticleMeta(string relativePath, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(markdown);

        string normalizedPath = NormalizeSitePath(relativePath);
        Dictionary<string, string> metadata = ParseFrontMatter(markdown);
        string slug = Get(metadata, "slug") ?? Path.GetFileNameWithoutExtension(normalizedPath);
        string kind = InferKind(normalizedPath, Get(metadata, "kind"));
        string title = Get(metadata, "title") ?? slug;
        string summary = Get(metadata, "summary") ?? string.Empty;
        string? category = Get(metadata, "category");
        DateTimeOffset publishedAtUtc = ParsePublishedAt(Get(metadata, "publishedAt") ?? Get(metadata, "date"));
        string[] tags = ParseTags(Get(metadata, "tags"));
        bool isDraft = bool.TryParse(Get(metadata, "draft"), out bool draft) ? draft : IsUnderRootSegment(normalizedPath, DraftsSegment);

        return new ArticleMeta(slug, title, summary, publishedAtUtc, tags, category, normalizedPath, CreateRoute(kind, slug), isDraft, kind);
    }

    public static bool TryCreatePublishedPathFromDraft(string draftPath, string markdown, string draftRoot, string postRoot, string noteRoot, out string? publishedPath)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        publishedPath = null;

        if (string.IsNullOrWhiteSpace(draftPath) || string.IsNullOrWhiteSpace(draftRoot) || string.IsNullOrWhiteSpace(postRoot) || string.IsNullOrWhiteSpace(noteRoot))
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

        Dictionary<string, string> metadata = ParseFrontMatter(markdown);
        string kind = Get(metadata, "kind") is { } frontMatterKind
            ? MarkazorArticleKind.Normalize(frontMatterKind)
            : MarkazorArticleKind.Post;
        string publishedRoot = string.Equals(kind, MarkazorArticleKind.Note, StringComparison.Ordinal) ? NormalizeSiteRoot(noteRoot) : NormalizeSiteRoot(postRoot);

        publishedPath = publishedRoot + suffix;

        return true;
    }

    private static string[] InsertDraftFlag(string[] lines, int closingFrontMatterIndex)
    {
        string[] result = new string[lines.Length + 1];
        Array.Copy(lines, result, closingFrontMatterIndex);
        result[closingFrontMatterIndex] = "draft: false";
        Array.Copy(lines, closingFrontMatterIndex, result, closingFrontMatterIndex + 1, lines.Length - closingFrontMatterIndex);

        return result;
    }

    private static Dictionary<string, string> ParseFrontMatter(string markdown)
    {
        return ParseFrontMatterLines(SplitFrontMatter(markdown).FrontMatterLines);
    }

    private static Dictionary<string, string> ParseFrontMatterLines(IReadOnlyList<string> lines)
    {
        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines)
        {
            int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = UnquoteFrontMatterScalar(line[(separatorIndex + 1)..].Trim());
            if (key.Length > 0)
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private static FrontMatterDocument SplitFrontMatter(string markdown)
    {
        string normalized = NormalizeMarkdownLineEndings(markdown);
        string[] lines = normalized.Split('\n');

        if (lines.Length < 2 || !string.Equals(lines[0], "---", StringComparison.Ordinal))
        {
            return new FrontMatterDocument([], markdown, HasFrontMatter: false);
        }

        for (int index = 1; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index], "---", StringComparison.Ordinal))
            {
                continue;
            }

            string[] frontMatterLines = lines[1..index];
            string body = string.Join('\n', lines[(index + 1)..]).TrimStart('\n');

            return new FrontMatterDocument(frontMatterLines, body, HasFrontMatter: true);
        }

        return new FrontMatterDocument([], markdown, HasFrontMatter: false);
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

    private static string NormalizeMarkdownLineEndings(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static string UnquoteFrontMatterScalar(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2
            && trimmed[0] == '"'
            && trimmed[^1] == '"'
                ? trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
                : trimmed.Trim('"');
    }

    private static string FormatFrontMatterScalar(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "\"\""
            : value.Trim();
    }

    private static string FormatQuotedFrontMatterScalar(string? value)
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

    private static string? Get(Dictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static DateTimeOffset ParsePublishedAt(string? value)
    {
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset publishedAt)
            ? publishedAt
            : UnixEpoch;
    }

    private static string[] ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return
        [
            .. value.Trim().Trim('[', ']')
                .Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static tag => tag.Trim('"', '\''))
                .Where(static tag => tag.Length > 0),
        ];
    }

    private static string InferKind(string normalizedPath, string? frontMatterKind)
    {
        if (IsUnderRootSegment(normalizedPath, PostsSegment))
        {
            return MarkazorArticleKind.Post;
        }

        return IsUnderRootSegment(normalizedPath, NotesSegment)
            ? MarkazorArticleKind.Note
            : frontMatterKind is not null
                ? MarkazorArticleKind.Normalize(frontMatterKind)
                : MarkazorArticleKind.Post;
    }

    private static bool IsUnderRootSegment(string normalizedPath, string rootSegment)
    {
        string root = string.Concat(rootSegment, SitePathSeparatorText);

        return normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRoute(string kind, string slug)
    {
        string routeRoot = string.Equals(kind, MarkazorArticleKind.Note, StringComparison.Ordinal)
            ? NotesSegment
            : PostsSegment;

        return string.Concat(SitePathSeparatorText, CombineSitePath(routeRoot, slug));
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

    private sealed record FrontMatterDocument(IReadOnlyList<string> FrontMatterLines, string Body, bool HasFrontMatter);
}
