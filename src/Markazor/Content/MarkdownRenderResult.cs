using System.Collections.Immutable;

namespace Markazor.Content;

public sealed record MarkdownHeading(string Id, string Text, int Level);

public sealed record MarkdownRenderResult(string Html, ImmutableArray<MarkdownHeading> Headings)
{
    public static MarkdownRenderResult Empty { get; } = new(string.Empty, []);
}
