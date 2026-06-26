namespace Markazor.Content;

public sealed record MarkdownDocumentMetadata(string Slug, string Title, string Kind, string Summary, DateTimeOffset PublishedAtUtc, bool IsDraft, IReadOnlyList<string> Tags, string? Category, IReadOnlyList<string> AdditionalFrontMatterLines)
{
    public string Kind { get; init; } = MarkazorArticleKind.Normalize(Kind);
}
