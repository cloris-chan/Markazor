using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Markazor.Configuration;
using Markazor.Content;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Markazor.SourceGen.Tests;

public sealed class SiteIndexGeneratorTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private readonly IReadOnlyList<MetadataReference> references = CreateReferences();

    [Fact]
    public void GeneratesArticleMetadataFromMarkdownFiles()
    {
        TestAdditionalText[] additionalTexts =
        [
            new(
                RepositoryPath("posts", "older.md"),
                """
                ---
                slug: older-post
                title: Older Post
                summary: The first public article.
                publishedAt: 2026-06-01T08:30:00Z
                tags: [intro, "markazor"]
                category: General
                ---

                # Older Post
                """),
            new(
                RepositoryPath("posts", "newer.md"),
                """
                ---
                slug: newer-post
                title: Newer Post
                summary: The newest public article.
                publishedAt: 2026-06-03T12:00:00Z
                tags: [release]
                category: Updates
                draft: false
                ---

                # Newer Post
                """),
            new(
                RepositoryPath("notes", "field-note.md"),
                """
                ---
                slug: field-note
                title: Field Note
                summary: A compact public note.
                publishedAt: 2026-06-04T07:00:00Z
                tags: [notes]
                category: Notes
                ---

                # Field Note
                """),
            new(
                RepositoryPath("drafts", "draft.md"),
                """
                ---
                slug: draft-post
                title: Draft Post
                summary: Hidden until published.
                publishedAt: 2026-06-02T00:00:00Z
                tags: [private]
                ---

                # Draft Post
                """),
        ];

        GeneratorExecution execution = RunGenerator(additionalTexts);
        IReadOnlyList<ArticleMeta> articles = LoadArticles(execution.OutputCompilation);

        Assert.Collection(
            articles,
            article =>
            {
                Assert.Equal("field-note", article.Slug);
                Assert.Equal("Field Note", article.Title);
                Assert.Equal("Notes", article.Category);
                Assert.Equal(RoutePath("notes", "field-note"), article.Route);
                Assert.Equal(RepositoryContentPath("notes", "field-note.md"), article.RelativePath);
                Assert.Equal(["notes"], article.Tags);
                Assert.Equal(MarkazorArticleKind.Note, article.Kind);
                Assert.False(article.IsDraft);
            },
            article =>
            {
                Assert.Equal("newer-post", article.Slug);
                Assert.Equal("Newer Post", article.Title);
                Assert.Equal("The newest public article.", article.Summary);
                Assert.Equal("Updates", article.Category);
                Assert.Equal(RoutePath("posts", "newer-post"), article.Route);
                Assert.Equal(RepositoryContentPath("posts", "newer.md"), article.RelativePath);
                Assert.Equal(["release"], article.Tags);
                Assert.Equal(MarkazorArticleKind.Post, article.Kind);
                Assert.False(article.IsDraft);
            },
            article =>
            {
                Assert.Equal("older-post", article.Slug);
                Assert.Equal("General", article.Category);
                Assert.Equal(["intro", "markazor"], article.Tags);
                Assert.Equal(MarkazorArticleKind.Post, article.Kind);
                Assert.False(article.IsDraft);
            });
        Assert.Empty(execution.Diagnostics);
    }

    [Fact]
    public void FallsBackToStableDefaultsWhenFrontMatterIsMissing()
    {
        TestAdditionalText[] additionalTexts =
        [
            new(RepositoryPath("posts", "plain-file.md"), "# Plain File"),
        ];

        GeneratorExecution execution = RunGenerator(additionalTexts);
        ArticleMeta article = Assert.Single(LoadArticles(execution.OutputCompilation));

        Assert.Equal("plain-file", article.Slug);
        Assert.Equal("plain-file", article.Title);
        Assert.Equal(string.Empty, article.Summary);
        Assert.Null(article.Category);
        Assert.Empty(article.Tags);
        Assert.Equal(RepositoryContentPath("posts", "plain-file.md"), article.RelativePath);
        Assert.Equal(RoutePath("posts", "plain-file"), article.Route);
        Assert.Equal(MarkazorArticleKind.Post, article.Kind);
        Assert.Equal(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), article.PublishedAtUtc);
        Assert.False(article.IsDraft);
        Assert.Empty(execution.Diagnostics);
    }

    [Fact]
    public void UsesNoteDefaultsFromRepositoryRootWhenFrontMatterIsMissing()
    {
        TestAdditionalText[] additionalTexts =
        [
            new(RepositoryPath("notes", "plain-note.md"), "# Plain Note"),
        ];

        GeneratorExecution execution = RunGenerator(additionalTexts);
        ArticleMeta article = Assert.Single(LoadArticles(execution.OutputCompilation));

        Assert.Equal("plain-note", article.Slug);
        Assert.Equal("plain-note", article.Title);
        Assert.Equal(RepositoryContentPath("notes", "plain-note.md"), article.RelativePath);
        Assert.Equal(RoutePath("notes", "plain-note"), article.Route);
        Assert.Equal(MarkazorArticleKind.Note, article.Kind);
        Assert.False(article.IsDraft);
        Assert.Empty(execution.Diagnostics);
    }

    [Fact]
    public void UsesAdditionalFileLinkMetadataAsLogicalPath()
    {
        TestAdditionalText[] additionalTexts =
        [
            new(
                Path.Combine(Path.GetTempPath(), "source", "somewhere", "generated-name.md"),
                "# Linked Post"),
        ];

        GeneratorExecution execution = RunGenerator(additionalTexts, new TestAnalyzerConfigOptionsProvider(
            additionalTexts[0],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.Link"] = RepositoryContentPath("posts", "linked-post.md"),
            }));
        ArticleMeta article = Assert.Single(LoadArticles(execution.OutputCompilation));

        Assert.Equal("linked-post", article.Slug);
        Assert.Equal(RepositoryContentPath("posts", "linked-post.md"), article.RelativePath);
        Assert.Equal(RoutePath("posts", "linked-post"), article.Route);
        Assert.Equal(MarkazorArticleKind.Post, article.Kind);
        Assert.Empty(execution.Diagnostics);
    }

    [Fact]
    public void ReportsErrorWhenPublishedContentIsMarkedAsDraft()
    {
        TestAdditionalText[] additionalTexts =
        [
            new(
                RepositoryPath("notes", "hidden.md"),
                """
                ---
                title: Hidden
                draft: true
                ---
                """),
        ];

        GeneratorExecution execution = RunGenerator(additionalTexts);

        Diagnostic diagnostic = Assert.Single(execution.Diagnostics);
        Assert.Equal("MZ001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("hidden.md", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratesSiteSettingsFromPublicSettingsFile()
    {
        TestAdditionalText[] additionalTexts =
        [
            new(RepositoryPath("public", "markazor.settings.json"), """
                {
                  "site": {
                    "name": "Configured Site",
                    "description": "Configured description"
                  }
                }
                """),
            new(RepositoryPath("posts", "hello.md"), "# Hello"),
        ];

        GeneratorExecution execution = RunGenerator(additionalTexts);
        MarkazorSiteOptions site = LoadSite(execution.OutputCompilation);

        Assert.Equal("Configured Site", site.Name);
        Assert.Equal("Configured description", site.Description);
        Assert.Empty(execution.Diagnostics);
    }

    [Fact]
    public void UsesSettingsFileLinkMetadataAsLogicalPath()
    {
        TestAdditionalText settings = new(
            Path.Combine(Path.GetTempPath(), "source", "somewhere", "markazor.settings.json"),
            """
            {
              "site": {
                "name": "Linked Site",
                "description": "Linked description"
              }
            }
            """);
        TestAdditionalText post = new(RepositoryPath("posts", "hello.md"), "# Hello");
        TestAdditionalText[] additionalTexts = [settings, post];

        GeneratorExecution execution = RunGenerator(additionalTexts, new TestAnalyzerConfigOptionsProvider([
            new TestAdditionalTextMetadata(
                settings,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["build_metadata.AdditionalFiles.Link"] = RepositoryContentPath("public", "markazor.settings.json"),
                }),
        ]));
        MarkazorSiteOptions site = LoadSite(execution.OutputCompilation);

        Assert.Equal("Linked Site", site.Name);
        Assert.Equal("Linked description", site.Description);
        Assert.Empty(execution.Diagnostics);
    }

    private GeneratorExecution RunGenerator(
        IReadOnlyList<AdditionalText> additionalTexts,
        AnalyzerConfigOptionsProvider? analyzerConfigOptionsProvider = null)
    {
        CSharpCompilation inputCompilation = CSharpCompilation.Create(
            "Markazor.SourceGen.Tests.GeneratedAssembly",
            [CSharpSyntaxTree.ParseText("namespace TestProject { internal static class Anchor { } }", ParseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = analyzerConfigOptionsProvider is null
            ? CSharpGeneratorDriver.Create(
                [new SiteIndexGenerator().AsSourceGenerator()],
                additionalTexts,
                parseOptions: ParseOptions)
            : CSharpGeneratorDriver.Create(
                [new SiteIndexGenerator().AsSourceGenerator()],
                additionalTexts,
                parseOptions: ParseOptions,
                optionsProvider: analyzerConfigOptionsProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            inputCompilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generationDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        return new GeneratorExecution(
            outputCompilation,
            [.. generationDiagnostics.Concat(runResult.Diagnostics).Distinct()]);
    }

    private static IReadOnlyList<ArticleMeta> LoadArticles(Compilation outputCompilation)
    {
        using MemoryStream assemblyStream = new();
        EmitResult emitResult = outputCompilation.Emit(assemblyStream);

        Assert.True(emitResult.Success, FormatDiagnostics(emitResult.Diagnostics));

        assemblyStream.Position = 0;
        Assembly assembly = Assembly.Load(assemblyStream.ToArray());
        Type siteIndexType = assembly.GetType("Markazor.Generated.SiteIndex", throwOnError: true)!;
        object articlesValue = siteIndexType.GetProperty("Articles", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        return Assert.IsAssignableFrom<IReadOnlyList<ArticleMeta>>(articlesValue);
    }

    private static MarkazorSiteOptions LoadSite(Compilation outputCompilation)
    {
        using MemoryStream assemblyStream = new();
        EmitResult emitResult = outputCompilation.Emit(assemblyStream);

        Assert.True(emitResult.Success, FormatDiagnostics(emitResult.Diagnostics));

        assemblyStream.Position = 0;
        Assembly assembly = Assembly.Load(assemblyStream.ToArray());
        Type siteIndexType = assembly.GetType("Markazor.Generated.SiteIndex", throwOnError: true)!;
        object siteValue = siteIndexType.GetProperty("Site", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        return Assert.IsType<MarkazorSiteOptions>(siteValue);
    }

    private static IReadOnlyList<MetadataReference> CreateReferences()
    {
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (trustedPlatformAssemblies is null)
        {
            throw new InvalidOperationException("The test runtime did not expose trusted platform assemblies.");
        }

        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(ArticleMeta).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString()));
    }

    private static string RepositoryPath(params string[] segments)
    {
        return Path.Combine(
            Path.GetTempPath(),
            "markazor-sourcegen-tests",
            Path.Combine(segments));
    }

    private static string RepositoryContentPath(params string[] segments)
    {
        return string.Join(
            Path.AltDirectorySeparatorChar,
            segments);
    }

    private static string RoutePath(string routeRoot, string slug)
    {
        return string.Concat(
            Path.AltDirectorySeparatorChar,
            string.Join(Path.AltDirectorySeparatorChar, routeRoot, slug));
    }

    private sealed class TestAdditionalText(string filePath, string content) : AdditionalText
    {
        private readonly SourceText text = SourceText.From(content, Encoding.UTF8);

        public override string Path { get; } = filePath;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return text;
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly IReadOnlyList<TestAdditionalTextMetadata> metadata;
        private readonly TestAnalyzerConfigOptions emptyOptions = new(new Dictionary<string, string>(StringComparer.Ordinal));

        public TestAnalyzerConfigOptionsProvider(
            AdditionalText targetAdditionalText,
            IReadOnlyDictionary<string, string> metadata)
            : this([new TestAdditionalTextMetadata(targetAdditionalText, metadata)])
        {
        }

        public TestAnalyzerConfigOptionsProvider(IReadOnlyList<TestAdditionalTextMetadata> metadata)
        {
            this.metadata = metadata;
        }

        public override AnalyzerConfigOptions GlobalOptions => emptyOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return emptyOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            foreach (TestAdditionalTextMetadata item in metadata)
            {
                if (ReferenceEquals(textFile, item.AdditionalText))
                {
                    return new TestAnalyzerConfigOptions(item.Metadata);
                }
            }

            return emptyOptions;
        }
    }

    private sealed record TestAdditionalTextMetadata(
        AdditionalText AdditionalText,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out string? candidate))
            {
                value = candidate;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    private sealed record GeneratorExecution(
        Compilation OutputCompilation,
        IReadOnlyList<Diagnostic> Diagnostics);
}
