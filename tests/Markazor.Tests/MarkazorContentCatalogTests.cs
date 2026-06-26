using System.Net;
using System.Text;
using Markazor.Core.Auth;
using Markazor.Core.Configuration;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;
using Markazor.Client;
using Markazor.Configuration;
using Markazor.Content;

namespace Markazor.Tests;

public sealed class MarkazorContentCatalogTests
{
    [Fact]
    public async Task GitHubTreeIsTruthAndBuildIndexEnrichesMatchingPaths()
    {
        using CatalogHandler handler = new(
            treeBody:
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"posts/hello.md","mode":"100644","type":"blob","sha":"hello-sha"},
                {"path":"notes/quick.md","mode":"100644","type":"blob","sha":"quick-sha"},
                {"path":"drafts/runtime.md","mode":"100644","type":"blob","sha":"runtime-sha"},
                {"path":"README.md","mode":"100644","type":"blob","sha":"readme-sha"}
              ]
            }
            """);
        using FakeSession session = new(handler);
        MarkazorOptions options = new()
        {
            Articles =
            [
                Article("hello", "Build title", "posts/hello.md", isDraft: false),
                Article("quick", "Build note", "notes/quick.md", isDraft: false, kind: MarkazorArticleKind.Note),
                Article("deleted", "Deleted", "posts/deleted.md", isDraft: false),
            ],
        };
        MarkazorContentCatalog catalog = new(session, options);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        IReadOnlyList<MarkazorContentEntry> entries = await catalog.RefreshAsync(cancellationToken);

        Assert.False(catalog.IsBuildIndexFallback);
        Assert.Null(catalog.Warning);
        Assert.Equal(3, entries.Count);
        Assert.Equal("Build title", entries.Single(entry => entry.RelativePath.EndsWith("hello.md", StringComparison.Ordinal)).Title);
        MarkazorContentEntry note = entries.Single(entry => entry.RelativePath.EndsWith("quick.md", StringComparison.Ordinal));
        Assert.Equal("Build note", note.Title);
        Assert.Equal(MarkazorArticleKind.Note, note.Article.Kind);
        Assert.Equal(RoutePath("notes", "quick"), note.Article.Route);
        Assert.Equal("runtime", entries.Single(entry => entry.RelativePath.EndsWith("runtime.md", StringComparison.Ordinal)).Title);
        Assert.DoesNotContain(entries, entry => entry.RelativePath.EndsWith("deleted.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TruncatedTreePreservesPreviousEntriesAndSetsWarning()
    {
        using CatalogHandler handler = new(
            treeBody:
            """
            {"sha":"tree-1","truncated":true,"tree":[]}
            """);
        using FakeSession session = new(handler);
        MarkazorOptions options = new()
        {
            Articles = [Article("hello", "Build title", "posts/hello.md", isDraft: false)],
        };
        MarkazorContentCatalog catalog = new(session, options);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        IReadOnlyList<MarkazorContentEntry> entries = await catalog.RefreshAsync(cancellationToken);

        Assert.True(catalog.IsBuildIndexFallback);
        Assert.Single(entries);
        Assert.Contains("truncated", catalog.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedDocumentUpdatesRuntimeMetadataAndRemoveDropsIt()
    {
        using CatalogHandler handler = new();
        using FakeSession session = new(handler);
        MarkazorContentCatalog catalog = new(session, new MarkazorOptions());
        const string Markdown = """
            ---
            title: Runtime title
            draft: true
            ---

            Body
            """;

        catalog.UpsertDocument("drafts/runtime.md", Markdown, "sha-1");

        MarkazorContentEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("Runtime title", entry.Title);
        Assert.Equal("sha-1", entry.Sha);
        Assert.True(entry.IsDraft);

        catalog.Remove("drafts/runtime.md");

        Assert.Empty(catalog.Entries);
    }

    private static ArticleMeta Article(
        string slug,
        string title,
        string path,
        bool isDraft,
        string kind = MarkazorArticleKind.Post)
    {
        return new ArticleMeta(
            slug,
            title,
            string.Empty,
            DateTimeOffset.UnixEpoch,
            [],
            null,
            path,
            RoutePath(
                string.Equals(kind, MarkazorArticleKind.Note, StringComparison.Ordinal) ? "notes" : "posts",
                slug),
            isDraft,
            kind);
    }

    private static string RoutePath(string routeRoot, string slug)
    {
        return string.Concat(
            Path.AltDirectorySeparatorChar,
            string.Join(Path.AltDirectorySeparatorChar, routeRoot, slug));
    }

    private sealed class FakeSession(CatalogHandler handler) : IMarkazorClientSession, IDisposable
    {
        private readonly HttpClient httpClient = new(handler);
        private readonly MarkazorApiOptions apiOptions = new()
        {
            GitHubApiBaseUri = new Uri("https://github.test/"),
            RepositoryOwner = "owner",
            RepositoryName = "repo",
            DefaultBranch = "main",
        };

        public MarkazorSetupStatus? SetupStatus { get; } = new(
            Ready: true,
            MissingSettings: [],
            Site: new MarkazorSitePublicSettings(),
            GitHub: new MarkazorGitHubSettings { ClientId = "client-id" },
            Repository: new MarkazorRepositoryStatus("owner", "repo", "main"),
            Theme: new MarkazorThemeSettings { Name = "default" },
            ExpectedStaticWebAppsBuildSettings: new MarkazorStaticWebAppsBuildSettings(
                "src/App",
                "src/Api",
                "wwwroot"));

        public string? AccessToken => "token";

        public DateTimeOffset? AccessTokenExpiresAtUtc => DateTimeOffset.UtcNow.AddHours(1);

        public bool IsReady => true;

        public bool IsAuthenticated => true;

        public Task<MarkazorSetupStatus?> LoadSetupStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetupStatus);
        }

        public Task<MarkazorGitHubAuthorizationResponse> StartGitHubAuthorizationAsync(
            MarkazorGitHubAuthorizationRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> CompleteGitHubAuthorizationAsync(
            MarkazorGitHubCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>("token");
        }

        public Task<MarkazorGitHubClientResult<T>> ExecuteGitHubAsync<T>(
            Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
            CancellationToken cancellationToken = default)
        {
            MarkazorGitHubRepositoryClient client = new(
                httpClient,
                apiOptions);

            return operation(client, "token", cancellationToken);
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }

    private sealed class CatalogHandler(string? treeBody = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string body = path.Contains("/git/ref/", StringComparison.Ordinal)
                ? """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}"""
                : path.Contains("/git/commits/", StringComparison.Ordinal)
                    ? """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}"""
                    : treeBody ?? """{"sha":"tree-1","truncated":false,"tree":[]}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
