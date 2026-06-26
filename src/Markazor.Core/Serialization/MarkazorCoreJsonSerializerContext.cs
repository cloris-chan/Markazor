using System.Text.Json.Serialization;
using Markazor.Core.Auth;
using Markazor.Core.GitHub;
using Markazor.Core.Setup;

namespace Markazor.Core.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GitHubBlobResponse))]
[JsonSerializable(typeof(GitHubCreateBlobRequest))]
[JsonSerializable(typeof(GitHubContentFileResponse))]
[JsonSerializable(typeof(GitHubContentsDeleteRequest))]
[JsonSerializable(typeof(GitHubContentsWriteRequest))]
[JsonSerializable(typeof(GitHubContentsWriteResponse))]
[JsonSerializable(typeof(GitHubCommitResponse))]
[JsonSerializable(typeof(GitHubCreateCommitRequest))]
[JsonSerializable(typeof(GitHubCreateCommitResponse))]
[JsonSerializable(typeof(GitHubCreateTreeRequest))]
[JsonSerializable(typeof(GitHubRefResponse))]
[JsonSerializable(typeof(GitHubRepositoryResponse))]
[JsonSerializable(typeof(GitHubTreeResponse))]
[JsonSerializable(typeof(GitHubUpdateRefRequest))]
[JsonSerializable(typeof(MarkazorGitHubAccessTokenResponse))]
[JsonSerializable(typeof(MarkazorGitHubAuthorizationRequest))]
[JsonSerializable(typeof(MarkazorGitHubAuthorizationResponse))]
[JsonSerializable(typeof(MarkazorGitHubCallbackRequest))]
[JsonSerializable(typeof(MarkazorSiteSettings))]
[JsonSerializable(typeof(MarkazorSetupStatus))]
public sealed partial class MarkazorCoreJsonSerializerContext : JsonSerializerContext;
