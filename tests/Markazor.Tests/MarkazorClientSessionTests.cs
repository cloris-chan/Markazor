using System.Net;
using System.Text;
using Markazor.Core.Auth;
using Markazor.Core.GitHub;
using Markazor.Core.Setup;
using Markazor.Client;

namespace Markazor.Tests;

public sealed class MarkazorClientSessionTests
{
    [Fact]
    public async Task ConcurrentTokenRequestsUseSingleRefresh()
    {
        using SessionHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        string?[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => session.GetAccessTokenAsync(testCancellationToken)));

        Assert.All(tokens, token => Assert.Equal("token-1", token));
        Assert.Equal(1, handler.RefreshCount);
    }

    [Fact]
    public async Task UnauthorizedGitHubRequestRefreshesAndRetriesOnce()
    {
        using SessionHandler handler = new()
        {
            FirstGitHubStatus = HttpStatusCode.Unauthorized,
        };
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;
        _ = await session.LoadSetupStatusAsync(testCancellationToken);

        MarkazorGitHubClientResult<MarkazorGitHubRepository> result = await session.ExecuteGitHubAsync(
            static (client, token, cancellationToken) => client.GetRepositoryAsync(token, cancellationToken),
            testCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.RefreshCount);
        Assert.Equal(2, handler.GitHubCount);
        Assert.Equal("Bearer token-2", handler.GitHubAuthorizations.Last());
    }

    [Fact]
    public async Task ForbiddenGitHubRequestDoesNotRefreshOrRetry()
    {
        using SessionHandler handler = new()
        {
            FirstGitHubStatus = HttpStatusCode.Forbidden,
        };
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;
        _ = await session.LoadSetupStatusAsync(testCancellationToken);

        MarkazorGitHubClientResult<MarkazorGitHubRepository> result = await session.ExecuteGitHubAsync(
            static (client, token, cancellationToken) => client.GetRepositoryAsync(token, cancellationToken),
            testCancellationToken);

        Assert.Equal(MarkazorGitHubClientResultKind.Forbidden, result.Kind);
        Assert.Equal(1, handler.RefreshCount);
        Assert.Equal(1, handler.GitHubCount);
    }

    [Fact]
    public async Task LoadSetupStatusReportsInvalidApiJsonAsHttpRequest()
    {
        using SessionHandler handler = new()
        {
            SetupStatusContentType = "text/html",
            SetupStatusBody = "<!doctype html><html></html>",
        };
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await session.LoadSetupStatusAsync(testCancellationToken).ConfigureAwait(false));

        Assert.Contains("/api/setup/status", exception.Message, StringComparison.Ordinal);
        Assert.Contains("valid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadSetupStatusDeserializesPublicBaseUrls()
    {
        using SessionHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        MarkazorSetupStatus? status = await session.LoadSetupStatusAsync(testCancellationToken);

        Assert.NotNull(status);
        Assert.Equal([new Uri("https://status.example/")], status.Site.BaseUrls);
        Assert.Equal("https://status.example/", status.Site.PrimaryBaseUrl?.ToString());
    }

    [Fact]
    public async Task StartAuthorizationPostsTemporarySettings()
    {
        using SessionHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        MarkazorGitHubAuthorizationResponse response = await session.StartGitHubAuthorizationAsync(
            new MarkazorGitHubAuthorizationRequest(
                "client-from-browser",
                new Uri("https://site.example/")),
            testCancellationToken);

        Assert.Equal("https://github.test/authorize", response.AuthorizationUrl.ToString());
        Assert.Contains("\"clientId\":\"client-from-browser\"", handler.LastStartBody, StringComparison.Ordinal);
        Assert.Contains("\"siteBaseUrl\":\"https://site.example/\"", handler.LastStartBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAuthorizationPostsCallbackBodyAndStoresAccessToken()
    {
        using SessionHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        bool completed = await session.CompleteGitHubAuthorizationAsync(
            new MarkazorGitHubCallbackRequest("code-1", "state-1"),
            testCancellationToken);

        Assert.True(completed);
        Assert.Equal("callback-token", session.AccessToken);
        Assert.Contains("\"code\":\"code-1\"", handler.LastCallbackBody, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"state-1\"", handler.LastCallbackBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSyncUsesDraftRepositorySettingsAsTarget()
    {
        MarkazorSiteSettings settings = CreateDraftSettings();
        using SessionHandler handler = new()
        {
            SettingsReadStatus = HttpStatusCode.NotFound,
            SetupMissingSettingsJson = """["repository.owner","repository.name"]""",
            StatusRepositoryOwner = "status-owner",
            StatusRepositoryName = "status-repo",
        };
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        MarkazorSettingsSyncService settingsSync = new(session);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        MarkazorSettingsSyncResult result = await settingsSync.SaveAsync(settings, cancellationToken: testCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("settings-commit", result.CommitSha);
        Assert.Contains(
            "/repos/target-owner/target-repo/contents/public/markazor.settings.json?ref=trunk",
            handler.GitHubRequestPaths,
            StringComparer.Ordinal);
        Assert.Contains(
            "/repos/target-owner/target-repo/contents/public/markazor.settings.json",
            handler.GitHubRequestPaths,
            StringComparer.Ordinal);
        Assert.Contains("\"branch\":\"trunk\"", handler.LastSettingsWriteBody, StringComparison.Ordinal);
        Assert.Contains("target-owner", handler.DecodedSettingsWriteContent, StringComparison.Ordinal);
        Assert.DoesNotContain("status-owner", handler.DecodedSettingsWriteContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSyncSkipsCommitWhenPublicSettingsAlreadyMatch()
    {
        MarkazorSiteSettings settings = CreateDraftSettings();
        using SessionHandler handler = new()
        {
            SettingsReadStatus = HttpStatusCode.OK,
            SettingsReadContent = """
                {
                  "repository": {
                    "defaultBranch": "trunk",
                    "name": "target-repo",
                    "owner": "target-owner"
                  },
                  "github": {
                    "clientId": "client-id"
                  },
                  "site": {
                    "name": " Target Site ",
                    "description": " Target description ",
                    "baseUrls": [
                      "https://site.example/"
                    ]
                  }
                }
                """,
        };
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        MarkazorSettingsSyncService settingsSync = new(session);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        MarkazorSettingsSyncResult result = await settingsSync.SaveAsync(settings, cancellationToken: testCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.CommitSha);
        Assert.Equal("Settings already match the repository.", result.Message);
        Assert.Equal(string.Empty, handler.LastSettingsWriteBody);
    }

    [Fact]
    public async Task SettingsSyncUploadsSiteIconToFixedAssetPath()
    {
        MarkazorSiteSettings settings = CreateDraftSettings();
        using SessionHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        MarkazorSettingsSyncService settingsSync = new(session);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        MarkazorSettingsSyncResult result = await settingsSync.SaveAsync(
            settings,
            new MarkazorSettingsAsset(MarkazorRepositoryPaths.SiteIconPath, "image/png", CreateTinyPngBytes()),
            testCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("settings-icon-commit", result.CommitSha);
        Assert.Contains(handler.GitHubRequestBodies, body => body.Contains("\"content\":\"iVBORw0KGgo", StringComparison.Ordinal));
        string treeBody = Assert.Single(handler.GitHubRequestBodies, body => body.Contains("\"path\":\"assets/site-icon.png\"", StringComparison.Ordinal));
        Assert.Contains("\"path\":\"public/markazor.settings.json\"", treeBody, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"assets/site-icon.png\"", treeBody, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"settings-blob\"", treeBody, StringComparison.Ordinal);
        Assert.Contains("\"sha\":\"icon-blob\"", treeBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSyncRejectsInvalidSiteIconPng()
    {
        MarkazorSiteSettings settings = CreateDraftSettings();
        using SessionHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://markazor.test/"),
        };
        using MarkazorClientSession session = new(httpClient);
        MarkazorSettingsSyncService settingsSync = new(session);
        CancellationToken testCancellationToken = TestContext.Current.CancellationToken;

        MarkazorSettingsSyncResult result = await settingsSync.SaveAsync(
            settings,
            new MarkazorSettingsAsset(MarkazorRepositoryPaths.SiteIconPath, "image/png", new byte[] { 1, 2, 3 }),
            testCancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("Site icon must be a valid PNG file.", result.Message);
        Assert.Empty(handler.GitHubRequestBodies);
    }

    private static MarkazorSiteSettings CreateDraftSettings()
    {
        return new MarkazorSiteSettings
        {
            Site = new MarkazorSitePublicSettings
            {
                Name = "Target Site",
                Description = "Target description",
                BaseUrls = [new Uri("https://site.example/")],
            },
            GitHub = new MarkazorGitHubSettings { ClientId = "client-id" },
            Repository = new MarkazorRepositorySettings
            {
                Owner = "target-owner",
                Name = "target-repo",
                DefaultBranch = "trunk",
            },
        };
    }

    private static byte[] CreateTinyPngBytes()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lE+H4wAAAABJRU5ErkJggg==");
    }

    private sealed class SessionHandler : HttpMessageHandler
    {
        public HttpStatusCode FirstGitHubStatus { get; init; } = HttpStatusCode.OK;

        public HttpStatusCode SettingsReadStatus { get; init; } = HttpStatusCode.OK;

        public string SettingsReadContent { get; init; } = "{}";

        public string? SetupStatusBody { get; init; }

        public string SetupStatusContentType { get; init; } = "application/json";

        public string SetupMissingSettingsJson { get; init; } = "[]";

        public string StatusRepositoryOwner { get; init; } = "owner";

        public string StatusRepositoryName { get; init; } = "repo";

        public int RefreshCount { get; private set; }

        public int GitHubCount { get; private set; }

        public string LastStartBody { get; private set; } = string.Empty;

        public string LastCallbackBody { get; private set; } = string.Empty;

        public string LastSettingsWriteBody { get; private set; } = string.Empty;

        public string DecodedSettingsWriteContent { get; private set; } = string.Empty;

        public List<string> GitHubAuthorizations { get; } = [];

        public List<string> GitHubRequestPaths { get; } = [];

        public List<string> GitHubRequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/api/setup/status", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        SetupStatusBody ?? $$"""
                    {
                      "ready":true,
                      "missingSettings":{{SetupMissingSettingsJson}},
                      "site":{"baseUrls":["https://status.example/"],"description":"Status description"},
                      "github":{"clientId":"status-client"},
                      "repository":{"owner":"{{StatusRepositoryOwner}}","name":"{{StatusRepositoryName}}","defaultBranch":"main"},
                      "expectedStaticWebAppsBuildSettings":{
                        "appLocation":"src/App",
                        "apiLocation":"src/Api",
                        "outputLocation":"wwwroot"
                      }
                    }
                    """,
                        Encoding.UTF8,
                        SetupStatusContentType),
                };
            }

            if (string.Equals(path, "/api/auth/github/refresh", StringComparison.Ordinal))
            {
                RefreshCount++;
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);

                return Json(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "accessToken":"token-{{RefreshCount}}",
                      "accessTokenExpiresAtUtc":"{{DateTimeOffset.UtcNow.AddHours(1):O}}"
                    }
                    """);
            }

            if (string.Equals(path, "/api/auth/github/start", StringComparison.Ordinal))
            {
                LastStartBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "authorizationUrl":"https://github.test/authorize",
                      "stateExpiresAtUtc":"2026-06-07T00:00:00+00:00"
                    }
                    """);
            }

            if (string.Equals(path, "/api/auth/github/callback", StringComparison.Ordinal))
            {
                LastCallbackBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return Json(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "accessToken":"callback-token",
                      "accessTokenExpiresAtUtc":"{{DateTimeOffset.UtcNow.AddHours(1):O}}"
                    }
                    """);
            }

            if (path.EndsWith($"/repos/{StatusRepositoryOwner}/{StatusRepositoryName}", StringComparison.Ordinal))
            {
                GitHubCount++;
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                GitHubAuthorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
                HttpStatusCode statusCode = GitHubCount == 1 ? FirstGitHubStatus : HttpStatusCode.OK;

                return Json(
                    statusCode,
                    statusCode == HttpStatusCode.OK
                        ? """
                          {
                            "name":"repo",
                            "full_name":"owner/repo",
                            "default_branch":"main",
                            "private":false,
                            "permissions":{"pull":true,"push":true}
                          }
                          """
                        : """{"message":"request failed"}""");
            }

            if (path.EndsWith("/repos/target-owner/target-repo/contents/public/markazor.settings.json", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                if (request.Method == HttpMethod.Get)
                {
                    return Json(
                        SettingsReadStatus,
                        SettingsReadStatus == HttpStatusCode.OK
                            ? $$"""
                              {
                                "path":"public/markazor.settings.json",
                                "sha":"settings-sha",
                                "encoding":"base64",
                                "content":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(SettingsReadContent))}}"
                              }
                              """
                            : """{"message":"not found"}""");
                }

                if (request.Method == HttpMethod.Put)
                {
                    LastSettingsWriteBody = request.Content is null
                        ? string.Empty
                        : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    DecodedSettingsWriteContent = DecodeSettingsWriteContent(LastSettingsWriteBody);

                    return Json(
                        HttpStatusCode.OK,
                        """
                        {"content":{"path":"public/markazor.settings.json","sha":"settings-sha"},"commit":{"sha":"settings-commit"}}
                        """);
                }
            }

            if (path.EndsWith("/repos/target-owner/target-repo/git/ref/heads/trunk", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                if (request.Method == HttpMethod.Get)
                {
                    return Json(HttpStatusCode.OK, """{"ref":"refs/heads/trunk","object":{"sha":"commit-1","type":"commit"}}""");
                }
            }

            if (path.EndsWith("/repos/target-owner/target-repo/git/refs/heads/trunk", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                if (request.Method == HttpMethod.Patch)
                {
                    GitHubRequestBodies.Add(await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false));
                    return Json(HttpStatusCode.OK, """{"ref":"refs/heads/trunk","object":{"sha":"settings-icon-commit","type":"commit"}}""");
                }
            }

            if (path.EndsWith("/repos/target-owner/target-repo/git/commits/commit-1", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                return Json(HttpStatusCode.OK, """{"sha":"commit-1","tree":{"sha":"tree-1","type":"tree"}}""");
            }

            if (path.EndsWith("/repos/target-owner/target-repo/git/blobs", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                string body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
                GitHubRequestBodies.Add(body);
                string sha = body.Contains("\"content\":\"iVBORw0KGgo", StringComparison.Ordinal)
                    ? "icon-blob"
                    : "settings-blob";

                return Json(HttpStatusCode.Created, $$"""{"sha":"{{sha}}","encoding":"base64","content":""}""");
            }

            if (path.EndsWith("/repos/target-owner/target-repo/git/trees", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                GitHubRequestBodies.Add(await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false));
                return Json(HttpStatusCode.Created, """{"sha":"tree-2","truncated":false,"tree":[]}""");
            }

            if (path.EndsWith("/repos/target-owner/target-repo/git/commits", StringComparison.Ordinal))
            {
                GitHubRequestPaths.Add(GetPathAndQuery(request));
                GitHubRequestBodies.Add(await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false));
                return Json(HttpStatusCode.Created, """{"sha":"settings-icon-commit"}""");
            }

            return Json(HttpStatusCode.NotFound, """{"message":"not found"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        private static Task<string> ReadBodyAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return request.Content is null
                ? Task.FromResult(string.Empty)
                : request.Content.ReadAsStringAsync(cancellationToken);
        }

        private static string DecodeSettingsWriteContent(string body)
        {
            const string property = "\"content\":\"";
            int start = body.IndexOf(property, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += property.Length;
            int end = body.IndexOf('"', start);

            return end < 0
                ? string.Empty
                : Encoding.UTF8.GetString(Convert.FromBase64String(body[start..end]));
        }

        private static string GetPathAndQuery(HttpRequestMessage request)
        {
            return request.RequestUri is null
                ? string.Empty
                : request.RequestUri.PathAndQuery;
        }
    }
}
