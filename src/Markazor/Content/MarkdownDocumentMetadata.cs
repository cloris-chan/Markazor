namespace Markazor.Content;

public sealed record MarkdownDocumentMetadata(string Slug, string Title, string Summary, DateTimeOffset PublishedAtUtc, bool IsDraft, IReadOnlyList<string> Tags, string? Category, IReadOnlyList<string> AdditionalFrontMatterLines);
