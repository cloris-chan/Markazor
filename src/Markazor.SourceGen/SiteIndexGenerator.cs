using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Markazor.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class SiteIndexGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor PublishedDraftDescriptor = new(
        id: "MZ001",
        title: "Draft content cannot be published",
        messageFormat: "Published content '{0}' declares 'draft: true'. Move it to drafts/ or remove the draft flag.",
        category: "Markazor.Content",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<MarkdownArticleResult> results = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (source, cancellationToken) => MarkdownArticle.Parse(
                source.Left,
                source.Right.GetOptions(source.Left),
                cancellationToken));

        IncrementalValueProvider<ImmutableArray<MarkdownArticleResult>> collectedResults = results.Collect();
        IncrementalValueProvider<GeneratedSiteSettings> siteSettings = context.AdditionalTextsProvider
            .Where(static file => MarkazorSiteSettingsFile.IsSettingsFile(file.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (source, cancellationToken) => GeneratedSiteSettings.Parse(
                source.Left,
                source.Right.GetOptions(source.Left),
                cancellationToken))
            .Collect()
            .Select(static (settings, _) => settings.Length == 0
                ? GeneratedSiteSettings.Empty
                : settings
                    .OrderBy(static setting => setting.Path, StringComparer.Ordinal)
                    .First());
        context.RegisterSourceOutput(
            collectedResults.Combine(siteSettings),
            static (productionContext, source) =>
            {
                ImmutableArray<MarkdownArticleResult> sourceResults = source.Left;
                foreach (MarkdownArticleResult result in sourceResults)
                {
                    if (result.HasPublishedDraftFlag)
                    {
                        productionContext.ReportDiagnostic(Diagnostic.Create(
                            PublishedDraftDescriptor,
                            Location.None,
                            result.Path));
                    }
                }

                ImmutableArray<MarkdownArticle> sourceArticles =
                [
                    .. sourceResults
                        .Where(static result => result.Article is not null)
                        .Select(static result => result.Article!),
                ];
                string generatedSource = SiteIndexSourceWriter.Write(sourceArticles, source.Right);
                productionContext.AddSource("SiteIndex.g.cs", SourceText.From(generatedSource, Encoding.UTF8));
            });
    }
}

internal sealed class GeneratedSiteSettings(string path, string name, string description)
{
    public static GeneratedSiteSettings Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public string Path { get; } = path;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public static GeneratedSiteSettings Parse(AdditionalText file, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
    {
        SourceText? text = file.GetText(cancellationToken);
        string logicalPath = MarkazorSiteSettingsFile.NormalizeAdditionalFilePath(file.Path, options);

        if (text is null)
        {
            return new GeneratedSiteSettings(logicalPath, string.Empty, string.Empty);
        }

        return MarkazorSiteSettingsJson.Parse(logicalPath, text.ToString());
    }
}

internal sealed class MarkdownArticleResult(string path, MarkdownArticle? article, bool hasPublishedDraftFlag)
{
    public string Path { get; } = path;

    public MarkdownArticle? Article { get; } = article;

    public bool HasPublishedDraftFlag { get; } = hasPublishedDraftFlag;
}

internal sealed class MarkdownArticle
{
    private static readonly DateTimeOffset UnixEpoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private MarkdownArticle(string slug, string title, string summary, DateTimeOffset publishedAtUtc, IReadOnlyList<string> tags, string? category, string relativePath, string contentPath, string route, string kind)
    {
        Slug = slug;
        Title = title;
        Summary = summary;
        PublishedAtUtc = publishedAtUtc;
        Tags = tags;
        Category = category;
        RelativePath = relativePath;
        ContentPath = contentPath;
        Route = route;
        Kind = kind;
        IsDraft = false;
    }

    public string Slug { get; }

    public string Title { get; }

    public string Summary { get; }

    public DateTimeOffset PublishedAtUtc { get; }

    public IReadOnlyList<string> Tags { get; }

    public string? Category { get; }

    public string RelativePath { get; }

    public string ContentPath { get; }

    public string Route { get; }

    public string Kind { get; }

    public bool IsDraft { get; }

    public static MarkdownArticleResult Parse(AdditionalText file, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
    {
        SourceText? text = file.GetText(cancellationToken);

        if (text is null)
        {
            return new MarkdownArticleResult(file.Path, null, hasPublishedDraftFlag: false);
        }

        IReadOnlyDictionary<string, string> metadata = FrontMatter.Parse(text.ToString());
        string relativePath = MarkazorContentPath.NormalizeAdditionalFilePath(file.Path, options);
        string? kind = MarkazorContentPath.GetPublishedKind(relativePath);

        if (kind is null)
        {
            return new MarkdownArticleResult(file.Path, null, hasPublishedDraftFlag: false);
        }

        string slug = Get(metadata, "slug") ?? MarkazorContentPath.GetFileNameWithoutExtension(relativePath);
        string title = Get(metadata, "title") ?? slug;
        string summary = Get(metadata, "summary") ?? string.Empty;
        string? category = Get(metadata, "category");
        DateTimeOffset publishedAtUtc = ParsePublishedAt(Get(metadata, "publishedAt") ?? Get(metadata, "date"));
        string[] tags = ParseTags(Get(metadata, "tags"));
        bool hasPublishedDraftFlag = bool.TryParse(Get(metadata, "draft"), out bool isDraft) && isDraft;
        string route = MarkazorContentPath.CreateRoute(kind, slug);
        string contentPath = MarkazorContentPath.CreateContentPath(relativePath);

        return new MarkdownArticleResult(file.Path, new MarkdownArticle(slug, title, summary, publishedAtUtc, tags, category, relativePath, contentPath, route, kind), hasPublishedDraftFlag);
    }

    private static string? Get(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key.ToUpperInvariant(), out string? value) && value.Length > 0 ? value : null;
    }

    private static DateTimeOffset ParsePublishedAt(string? value)
    {
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset publishedAt) ? publishedAt : UnixEpoch;
    }

    private static string[] ParseTags(string? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string normalizedValue = value.Trim().Trim('[', ']');

        return [.. normalizedValue
            .Split([','], StringSplitOptions.RemoveEmptyEntries)
            .Select(static tag => tag.Trim().Trim('"', '\''))
            .Where(static tag => tag.Length > 0)];
    }

}

internal static class MarkazorContentPath
{
    private const string NoteKind = "note";
    private const string NotesSegment = "notes";
    private const string PostKind = "post";
    private const string PostsSegment = "posts";

    private static readonly char SitePathSeparator = Path.AltDirectorySeparatorChar;
    private static readonly string SitePathSeparatorText = SitePathSeparator.ToString();
    private static readonly string PublishedPostRoot = string.Concat(PostsSegment, SitePathSeparatorText);
    private static readonly string PublishedNoteRoot = string.Concat(NotesSegment, SitePathSeparatorText);

    public static string NormalizeAdditionalFilePath(string path, AnalyzerConfigOptions options)
    {
        if (TryGetLinkedAdditionalFilePath(options, out string? linkedPath))
        {
            return linkedPath!;
        }

        string filePath = GetUriPathOrDefault(path);
        string[] segments = filePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int postsSegmentIndex = FindSegment(segments, PostsSegment);
        int notesSegmentIndex = FindSegment(segments, NotesSegment);
        int publishedSegmentIndex = SelectPublishedSegmentIndex(postsSegmentIndex, notesSegmentIndex);

        return publishedSegmentIndex >= 0 ? CombineSitePath(segments.Skip(publishedSegmentIndex)) : Path.GetFileName(filePath);
    }

    public static string? GetPublishedKind(string relativePath)
    {
        if (relativePath.StartsWith(PublishedPostRoot, StringComparison.OrdinalIgnoreCase))
        {
            return PostKind;
        }

        return relativePath.StartsWith(PublishedNoteRoot, StringComparison.OrdinalIgnoreCase) ? NoteKind : null;
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }

    public static string CreateRoute(string kind, string slug)
    {
        string routeRoot = string.Equals(kind, NoteKind, StringComparison.Ordinal) ? NotesSegment : PostsSegment;

        return string.Concat(SitePathSeparatorText, CombineSitePath(routeRoot, slug));
    }

    public static string CreateContentPath(string relativePath)
    {
        return string.Concat(SitePathSeparatorText, "_markazor", SitePathSeparatorText, "content", SitePathSeparatorText, relativePath);
    }

    private static string GetUriPathOrDefault(string path)
    {
        if (TryCreateFileUri(path, out Uri? uri) && uri is not null)
        {
            return Uri.UnescapeDataString(uri.AbsolutePath);
        }

        return path;
    }

    private static bool TryGetLinkedAdditionalFilePath(AnalyzerConfigOptions options, out string? linkedPath)
    {
        linkedPath = null;

        if (!options.TryGetValue("build_metadata.AdditionalFiles.Link", out string? link)
            || string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        string[] segments = link.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        linkedPath = CombineSitePath(segments);

        return true;
    }

    private static bool TryCreateFileUri(string path, out Uri? uri)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out uri) && uri.IsFile;
    }

    private static int FindSegment(string[] segments, string expectedSegment)
    {
        for (int index = 0; index < segments.Length; index++)
        {
            if (string.Equals(segments[index], expectedSegment, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int SelectPublishedSegmentIndex(int postsSegmentIndex, int notesSegmentIndex)
    {
        if (postsSegmentIndex < 0)
        {
            return notesSegmentIndex;
        }

        if (notesSegmentIndex < 0)
        {
            return postsSegmentIndex;
        }

        return Math.Min(postsSegmentIndex, notesSegmentIndex);
    }

    private static string CombineSitePath(string firstSegment, string secondSegment)
    {
        return string.Concat(firstSegment, SitePathSeparatorText, secondSegment);
    }

    private static string CombineSitePath(IEnumerable<string> segments)
    {
        return string.Join(SitePathSeparatorText, segments);
    }
}

internal static class MarkazorSiteSettingsFile
{
    private static readonly char SitePathSeparator = Path.AltDirectorySeparatorChar;
    private static readonly string SitePathSeparatorText = SitePathSeparator.ToString();
    private static readonly string SettingsPath = string.Join(SitePathSeparatorText, "public", "markazor.settings.json");

    public static bool IsSettingsFile(string path)
    {
        string normalizedPath = GetUriPathOrDefault(path)
            .Replace(Path.DirectorySeparatorChar, SitePathSeparator)
            .Replace(Path.AltDirectorySeparatorChar, SitePathSeparator);

        return normalizedPath.EndsWith(SettingsPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(normalizedPath), "markazor.settings.json", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeAdditionalFilePath(string path, AnalyzerConfigOptions options)
    {
        if (TryGetLinkedAdditionalFilePath(options, out string? linkedPath))
        {
            return linkedPath!;
        }

        string filePath = GetUriPathOrDefault(path);
        string[] segments = filePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int publicSegmentIndex = FindSegment(segments, "public");

        return publicSegmentIndex >= 0 ? CombineSitePath(segments.Skip(publicSegmentIndex)) : Path.GetFileName(filePath);
    }

    private static bool TryGetLinkedAdditionalFilePath(AnalyzerConfigOptions options, out string? linkedPath)
    {
        linkedPath = null;

        if (!options.TryGetValue("build_metadata.AdditionalFiles.Link", out string? link)
            || string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        string[] segments = link.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        linkedPath = CombineSitePath(segments);

        return true;
    }

    private static string GetUriPathOrDefault(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            return Uri.UnescapeDataString(uri.AbsolutePath);
        }

        return path;
    }

    private static int FindSegment(string[] segments, string expectedSegment)
    {
        for (int index = 0; index < segments.Length; index++)
        {
            if (string.Equals(segments[index], expectedSegment, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string CombineSitePath(IEnumerable<string> segments)
    {
        return string.Join(SitePathSeparatorText, segments);
    }
}

internal static class MarkazorSiteSettingsJson
{
    public static GeneratedSiteSettings Parse(string path, string content)
    {
        string? siteJson = TryReadObjectProperty(content, "site");
        if (siteJson is null)
        {
            return new GeneratedSiteSettings(path, string.Empty, string.Empty);
        }

        string name = NormalizeText(TryReadStringProperty(siteJson, "name"));
        string description = NormalizeText(TryReadStringProperty(siteJson, "description"));

        return new GeneratedSiteSettings(path, name, description);
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static string? TryReadObjectProperty(string json, string propertyName)
    {
        int index = 0;
        while (TryReadNextProperty(json, ref index, propertyName, out int valueStart))
        {
            int objectStart = SkipWhitespace(json, valueStart);
            if (objectStart >= json.Length || json[objectStart] != '{')
            {
                continue;
            }

            int objectEnd = FindMatchingObjectEnd(json, objectStart);
            if (objectEnd >= objectStart)
            {
                return json.Substring(objectStart, objectEnd - objectStart + 1);
            }
        }

        return null;
    }

    private static string? TryReadStringProperty(string json, string propertyName)
    {
        int index = 0;
        while (TryReadNextProperty(json, ref index, propertyName, out int valueStart))
        {
            int stringStart = SkipWhitespace(json, valueStart);
            if (stringStart >= json.Length || json[stringStart] != '"')
            {
                continue;
            }

            int current = stringStart;
            return ReadString(json, ref current);
        }

        return null;
    }

    private static bool TryReadNextProperty(string json, ref int index, string propertyName, out int valueStart)
    {
        int depth = 0;
        valueStart = -1;

        while (index < json.Length)
        {
            char character = json[index];
            if (character == '"')
            {
                string? name = ReadString(json, ref index);
                if (depth == 1 && string.Equals(name, propertyName, StringComparison.Ordinal))
                {
                    int colonIndex = SkipWhitespace(json, index);
                    if (colonIndex < json.Length && json[colonIndex] == ':')
                    {
                        valueStart = colonIndex + 1;
                        index = valueStart;
                        return true;
                    }
                }

                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
            }

            index++;
        }

        return false;
    }

    private static int FindMatchingObjectEnd(string json, int objectStart)
    {
        int depth = 0;

        for (int index = objectStart; index < json.Length; index++)
        {
            char character = json[index];
            if (character == '"')
            {
                _ = ReadString(json, ref index);
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string? ReadString(string json, ref int index)
    {
        if (index >= json.Length || json[index] != '"')
        {
            return null;
        }

        index++;
        StringBuilder builder = new();

        while (index < json.Length)
        {
            char character = json[index++];
            if (character == '"')
            {
                return builder.ToString();
            }

            if (character != '\\' || index >= json.Length)
            {
                _ = builder.Append(character);
                continue;
            }

            char escaped = json[index++];
            switch (escaped)
            {
                case '"':
                case '\\':
                case '/':
                    _ = builder.Append(escaped);
                    break;
                case 'b':
                    _ = builder.Append('\b');
                    break;
                case 'f':
                    _ = builder.Append('\f');
                    break;
                case 'n':
                    _ = builder.Append('\n');
                    break;
                case 'r':
                    _ = builder.Append('\r');
                    break;
                case 't':
                    _ = builder.Append('\t');
                    break;
                case 'u':
                    if (index + 4 <= json.Length
                        && int.TryParse(json.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
                    {
                        _ = builder.Append((char)codePoint);
                        index += 4;
                    }

                    break;
            }
        }

        return null;
    }

    private static int SkipWhitespace(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return index;
    }
}

internal static class FrontMatter
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        Dictionary<string, string> metadata = [];
        using StringReader reader = new(content);

        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return metadata;
        }

        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                return metadata;
            }

            int separatorIndex = line.IndexOf(':');

            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line.Substring(0, separatorIndex).Trim().ToUpperInvariant();
            string value = line.Substring(separatorIndex + 1).Trim().Trim('"');

            if (key.Length > 0)
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }
}

internal static class SiteIndexSourceWriter
{
    public static string Write(ImmutableArray<MarkdownArticle> articles, GeneratedSiteSettings siteSettings)
    {
        StringBuilder builder = new();

        AppendLine(builder, "// <auto-generated />");
        AppendLine(builder, "#nullable enable");
        AppendLine(builder);
        AppendLine(builder, "namespace Markazor.Generated;");
        AppendLine(builder);
        AppendLine(builder, "public static partial class SiteIndex");
        AppendLine(builder, "{");
        AppendLine(builder, "    public static global::Markazor.Configuration.MarkazorSiteOptions Site { get; } = new()");
        AppendLine(builder, "    {");
        AppendLine(builder, "        Name = " + Literal(siteSettings.Name) + ",");
        AppendLine(builder, "        Description = " + Literal(siteSettings.Description) + ",");
        AppendLine(builder, "    };");
        AppendLine(builder);
        AppendLine(builder, "    public static global::System.Collections.Generic.IReadOnlyList<global::Markazor.Content.ArticleMeta> Articles { get; } =");
        AppendLine(builder, "    [");

        foreach (MarkdownArticle article in articles.OrderByDescending(static article => article.PublishedAtUtc))
        {
            AppendLine(builder, "        new global::Markazor.Content.ArticleMeta(");
            AppendArgument(builder, "Slug", Literal(article.Slug));
            AppendArgument(builder, "Title", Literal(article.Title));
            AppendArgument(builder, "Summary", Literal(article.Summary));
            AppendArgument(builder, "PublishedAtUtc", DateTimeOffsetLiteral(article.PublishedAtUtc));
            AppendArgument(builder, "Tags", TagsLiteral(article.Tags));
            AppendArgument(builder, "Category", NullableLiteral(article.Category));
            AppendArgument(builder, "RelativePath", Literal(article.RelativePath));
            AppendArgument(builder, "Route", Literal(article.Route));
            AppendArgument(builder, "IsDraft", article.IsDraft ? "true" : "false");
            AppendArgument(builder, "Kind", Literal(article.Kind));
            AppendArgument(builder, "ContentPath", Literal(article.ContentPath), closeConstructor: true);
        }

        AppendLine(builder, "    ];");
        AppendLine(builder, "}");

        return builder.ToString();
    }

    private static string DateTimeOffsetLiteral(DateTimeOffset value)
    {
        return string.Format(CultureInfo.InvariantCulture, "new global::System.DateTimeOffset({0}, {1}, {2}, {3}, {4}, {5}, global::System.TimeSpan.Zero)", value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
    }

    private static string Literal(string value)
    {
        StringBuilder builder = new(value.Length + 2);

        _ = builder.Append('"');

        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    _ = builder.Append("\\\"");
                    break;
                case '\\':
                    _ = builder.Append("\\\\");
                    break;
                case '\r':
                    _ = builder.Append("\\r");
                    break;
                case '\n':
                    _ = builder.Append("\\n");
                    break;
                case '\t':
                    _ = builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        _ = builder.Append("\\u");
                        _ = builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _ = builder.Append(character);
                    }

                    break;
            }
        }

        _ = builder.Append('"');

        return builder.ToString();
    }

    private static string NullableLiteral(string? value)
    {
        return value is null ? "null" : Literal(value);
    }

    private static string TagsLiteral(IReadOnlyList<string> tags)
    {
        return tags.Count == 0 ? "global::System.Array.Empty<string>()" : "[" + string.Join(", ", tags.Select(Literal)) + "]";
    }

    private static void AppendArgument(StringBuilder builder, string name, string value, bool closeConstructor = false)
    {
        _ = builder.Append("            ");
        _ = builder.Append(name);
        _ = builder.Append(": ");
        _ = builder.Append(value);
        _ = builder.AppendLine(closeConstructor ? ")," : ",");
    }

    private static void AppendLine(StringBuilder builder, string? value = null)
    {
        _ = value is null ? builder.AppendLine() : builder.AppendLine(value);
    }
}
