using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Markazor.Content;

public sealed record MarkdownHeading(string Id, string Text, int Level);

public sealed record MarkdownRenderResult(string Html, ImmutableArray<MarkdownHeading> Headings)
{
    public static MarkdownRenderResult Empty { get; } = new(string.Empty, []);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MarkdownRenderResult))]
internal sealed partial class ArticleJsonContext : JsonSerializerContext;
