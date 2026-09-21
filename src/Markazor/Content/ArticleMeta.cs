namespace Markazor.Content;

public sealed record ArticleMeta(string Slug, string Title, string Summary, DateTimeOffset PublishedAtUtc, IReadOnlyList<string> Tags, string? Category, string RelativePath, string Route, bool IsDraft, string Kind = MarkazorArticleKind.Post, string? ContentPath = null)
{
    public string Kind { get; init; } = MarkazorArticleKind.Normalize(Kind);

    public string ContentPath { get; init; } = ContentPath ?? CreateContentPath(RelativePath);

    private static string CreateContentPath(string relativePath)
    {
        string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        string escapedPath = EscapePath(normalizedPath);

        if (normalizedPath.StartsWith("posts/", StringComparison.Ordinal) || normalizedPath.StartsWith("notes/", StringComparison.Ordinal))
        {
            return "/_markazor/content/" + escapedPath;
        }

        return "/" + escapedPath;
    }

    private static string EscapePath(string path)
    {
        string[] segments = path.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.EscapeDataString(segments[index]);
        }

        return string.Join("/", segments);
    }
}
