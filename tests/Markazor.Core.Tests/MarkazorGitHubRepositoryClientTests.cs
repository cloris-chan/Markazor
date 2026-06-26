using System.Net;
using System.Text;
using Markazor.Core.Configuration;
using Markazor.Core.GitHub;
using Markazor.Core.Security;

namespace Markazor.Core.Tests;

public sealed class MarkazorGitHubRepositoryClientTests
{
    [Fact]
    public async Task RepositoryMetadataIncludesPermissions()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {
              "name":"repo",
              "full_name":"owner/repo",
              "default_branch":"main",
              "private":true,
              "permissions":{"pull":true,"push":false}
            }
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubRepository> result = await client.GetRepositoryAsync(
            "access-token",
            cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("owner/repo", result.Value?.FullName);
        Assert.True(result.Value?.IsPrivate);
        Assert.True(result.Value?.CanPull);
        Assert.False(result.Value?.CanPush);
        Assert.Equal(
            "https://github.test/repos/owner/repo",
            Assert.Single(handler.Requests).RequestUri.ToString());
    }

    [Fact]
    public async Task ReadBlobDecodesBase64Content()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"sha":"blob-sha","encoding":"base64","content":"SGVsbG8h\n"}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubBlob> result = await client.ReadBlobAsync(
            "blob-sha",
            "access-token",
            cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello!", result.Value?.ContentText);
        Assert.Equal(
            "https://github.test/repos/owner/repo/git/blobs/blob-sha",
            Assert.Single(handler.Requests).RequestUri.ToString());
    }

    [Fact]
    public async Task CreateBlobSendsBase64Payload()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"sha":"asset-blob","encoding":"base64","content":""}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubBlob> result = await client.CreateBlobAsync(
            new byte[] { 1, 2, 3 },
            "access-token",
            cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("asset-blob", result.Value?.Sha);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://github.test/repos/owner/repo/git/blobs", request.RequestUri.ToString());
        Assert.Contains("\"content\":\"AQID\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"encoding\":\"base64\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBlobAcceptsGitHubResponseWithoutContent()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"sha":"asset-blob","content":null}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubBlob> result = await client.CreateBlobAsync(
            new byte[] { 1, 2, 3 },
            "access-token",
            cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("asset-blob", result.Value?.Sha);
        Assert.Equal(string.Empty, result.Value?.EncodedContent);
        Assert.Equal(string.Empty, result.Value?.ContentText);
    }

    [Fact]
    public async Task ReadFileBuildsGitHubRequestAndDecodesBase64Content()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"path":"posts/hello.md","sha":"file-sha","encoding":"base64","content":"SGVsbG8h\n"}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubContentFile> result = await client.ReadFileAsync(
            "posts/hello.md",
            "access-token",
            cancellationToken: cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello!", result.Value?.ContentText);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://github.test/repos/owner/repo/contents/posts/hello.md?ref=main",
            request.RequestUri.ToString());
        Assert.Equal("Bearer access-token", request.Authorization);
        Assert.Contains("application/vnd.github+json", request.Accept, StringComparison.Ordinal);
        Assert.Equal("2026-03-10", request.GitHubApiVersion);
    }

    [Fact]
    public async Task CreateFileSendsBase64ContentWithoutSha()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"content":{"path":"posts/hello.md","sha":"file-sha"},"commit":{"sha":"commit-sha"}}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult> result = await client.CreateFileAsync(
            "posts\\hello.md",
            "Hello!",
            "Create post",
            "access-token",
            "preview",
            cancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("commit-sha", result.Value?.CommitSha);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://github.test/repos/owner/repo/contents/posts/hello.md", request.RequestUri.ToString());
        Assert.Contains("\"content\":\"SGVsbG8h\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"branch\":\"preview\"", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sha\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootSettingsFileHasDedicatedWriteMethod()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"content":{"path":"markazor.settings.json","sha":"settings-sha"},"commit":{"sha":"commit-sha"}}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult> result = await client.UpdateSettingsFileAsync(
            "{}",
            "Configure settings",
            "old-sha",
            "access-token",
            cancellationToken: cancellationToken);

        Assert.True(result.Succeeded);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://github.test/repos/owner/repo/contents/public/markazor.settings.json", request.RequestUri.ToString());
        Assert.Contains("\"content\":\"e30=\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"old-sha\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateConflictMapsToConflictResult()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.Conflict,
            """
            {"message":"sha does not match"}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult> result = await client.UpdateFileAsync(
            "posts/hello.md",
            "Hello!",
            "Update post",
            "old-sha",
            "access-token",
            cancellationToken: cancellationToken);

        Assert.Equal(MarkazorGitHubClientResultKind.Conflict, result.Kind);
        Assert.False(result.Succeeded);
        Assert.Contains("sha does not match", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MarkazorGitHubClientResultKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, MarkazorGitHubClientResultKind.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity, MarkazorGitHubClientResultKind.Conflict)]
    public async Task FailureStatusMapsToTypedResult(
        HttpStatusCode statusCode,
        MarkazorGitHubClientResultKind expectedKind)
    {
        using RecordingHandler handler = new();
        handler.Enqueue(statusCode, """{"message":"failed"}""");
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubRef> result = await client.UpdateBranchRefAsync(
            "main",
            "commit-sha",
            "access-token",
            cancellationToken: cancellationToken);

        Assert.Equal(expectedKind, result.Kind);
    }

    [Fact]
    public async Task WritesRequireAllowedPathAndCurrentShaForMutations()
    {
        using RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CreateFileAsync(
                "posts-evil/hello.md",
                "Hello!",
                "Create post",
                "access-token",
                cancellationToken: cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CreateFileAsync(
                "markazor.settings.json",
                "{}",
                "Create settings",
                "access-token",
                cancellationToken: cancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.UpdateFileAsync(
                "posts/hello.md",
                "Hello!",
                "Update post",
                string.Empty,
                "access-token",
                cancellationToken: cancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DeleteFileAsync(
                "posts/hello.md",
                "Delete post",
                string.Empty,
                "access-token",
                cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task GitDataBatchRequestsUsePathPolicyAndNonForceRefUpdateByDefault()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"ref":"refs/heads/main","object":{"sha":"commit-1","type":"commit"}}
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"sha":"tree-2","tree":[{"path":"posts/hello.md","mode":"100644","type":"blob","sha":"blob-1"}],"truncated":false}
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"sha":"commit-2"}
            """);
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"ref":"refs/heads/main","object":{"sha":"commit-2","type":"commit"}}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await client.GetBranchRefAsync(
            "main",
            "access-token",
            cancellationToken);
        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await client.CreateTreeAsync(
            "tree-1",
            [new MarkazorGitHubTreeFileChange("posts/hello.md", "Hello!")],
            "access-token",
            cancellationToken);
        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await client.CreateCommitAsync(
            "Publish content",
            tree.Value?.Sha ?? string.Empty,
            branch.Value?.Sha ?? string.Empty,
            "access-token",
            cancellationToken);
        MarkazorGitHubClientResult<MarkazorGitHubRef> updatedRef = await client.UpdateBranchRefAsync(
            "main",
            commit.Value?.Sha ?? string.Empty,
            "access-token",
            cancellationToken: cancellationToken);

        Assert.True(updatedRef.Succeeded);
        Assert.Collection(
            handler.Requests,
            first => Assert.Equal("https://github.test/repos/owner/repo/git/ref/heads/main", first.RequestUri.ToString()),
            second =>
            {
                Assert.Equal(HttpMethod.Post, second.Method);
                Assert.Equal("https://github.test/repos/owner/repo/git/trees", second.RequestUri.ToString());
                Assert.Contains("\"base_tree\":\"tree-1\"", second.Body, StringComparison.Ordinal);
                Assert.Contains("\"path\":\"posts/hello.md\"", second.Body, StringComparison.Ordinal);
            },
            third =>
            {
                Assert.Equal(HttpMethod.Post, third.Method);
                Assert.Equal("https://github.test/repos/owner/repo/git/commits", third.RequestUri.ToString());
                Assert.Contains("\"parents\":[\"commit-1\"]", third.Body, StringComparison.Ordinal);
            },
            fourth =>
            {
                Assert.Equal(HttpMethod.Patch, fourth.Method);
                Assert.Equal("https://github.test/repos/owner/repo/git/refs/heads/main", fourth.RequestUri.ToString());
                Assert.Contains("\"force\":false", fourth.Body, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task GitDataDraftPublishCanReadCommitTreeAndDeleteDraftEntry()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}
            """);
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"sha":"tree-2","tree":[],"truncated":false}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await client.GetCommitAsync(
            "commit-1",
            "access-token",
            cancellationToken);
        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await client.CreateTreeAsync(
            commit.Value?.TreeSha ?? string.Empty,
            [
                MarkazorGitHubTreeFileChange.Upsert("posts/hello.md", "Hello!"),
                MarkazorGitHubTreeFileChange.Remove("drafts/hello.md"),
            ],
            "access-token",
            cancellationToken);

        Assert.True(tree.Succeeded);
        Assert.Equal("tree-1", commit.Value?.TreeSha);
        Assert.Collection(
            handler.Requests,
            first => Assert.Equal("https://github.test/repos/owner/repo/git/commits/commit-1", first.RequestUri.ToString()),
            second =>
            {
                Assert.Equal("https://github.test/repos/owner/repo/git/trees", second.RequestUri.ToString());
                Assert.Contains("\"path\":\"posts/hello.md\"", second.Body, StringComparison.Ordinal);
                Assert.Contains("\"content\":\"Hello!\"", second.Body, StringComparison.Ordinal);
                Assert.Contains("\"path\":\"drafts/hello.md\"", second.Body, StringComparison.Ordinal);
                Assert.Contains("\"sha\":null", second.Body, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task GitDataTreeEntryCanReferenceExistingBlobSha()
    {
        using RecordingHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.Created,
            """
            {"sha":"tree-2","tree":[{"path":"assets/hash.png","mode":"100644","type":"blob","sha":"asset-blob"}],"truncated":false}
        """);
        using HttpClient httpClient = new(handler);
        MarkazorGitHubRepositoryClient client = CreateClient(httpClient);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await client.CreateTreeAsync(
            "tree-1",
            [MarkazorGitHubTreeFileChange.UpsertBlob("assets/hash.png", "asset-blob")],
            "access-token",
            cancellationToken);

        Assert.True(tree.Succeeded);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Contains("\"path\":\"assets/hash.png\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"asset-blob\"", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"content\"", request.Body, StringComparison.Ordinal);
    }

    private static MarkazorGitHubRepositoryClient CreateClient(HttpClient httpClient)
    {
        MarkazorApiOptions options = new()
        {
            GitHubApiBaseUri = new Uri("https://github.test/"),
            RepositoryOwner = "owner",
            RepositoryName = "repo",
            DefaultBranch = "main",
        };

        return new MarkazorGitHubRepositoryClient(
            httpClient,
            options);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<RecordedResponse> responses = [];

        public List<RecordedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            responses.Enqueue(new RecordedResponse(statusCode, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri ?? new Uri("about:blank"),
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString(),
                request.Headers.TryGetValues("X-GitHub-Api-Version", out IEnumerable<string>? values)
                    ? values.Single()
                    : string.Empty));

            RecordedResponse response = responses.Dequeue();

            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedResponse(HttpStatusCode StatusCode, string Body);

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string Body,
        string? Authorization,
        string Accept,
        string GitHubApiVersion);
}
