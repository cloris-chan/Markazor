using System.Net;
using System.Text;
using Markazor.Core.Auth;
using Markazor.Core.Configuration;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;
using Markazor.Client;

namespace Markazor.Tests;

public sealed class MarkazorSetupDiagnosticsServiceTests
{
    [Fact]
    public async Task DiagnosticsPassWithPushAccessAndIgnoresMissingRoots()
    {
        using DiagnosticsHandler handler = new();
        EnqueueSuccess(handler, canPush: true);
        using FakeSession session = new(handler);
        MarkazorSetupDiagnosticsService service = new(session);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorRepositoryDiagnostics result = await service.RunAsync(cancellationToken);

        Assert.True(result.Ready);
        Assert.True(result.RepositoryAccessible);
        Assert.True(result.CanPull);
        Assert.True(result.CanPush);
        Assert.True(result.BranchAccessible);
        Assert.True(result.TreeReadable);
        Assert.Empty(result.Warnings);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task MissingPushPermissionBlocksEditorReadiness()
    {
        using DiagnosticsHandler handler = new();
        EnqueueSuccess(handler, canPush: false);
        using FakeSession session = new(handler);
        MarkazorSetupDiagnosticsService service = new(session);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorRepositoryDiagnostics result = await service.RunAsync(cancellationToken);

        Assert.False(result.Ready);
        Assert.False(result.CanPush);
        Assert.Contains(result.Errors, error => error.Contains("cannot push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingConfiguredBranchIsReportedWithoutWriteProbe()
    {
        using DiagnosticsHandler handler = new();
        handler.Enqueue(
            HttpStatusCode.OK,
            Repository(canPush: true));
        handler.Enqueue(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        using FakeSession session = new(handler);
        MarkazorSetupDiagnosticsService service = new(session);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        MarkazorRepositoryDiagnostics result = await service.RunAsync(cancellationToken);

        Assert.False(result.Ready);
        Assert.False(result.BranchAccessible);
        Assert.Contains(result.Errors, error => error.Contains("branch", StringComparison.OrdinalIgnoreCase));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    private static void EnqueueSuccess(DiagnosticsHandler handler, bool canPush)
    {
        handler.Enqueue(HttpStatusCode.OK, Repository(canPush));
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
                {"path":"posts/hello.md","mode":"100644","type":"blob","sha":"blob-1"},
                {"path":"notes/quick.md","mode":"100644","type":"blob","sha":"blob-2"},
                {"path":"assets/seed.png","mode":"100644","type":"blob","sha":"blob-3"}
              ]
            }
            """);
    }

    private static string Repository(bool canPush)
    {
        string push = canPush ? "true" : "false";

        return $$"""
            {
              "name":"repo",
              "full_name":"owner/repo",
              "default_branch":"main",
              "private":false,
              "permissions":{"pull":true,"push":{{push}}}
            }
            """;
    }

    private sealed class FakeSession : IMarkazorClientSession, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly MarkazorApiOptions apiOptions;

        public FakeSession(
            DiagnosticsHandler handler)
        {
            httpClient = new HttpClient(handler);
            apiOptions = new MarkazorApiOptions
            {
                GitHubApiBaseUri = new Uri("https://github.test/"),
                RepositoryOwner = "owner",
                RepositoryName = "repo",
                DefaultBranch = "main",
            };
            SetupStatus = new MarkazorSetupStatus(
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
        }

        public MarkazorSetupStatus? SetupStatus { get; }

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

    private sealed class DiagnosticsHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> responses = [];

        public List<HttpMethod> Methods { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            responses.Enqueue((statusCode, body));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            (HttpStatusCode statusCode, string body) = responses.Dequeue();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
