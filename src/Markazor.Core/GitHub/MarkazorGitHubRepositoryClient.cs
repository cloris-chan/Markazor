using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Markazor.Core.Configuration;
using Markazor.Core.Security;
using Markazor.Core.Serialization;
using Markazor.Core.Setup;

namespace Markazor.Core.GitHub;

public sealed class MarkazorGitHubRepositoryClient(HttpClient httpClient, MarkazorApiOptions options)
{
    private const string GitHubApiVersion = "2026-03-10";
    private static readonly Uri DefaultApiBaseUri = new("https://api.github.com/");

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubRepository>> GetRepositoryAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        MarkazorGitHubClientResult<GitHubRepositoryResponse> result = await SendAsync(
            CreateRequest(HttpMethod.Get, BuildRepositoryUri(string.Empty), accessToken),
            MarkazorCoreJsonSerializerContext.Default.GitHubRepositoryResponse,
            cancellationToken).ConfigureAwait(false);

        return MapResult(result, static response => new MarkazorGitHubRepository(response.Name, response.FullName, response.DefaultBranch, response.IsPrivate, response.Permissions?.Pull == true, response.Permissions?.Push == true));
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubBlob>> ReadBlobAsync(string blobSha, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobSha);

        MarkazorGitHubClientResult<GitHubBlobResponse> result = await SendAsync(
            CreateRequest(
                HttpMethod.Get,
                BuildRepositoryUri($"git/blobs/{Uri.EscapeDataString(blobSha)}"),
                accessToken),
            MarkazorCoreJsonSerializerContext.Default.GitHubBlobResponse,
            cancellationToken).ConfigureAwait(false);

        return MapResult(
            result,
            static response =>
            {
                string encodedContent = RemoveWhitespace(response.Content);
                string contentText = string.Equals(response.Encoding, "base64", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetString(Convert.FromBase64String(encodedContent)) : response.Content;

                return new MarkazorGitHubBlob(response.Sha, response.Encoding, encodedContent, contentText);
            });
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubBlob>> CreateBlobAsync(ReadOnlyMemory<byte> content, string accessToken, CancellationToken cancellationToken = default)
    {
        GitHubCreateBlobRequest payload = new()
        {
            Content = Convert.ToBase64String(content.Span),
            Encoding = "base64",
        };

        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, BuildRepositoryUri("git/blobs"), accessToken);
        request.Content = CreateJsonContent(payload, MarkazorCoreJsonSerializerContext.Default.GitHubCreateBlobRequest);

        MarkazorGitHubClientResult<GitHubBlobResponse> result = await SendAsync(request, MarkazorCoreJsonSerializerContext.Default.GitHubBlobResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, static response => new MarkazorGitHubBlob(response.Sha, string.Empty, string.Empty, string.Empty));
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubContentFile>> ReadFileAsync(string relativePath, string accessToken, string? reference = null, CancellationToken cancellationToken = default)
    {
        string normalizedPath = ContentPathPolicy.NormalizeGitHubPath(relativePath);
        string requestUri = BuildRepositoryUri($"contents/{EscapePath(normalizedPath)}?ref={Uri.EscapeDataString(NormalizeBranch(reference))}");

        MarkazorGitHubClientResult<GitHubContentFileResponse> result = await SendAsync(CreateRequest(HttpMethod.Get, requestUri, accessToken), MarkazorCoreJsonSerializerContext.Default.GitHubContentFileResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapContentFile);
    }

    public Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> CreateFileAsync(string relativePath, string content, string message, string accessToken, string? branch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return PutFileAsync(relativePath, content, message, null, accessToken, branch, cancellationToken);
    }

    public Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> CreateSettingsFileAsync(string content, string message, string accessToken, string? branch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return PutRootSettingsFileAsync(content, message, null, accessToken, branch, cancellationToken);
    }

    public Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> UpdateFileAsync(string relativePath, string content, string message, string sha, string accessToken, string? branch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);

        return PutFileAsync(relativePath, content, message, sha, accessToken, branch, cancellationToken);
    }

    public Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> UpdateSettingsFileAsync(string content, string message, string sha, string accessToken, string? branch = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);

        return PutRootSettingsFileAsync(content, message, sha, accessToken, branch, cancellationToken);
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> DeleteFileAsync(
        string relativePath, string message, string sha, string accessToken, string? branch = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);

        string normalizedPath = ContentPathPolicy.EnsureRepositoryWriteAllowed(relativePath);
        GitHubContentsDeleteRequest payload = new()
        {
            Message = message,
            Sha = sha,
            Branch = NormalizeBranch(branch),
        };

        using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, BuildRepositoryUri($"contents/{EscapePath(normalizedPath)}"), accessToken);
        request.Content = CreateJsonContent(payload, MarkazorCoreJsonSerializerContext.Default.GitHubContentsDeleteRequest);

        MarkazorGitHubClientResult<GitHubContentsWriteResponse> result = await SendAsync(
            request,
            MarkazorCoreJsonSerializerContext.Default.GitHubContentsWriteResponse,
            cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapContentMutation);
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubRef>> GetBranchRefAsync(string? branch, string accessToken, CancellationToken cancellationToken = default)
    {
        string requestUri = BuildRepositoryUri($"git/ref/heads/{EscapePath(NormalizeBranch(branch))}");

        MarkazorGitHubClientResult<GitHubRefResponse> result = await SendAsync(CreateRequest(HttpMethod.Get, requestUri, accessToken), MarkazorCoreJsonSerializerContext.Default.GitHubRefResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapRef);
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubTree>> GetTreeAsync(string treeSha, string accessToken, bool recursive = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(treeSha);

        string recursiveQuery = recursive ? "?recursive=1" : string.Empty;
        string requestUri = BuildRepositoryUri($"git/trees/{Uri.EscapeDataString(treeSha)}{recursiveQuery}");

        MarkazorGitHubClientResult<GitHubTreeResponse> result = await SendAsync(CreateRequest(HttpMethod.Get, requestUri, accessToken), MarkazorCoreJsonSerializerContext.Default.GitHubTreeResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapTree);
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubCommit>> GetCommitAsync(string commitSha, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        string requestUri = BuildRepositoryUri($"git/commits/{Uri.EscapeDataString(commitSha)}");

        MarkazorGitHubClientResult<GitHubCommitResponse> result = await SendAsync(CreateRequest(HttpMethod.Get, requestUri, accessToken), MarkazorCoreJsonSerializerContext.Default.GitHubCommitResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, static response => new MarkazorGitHubCommit(response.Sha, response.Tree?.Sha ?? string.Empty));
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubTree>> CreateTreeAsync(string baseTreeSha, IReadOnlyCollection<MarkazorGitHubTreeFileChange> changes, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTreeSha);
        ArgumentNullException.ThrowIfNull(changes);

        List<GitHubCreateTreeEntryRequest> entries = new(changes.Count);
        foreach (MarkazorGitHubTreeFileChange change in changes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(change.Path);
            ArgumentException.ThrowIfNullOrWhiteSpace(change.Mode);
            ArgumentException.ThrowIfNullOrWhiteSpace(change.Type);

            if (!change.Delete && string.IsNullOrWhiteSpace(change.Sha))
            {
                ArgumentNullException.ThrowIfNull(change.Content);
            }

            entries.Add(new GitHubCreateTreeEntryRequest
            {
                Path = ContentPathPolicy.EnsureRepositoryWriteAllowed(change.Path),
                Mode = change.Mode,
                Type = change.Type,
                Content = change.Content,
                Sha = change.Sha,
                Delete = change.Delete,
            });
        }

        GitHubCreateTreeRequest payload = new()
        {
            BaseTreeSha = baseTreeSha,
            Tree = [.. entries],
        };

        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, BuildRepositoryUri("git/trees"), accessToken);
        request.Content = CreateJsonContent(payload, MarkazorCoreJsonSerializerContext.Default.GitHubCreateTreeRequest);

        MarkazorGitHubClientResult<GitHubTreeResponse> result = await SendAsync(request, MarkazorCoreJsonSerializerContext.Default.GitHubTreeResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapTree);
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubCommit>> CreateCommitAsync(string message, string treeSha, string parentCommitSha, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(treeSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentCommitSha);

        GitHubCreateCommitRequest payload = new()
        {
            Message = message,
            Tree = treeSha,
            Parents = [parentCommitSha],
        };

        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, BuildRepositoryUri("git/commits"), accessToken);
        request.Content = CreateJsonContent(payload, MarkazorCoreJsonSerializerContext.Default.GitHubCreateCommitRequest);

        MarkazorGitHubClientResult<GitHubCreateCommitResponse> result = await SendAsync(request, MarkazorCoreJsonSerializerContext.Default.GitHubCreateCommitResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, static response => new MarkazorGitHubCommit(response.Sha));
    }

    public async Task<MarkazorGitHubClientResult<MarkazorGitHubRef>> UpdateBranchRefAsync(string? branch, string commitSha, string accessToken, bool force = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        GitHubUpdateRefRequest payload = new()
        {
            Sha = commitSha,
            Force = force,
        };

        using HttpRequestMessage request = CreateRequest(HttpMethod.Patch, BuildRepositoryUri($"git/refs/heads/{EscapePath(NormalizeBranch(branch))}"), accessToken);
        request.Content = CreateJsonContent(payload, MarkazorCoreJsonSerializerContext.Default.GitHubUpdateRefRequest);

        MarkazorGitHubClientResult<GitHubRefResponse> result = await SendAsync(request, MarkazorCoreJsonSerializerContext.Default.GitHubRefResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapRef);
    }

    private async Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> PutFileAsync(string relativePath, string content, string message, string? sha, string accessToken, string? branch, CancellationToken cancellationToken)
    {
        string normalizedPath = ContentPathPolicy.EnsureRepositoryWriteAllowed(relativePath);
        GitHubContentsWriteRequest payload = new()
        {
            Message = message,
            Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            Branch = NormalizeBranch(branch),
            Sha = sha,
        };

        using HttpRequestMessage request = CreateRequest(HttpMethod.Put, BuildRepositoryUri($"contents/{EscapePath(normalizedPath)}"), accessToken);
        request.Content = CreateJsonContent(payload, MarkazorCoreJsonSerializerContext.Default.GitHubContentsWriteRequest);

        MarkazorGitHubClientResult<GitHubContentsWriteResponse> result = await SendAsync(request, MarkazorCoreJsonSerializerContext.Default.GitHubContentsWriteResponse, cancellationToken).ConfigureAwait(false);

        return MapResult(result, MapContentMutation);
    }

    private Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>> PutRootSettingsFileAsync(string content, string message, string? sha, string accessToken, string? branch, CancellationToken cancellationToken)
    {
        return PutFileAsync(MarkazorRepositoryPaths.SettingsPath, content, message, sha, accessToken, branch, cancellationToken);
    }

    private static MarkazorGitHubContentFile MapContentFile(GitHubContentFileResponse response)
    {
        string encodedContent = RemoveWhitespace(response.Content);
        string contentText = string.Equals(response.Encoding, "base64", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8.GetString(Convert.FromBase64String(encodedContent)) : response.Content;

        return new MarkazorGitHubContentFile(response.Path, response.Sha, response.Encoding, encodedContent, contentText);
    }

    private static MarkazorGitHubContentMutationResult MapContentMutation(GitHubContentsWriteResponse response)
    {
        return new MarkazorGitHubContentMutationResult(response.Content?.Path ?? string.Empty, response.Content?.Sha ?? string.Empty, response.Commit?.Sha ?? string.Empty);
    }

    private static MarkazorGitHubRef MapRef(GitHubRefResponse response)
    {
        return new MarkazorGitHubRef(response.Name, response.Target?.Sha ?? string.Empty, response.Target?.Type ?? string.Empty);
    }

    private static MarkazorGitHubTree MapTree(GitHubTreeResponse response)
    {
        MarkazorGitHubTreeEntry[] entries = [.. response.Tree.Select(static entry => new MarkazorGitHubTreeEntry(entry.Path, entry.Mode, entry.Type, entry.Sha))];

        return new MarkazorGitHubTree(response.Sha, entries, response.Truncated);
    }

    private static MarkazorGitHubClientResult<TTarget> MapResult<TSource, TTarget>(MarkazorGitHubClientResult<TSource> result, Func<TSource, TTarget> map)
    {
        if (!result.Succeeded || result.Value is null)
        {
            return new MarkazorGitHubClientResult<TTarget>(result.Kind, default, result.StatusCode, result.Message);
        }

        return new MarkazorGitHubClientResult<TTarget>(MarkazorGitHubClientResultKind.Success, map(result.Value), result.StatusCode, null);
    }

    private async Task<MarkazorGitHubClientResult<TResponse>> SendAsync<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using (request)
        using (HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.Unauthorized, default, response.StatusCode, body);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.Forbidden, default, response.StatusCode, body);
            }

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                return new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.Conflict, default, response.StatusCode, body);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.NotFound, default, response.StatusCode, body);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.Error, default, response.StatusCode, body);
            }

            TResponse? payload = JsonSerializer.Deserialize(body, responseTypeInfo);

            return payload is null
                ? new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.Error, default, response.StatusCode, "GitHub returned an empty response.")
                : new MarkazorGitHubClientResult<TResponse>(MarkazorGitHubClientResultKind.Success, payload, response.StatusCode, null);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string requestUri, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        HttpRequestMessage request = new(method, new Uri(GetApiBaseUri(), requestUri));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Markazor", "0.1"));

        return request;
    }

    private static StringContent CreateJsonContent<TValue>(TValue value, JsonTypeInfo<TValue> typeInfo)
    {
        return new StringContent(JsonSerializer.Serialize(value, typeInfo), Encoding.UTF8, "application/json");
    }

    private string BuildRepositoryUri(string suffix)
    {
        string repositoryUri = $"repos/{Uri.EscapeDataString(options.RepositoryOwner)}/{Uri.EscapeDataString(options.RepositoryName)}";

        return string.IsNullOrEmpty(suffix) ? repositoryUri : $"{repositoryUri}/{suffix}";
    }

    private string NormalizeBranch(string? branch)
    {
        string normalizedBranch = string.IsNullOrWhiteSpace(branch) ? options.DefaultBranch : branch;
        const string headsPrefix = "refs/heads/";

        if (normalizedBranch.StartsWith(headsPrefix, StringComparison.Ordinal))
        {
            normalizedBranch = normalizedBranch[headsPrefix.Length..];
        }

        return ContentPathPolicy.NormalizeGitHubPath(normalizedBranch);
    }

    private Uri GetApiBaseUri()
    {
        return options.GitHubApiBaseUri ?? DefaultApiBaseUri;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.RepositoryOwner))
        {
            throw new InvalidOperationException("MARKAZOR_REPO_OWNER is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.RepositoryName))
        {
            throw new InvalidOperationException("MARKAZOR_REPO_NAME is not configured.");
        }
    }

    private static string EscapePath(string path)
    {
        return string.Join('/', path.Split('/').Select(static segment => Uri.EscapeDataString(segment)));
    }

    private static string RemoveWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
