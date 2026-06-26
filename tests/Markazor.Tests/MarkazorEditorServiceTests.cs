using System.Net;
using System.Text;
using Markazor.Core.Auth;
using Markazor.Core.Configuration;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;
using Markazor.Client;
using Markazor.Content;
using Markazor.Editing;

namespace Markazor.Tests;

public sealed class MarkazorEditorServiceTests
{
    [Fact]
    public async Task BatchPublishCreatesOneAtomicCommitForAllDrafts()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"drafts/one.md","mode":"100644","type":"blob","sha":"blob-1"},
                {"path":"drafts/two.md","mode":"100644","type":"blob","sha":"blob-2"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.OK, Blob("blob-1", "One"));
        handler.Enqueue(HttpStatusCode.OK, Blob("blob-2", "Two"));
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {
              "sha":"tree-2",
              "truncated":false,
              "tree":[
                {"path":"posts/one.md","mode":"100644","type":"blob","sha":"post-1"},
                {"path":"posts/two.md","mode":"100644","type":"blob","sha":"post-2"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, """{"sha":"commit-2"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}""");
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);

        MarkazorBatchPublishResult result = await editor.PublishDraftsAsync(
            ["drafts/one.md", "drafts/two.md"],
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("commit-2", result.CommitSha);
        Assert.Equal(2, result.PublishedDocuments.Count);
        Assert.All(result.PublishedDocuments, document =>
            Assert.Contains("draft: false", document.Markdown, StringComparison.Ordinal));
        Assert.Equal(
            ["posts/one.md", "posts/two.md"],
            catalog.Entries.Select(static entry => entry.RelativePath).Order(StringComparer.Ordinal));
        RecordedRequest treeRequest = handler.Requests.Single(request =>
            request.Uri.AbsolutePath.EndsWith("/git/trees", StringComparison.Ordinal)
            && request.Method == HttpMethod.Post);
        Assert.Contains("\"path\":\"posts/one.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"drafts/one.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"posts/two.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"drafts/two.md\"", treeRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchPublishRejectsNestedDraftKindFolder()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"drafts/notes/idea.md","mode":"100644","type":"blob","sha":"blob-1"}
              ]
            }
            """);
        using FakeSession session = new(handler);
        MarkazorEditorService editor = new(session, new FakeCatalog());

        MarkazorBatchPublishResult result = await editor.PublishDraftsAsync(
            ["drafts/notes/idea.md"],
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("Only flat Markdown files under drafts/", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, request =>
            request.Uri.AbsolutePath.EndsWith("/git/trees", StringComparison.Ordinal)
            && request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task BatchPublishCreatesNoteDestinationFromFrontMatterKind()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"drafts/idea.md","mode":"100644","type":"blob","sha":"blob-1"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.OK, Blob("blob-1", "Idea", MarkazorArticleKind.Note));
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {
              "sha":"tree-2",
              "truncated":false,
              "tree":[
                {"path":"notes/idea.md","mode":"100644","type":"blob","sha":"note-1"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, """{"sha":"commit-2"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}""");
        using FakeSession session = new(handler);
        MarkazorEditorService editor = new(session, new FakeCatalog());

        MarkazorBatchPublishResult result = await editor.PublishDraftsAsync(
            ["drafts/idea.md"],
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        MarkazorEditorDocument document = Assert.Single(result.PublishedDocuments);
        Assert.Equal("notes/idea.md", document.Path);
    }

    [Fact]
    public async Task BatchPublishRejectsExistingPostDestinationBeforeWriting()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"drafts/one.md","mode":"100644","type":"blob","sha":"blob-1"},
                {"path":"posts/one.md","mode":"100644","type":"blob","sha":"post-1"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.OK, Blob("blob-1", "One"));
        using FakeSession session = new(handler);
        MarkazorEditorService editor = new(session, new FakeCatalog());

        MarkazorBatchPublishResult result = await editor.PublishDraftsAsync(
            ["drafts/one.md"],
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(MarkazorGitHubClientResultKind.Conflict, result.Kind);
        Assert.Contains("already exists", result.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task BatchPublishRetriesWholeOperationOnceAfterHeadConflict()
    {
        using EditorHandler handler = new();
        EnqueuePublishAttempt(handler, "1", HttpStatusCode.Conflict);
        EnqueuePublishAttempt(handler, "2", HttpStatusCode.OK);
        using FakeSession session = new(handler);
        MarkazorEditorService editor = new(session, new FakeCatalog());

        MarkazorBatchPublishResult result = await editor.PublishDraftsAsync(
            ["drafts/one.md"],
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(
            2,
            handler.Requests.Count(request =>
                request.Uri.AbsolutePath.Contains("/git/ref/heads/", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            handler.Requests.Count(request =>
                request.Uri.AbsolutePath.Contains("/git/refs/heads/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BatchPublishStopsAfterSecondHeadConflict()
    {
        using EditorHandler handler = new();
        EnqueuePublishAttempt(handler, "1", HttpStatusCode.Conflict);
        EnqueuePublishAttempt(handler, "2", HttpStatusCode.Conflict);
        using FakeSession session = new(handler);
        MarkazorEditorService editor = new(session, new FakeCatalog());

        MarkazorBatchPublishResult result = await editor.PublishDraftsAsync(
            ["drafts/one.md"],
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(MarkazorGitHubClientResultKind.Conflict, result.Kind);
        Assert.Equal(2, result.AttemptCount);
    }

    [Fact]
    public async Task SaveConflictReturnsLocalAndRemoteRecoveryData()
    {
        using EditorHandler handler = new();
        handler.Enqueue(HttpStatusCode.Conflict, """{"message":"sha mismatch"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "path":"posts/hello.md",
              "sha":"remote-sha",
              "encoding":"base64",
              "content":"UmVtb3Rl"
            }
            """);
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);

        MarkazorEditorOperationResult<MarkazorEditorDocument> result = await editor.SaveAsync(
            "posts/hello.md",
            "Local",
            "old-sha",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(MarkazorGitHubClientResultKind.Conflict, result.Kind);
        Assert.Equal("Local", result.Conflict?.LocalMarkdown);
        Assert.Equal("Remote", result.Conflict?.RemoteMarkdown);
        Assert.Equal("remote-sha", result.Conflict?.RemoteSha);
        Assert.Empty(catalog.Entries);
    }

    [Fact]
    public async Task SuccessfulSaveUpdatesCatalogImmediately()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"content":{"path":"drafts/hello.md","sha":"new-sha"},"commit":{"sha":"commit-sha"}}
            """);
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);
        const string Markdown = """
            ---
            title: Saved title
            draft: true
            ---
            """;

        MarkazorEditorOperationResult<MarkazorEditorDocument> result = await editor.SaveAsync(
            "drafts/hello.md",
            Markdown,
            "old-sha",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        MarkazorContentEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("Saved title", entry.Title);
        Assert.Equal("new-sha", entry.Sha);
    }

    [Fact]
    public async Task SaveWithReferencedAssetCreatesOneAtomicCommit()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"drafts/hello.md","mode":"100644","type":"blob","sha":"old-sha"}
              ]
            }
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """{"sha":"document-blob","encoding":"base64","content":""}""");
        handler.Enqueue(
            HttpStatusCode.Created,
            """{"sha":"asset-blob","encoding":"base64","content":""}""");
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {
              "sha":"tree-2",
              "truncated":false,
              "tree":[
                {"path":"drafts/hello.md","mode":"100644","type":"blob","sha":"document-blob"},
                {"path":"assets/hash.png","mode":"100644","type":"blob","sha":"asset-blob"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, """{"sha":"commit-2"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}""");
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);
        const string Markdown = """
            ---
            title: Saved title
            draft: true
            ---

            ![Image](/assets/hash.png)
            """;

        MarkazorEditorOperationResult<MarkazorEditorDocument> result = await editor.SaveAsync(
            "drafts/hello.md",
            Markdown,
            "old-sha",
            [new MarkazorEditorAsset("assets/hash.png", "/assets/hash.png", "image/png", new byte[] { 1, 2, 3 })],
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("document-blob", result.Value?.Sha);
        Assert.Equal("Saved title", Assert.Single(catalog.Entries).Title);
        RecordedRequest[] blobRequests =
        [
            .. handler.Requests.Where(request =>
                request.Uri.AbsolutePath.EndsWith("/git/blobs", StringComparison.Ordinal)),
        ];
        Assert.Equal(2, blobRequests.Length);
        Assert.Contains(blobRequests, request => request.Body.Contains("\"content\":\"AQID\"", StringComparison.Ordinal));
        RecordedRequest treeRequest = handler.Requests.Single(request =>
            request.Uri.AbsolutePath.EndsWith("/git/trees", StringComparison.Ordinal));
        Assert.Contains("\"path\":\"drafts/hello.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"document-blob\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"assets/hash.png\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"asset-blob\"", treeRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveWithExistingAssetSkipsBlobUpload()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"assets/hash.png","mode":"100644","type":"blob","sha":"asset-blob"}
              ]
            }
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """{"sha":"document-blob","encoding":"base64","content":""}""");
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {
              "sha":"tree-2",
              "truncated":false,
              "tree":[
                {"path":"drafts/hello.md","mode":"100644","type":"blob","sha":"document-blob"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, """{"sha":"commit-2"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}""");
        using FakeSession session = new(handler);
        MarkazorEditorService editor = new(session, new FakeCatalog());
        const string Markdown = """
            ---
            title: Saved title
            draft: true
            ---

            ![Image](/assets/hash.png)
            """;

        MarkazorEditorOperationResult<MarkazorEditorDocument> result = await editor.SaveAsync(
            "drafts/hello.md",
            Markdown,
            null,
            [new MarkazorEditorAsset("assets/hash.png", "/assets/hash.png", "image/png", new byte[] { 1, 2, 3 })],
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        RecordedRequest blobRequest = Assert.Single(
            handler.Requests,
            request => request.Uri.AbsolutePath.EndsWith("/git/blobs", StringComparison.Ordinal));
        Assert.DoesNotContain("\"content\":\"AQID\"", blobRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishUnsavedDraftCreatesPublishedFileWithoutDraftDelete()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[]
            }
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {
              "sha":"tree-2",
              "truncated":false,
              "tree":[
                {"path":"posts/new-draft.md","mode":"100644","type":"blob","sha":"post-sha"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, """{"sha":"commit-2"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}""");
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);
        const string Markdown = """
            ---
            title: New Draft
            slug: new-draft
            draft: true
            ---

            Body
            """;

        MarkazorEditorOperationResult<MarkazorEditorDocument> result = await editor.PublishDraftAsync(
            "drafts/new-draft.md",
            Markdown,
            draftSha: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("posts/new-draft.md", result.Value?.Path);
        Assert.Contains("draft: false", result.Value?.Markdown, StringComparison.Ordinal);
        Assert.Equal("posts/new-draft.md", Assert.Single(catalog.Entries).RelativePath);
        RecordedRequest treeRequest = handler.Requests.Single(request =>
            request.Uri.AbsolutePath.EndsWith("/git/trees", StringComparison.Ordinal)
            && request.Method == HttpMethod.Post);
        Assert.Contains("\"path\":\"posts/new-draft.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"path\":\"drafts/new-draft.md\"", treeRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishDirtyDraftWithAssetCreatesOneAtomicCommit()
    {
        using EditorHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "sha":"tree-1",
              "truncated":false,
              "tree":[
                {"path":"drafts/hello.md","mode":"100644","type":"blob","sha":"draft-sha"}
              ]
            }
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """{"sha":"asset-blob","encoding":"base64","content":""}""");
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {
              "sha":"tree-2",
              "truncated":false,
              "tree":[
                {"path":"posts/hello.md","mode":"100644","type":"blob","sha":"post-sha"},
                {"path":"assets/hash.png","mode":"100644","type":"blob","sha":"asset-blob"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, """{"sha":"commit-2"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}""");
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);
        const string Markdown = """
            ---
            title: Hello
            slug: hello
            draft: true
            ---

            Edited body

            ![Image](/assets/hash.png)
            """;

        MarkazorEditorOperationResult<MarkazorEditorDocument> result = await editor.PublishDraftAsync(
            "drafts/hello.md",
            Markdown,
            "draft-sha",
            [new MarkazorEditorAsset("assets/hash.png", "/assets/hash.png", "image/png", new byte[] { 1, 2, 3 })],
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("posts/hello.md", result.Value?.Path);
        Assert.Contains("Edited body", result.Value?.Markdown, StringComparison.Ordinal);
        Assert.Contains("draft: false", result.Value?.Markdown, StringComparison.Ordinal);
        Assert.Equal("posts/hello.md", Assert.Single(catalog.Entries).RelativePath);
        Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath.Contains("/contents/", StringComparison.Ordinal));
        Assert.Single(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/git/blobs", StringComparison.Ordinal));
        RecordedRequest treeRequest = handler.Requests.Single(request =>
            request.Uri.AbsolutePath.EndsWith("/git/trees", StringComparison.Ordinal)
            && request.Method == HttpMethod.Post);
        Assert.Contains("\"path\":\"posts/hello.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"drafts/hello.md\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"assets/hash.png\"", treeRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"asset-blob\"", treeRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteConflictReturnsLatestShaForExplicitSecondDelete()
    {
        using EditorHandler handler = new();
        handler.Enqueue(HttpStatusCode.Conflict, """{"message":"sha mismatch"}""");
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "path":"posts/hello.md",
              "sha":"latest-sha",
              "encoding":"base64",
              "content":"UmVtb3Rl"
            }
            """);
        using FakeSession session = new(handler);
        FakeCatalog catalog = new();
        MarkazorEditorService editor = new(session, catalog);

        MarkazorEditorOperationResult result = await editor.DeleteAsync(
            "posts/hello.md",
            "old-sha",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("latest-sha", result.Conflict?.RemoteSha);
        Assert.Equal(HttpMethod.Delete, handler.Methods[0]);
        Assert.Equal(HttpMethod.Get, handler.Methods[1]);
    }

    private sealed class FakeCatalog : IMarkazorContentCatalog
    {
        private readonly List<MarkazorContentEntry> entries = [];

        public IReadOnlyList<MarkazorContentEntry> Entries => entries;

        public bool IsBuildIndexFallback => false;

        public string? Warning => null;

        public Task<IReadOnlyList<MarkazorContentEntry>> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Entries);
        }

        public void UpsertDocument(string path, string markdown, string? sha)
        {
            entries.RemoveAll(entry => string.Equals(entry.RelativePath, path, StringComparison.Ordinal));
            entries.Add(new MarkazorContentEntry(
                MarkdownContent.ParseArticleMeta(path, markdown),
                sha,
                ExistsOnGitHub: true));
        }

        public void Remove(string path)
        {
            entries.RemoveAll(entry => string.Equals(entry.RelativePath, path, StringComparison.Ordinal));
        }
    }

    private static void EnqueuePublishAttempt(
        EditorHandler handler,
        string suffix,
        HttpStatusCode updateRefStatus)
    {
        handler.Enqueue(
            HttpStatusCode.OK,
            $"{{\"ref\":\"refs/heads/main\",\"object\":{{\"sha\":\"commit-{suffix}\",\"type\":\"commit\"}}}}");
        handler.Enqueue(
            HttpStatusCode.OK,
            $"{{\"sha\":\"commit-{suffix}\",\"tree\":{{\"sha\":\"tree-{suffix}\",\"type\":\"tree\"}}}}");
        handler.Enqueue(
            HttpStatusCode.OK,
            $$"""
            {
              "sha":"tree-{{suffix}}",
              "truncated":false,
              "tree":[
                {"path":"drafts/one.md","mode":"100644","type":"blob","sha":"blob-{{suffix}}"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.OK, Blob($"blob-{suffix}", "One"));
        handler.Enqueue(
            HttpStatusCode.Created,
            $$"""
            {
              "sha":"new-tree-{{suffix}}",
              "truncated":false,
              "tree":[
                {"path":"posts/one.md","mode":"100644","type":"blob","sha":"post-{{suffix}}"}
              ]
            }
            """);
        handler.Enqueue(HttpStatusCode.Created, $"{{\"sha\":\"new-commit-{suffix}\"}}");
        handler.Enqueue(
            updateRefStatus,
            updateRefStatus == HttpStatusCode.OK
                ? $"{{\"ref\":\"refs/heads/main\",\"object\":{{\"sha\":\"new-commit-{suffix}\",\"type\":\"commit\"}}}}"
                : """{"message":"Reference update failed"}""");
    }

    private static string Blob(string sha, string title, string kind = MarkazorArticleKind.Post)
    {
        string markdown = $$"""
            ---
            title: {{title}}
            kind: {{kind}}
            draft: true
            ---

            Body
            """;

        return $$"""
            {
              "sha":"{{sha}}",
              "encoding":"base64",
              "content":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown))}}"
            }
            """;
    }

    private sealed class FakeSession(EditorHandler handler) : IMarkazorClientSession, IDisposable
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

    private sealed class EditorHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> responses = [];

        public List<HttpMethod> Methods { get; } = [];

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            responses.Enqueue((statusCode, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            string requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri ?? new Uri("about:blank"),
                requestBody));
            (HttpStatusCode statusCode, string responseBody) = responses.Dequeue();

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
}
