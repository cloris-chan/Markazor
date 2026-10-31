using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Markazor.Content;
using Markazor.Core.Setup;
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

    private static readonly DiagnosticDescriptor InvalidSettingsDescriptor = new(
        id: "MZ002",
        title: "Invalid site settings",
        messageFormat: "Site settings '{0}' are invalid: {1}",
        category: "Markazor.Configuration",
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
                if (source.Right.Error is not null)
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(InvalidSettingsDescriptor, Location.None, source.Right.Path, source.Right.Error));
                }

                string generatedSource = SiteIndexSourceWriter.Write(sourceArticles, source.Right);
                productionContext.AddSource("SiteIndex.g.cs", SourceText.From(generatedSource, Encoding.UTF8));
            });
    }
}

internal sealed class GeneratedSiteSettings(string path, string name, string description, string? baseUrl = null, string? error = null)
{
    public static GeneratedSiteSettings Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public string Path { get; } = path;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public string? BaseUrl { get; } = baseUrl;

    public string? Error { get; } = error;

    public static GeneratedSiteSettings Parse(AdditionalText file, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
    {
        SourceText? text = file.GetText(cancellationToken);
        string logicalPath = MarkazorSiteSettingsFile.NormalizeAdditionalFilePath(file.Path, options);

        if (text is null)
        {
            return new GeneratedSiteSettings(logicalPath, string.Empty, string.Empty, error: "The settings file could not be read.");
        }

        try
        {
            MarkazorSiteSettings settings = MarkazorSiteSettingsJson.Parse(text.ToString());
            return new GeneratedSiteSettings(logicalPath, settings.Site.Name, settings.Site.Description, settings.Site.PrimaryBaseUrl?.AbsoluteUri);
        }
        catch (JsonException exception)
        {
            return new GeneratedSiteSettings(logicalPath, string.Empty, string.Empty, error: exception.Message);
        }
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

    private MarkdownArticle(string slug, string title, string summary, DateTimeOffset publishedAtUtc, IReadOnlyList<string> tags, string? category, string relativePath, string contentPath, string route)
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

    public bool IsDraft { get; }

    public static MarkdownArticleResult Parse(AdditionalText file, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
    {
        SourceText? text = file.GetText(cancellationToken);

        if (text is null)
        {
            return new MarkdownArticleResult(file.Path, null, hasPublishedDraftFlag: false);
        }

        MarkdownFrontMatter metadata = MarkdownFrontMatter.Parse(text.ToString());
        string relativePath = MarkazorContentPath.NormalizeAdditionalFilePath(file.Path, options);
        if (!MarkazorContentPath.IsPublishedPath(relativePath))
        {
            return new MarkdownArticleResult(file.Path, null, hasPublishedDraftFlag: false);
        }

        string slug = metadata.Get("slug") ?? MarkazorContentPath.GetFileNameWithoutExtension(relativePath);
        string title = metadata.Get("title") ?? slug;
        string summary = metadata.Get("summary") ?? string.Empty;
        string? category = metadata.Get("category");
        DateTimeOffset publishedAtUtc = metadata.PublishedAtUtc;
        string[] tags = metadata.GetTags();
        bool hasPublishedDraftFlag = bool.TryParse(metadata.Get("draft"), out bool isDraft) && isDraft;
        string route = MarkazorContentPath.CreateRoute(slug);
        string contentPath = MarkazorContentPath.CreateContentPath(relativePath);

        return new MarkdownArticleResult(file.Path, new MarkdownArticle(slug, title, summary, publishedAtUtc, tags, category, relativePath, contentPath, route), hasPublishedDraftFlag);
    }

}

internal static class MarkazorContentPath
{
    private const string PostsSegment = "posts";

    private static readonly char SitePathSeparator = Path.AltDirectorySeparatorChar;
    private static readonly string SitePathSeparatorText = SitePathSeparator.ToString();
    private static readonly string PublishedPostRoot = string.Concat(PostsSegment, SitePathSeparatorText);

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
        return postsSegmentIndex >= 0 ? CombineSitePath(segments.Skip(postsSegmentIndex)) : Path.GetFileName(filePath);
    }

    public static bool IsPublishedPath(string relativePath)
    {
        return relativePath.StartsWith(PublishedPostRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }

    public static string CreateRoute(string slug)
    {
        return string.Concat(SitePathSeparatorText, CombineSitePath(PostsSegment, Uri.EscapeDataString(slug)));
    }

    public static string CreateContentPath(string relativePath)
    {
        string normalizedPath = relativePath.Replace('\\', '/');
        string[] segments = normalizedPath.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.EscapeDataString(segments[index]);
        }

        return string.Concat(SitePathSeparatorText, "_markazor", SitePathSeparatorText, "content", SitePathSeparatorText, string.Join(SitePathSeparatorText, segments), ".json");
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

internal static class SiteIndexSourceWriter
{
    public static string Write(ImmutableArray<MarkdownArticle> articles, GeneratedSiteSettings siteSettings)
    {
        StringBuilder builder = new();

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
        AppendLine(builder, "        BaseUrl = " + (siteSettings.BaseUrl is null ? "null" : "new global::System.Uri(" + Literal(siteSettings.BaseUrl) + ")") + ",");
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
