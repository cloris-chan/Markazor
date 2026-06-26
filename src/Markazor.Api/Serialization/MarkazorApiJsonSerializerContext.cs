using System.Text.Json.Serialization;
using Markazor.Api.Auth;

namespace Markazor.Api.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GitHubOAuthTokenResponse))]
[JsonSerializable(typeof(MarkazorGitHubOAuthStartResult))]
[JsonSerializable(typeof(MarkazorGitHubOAuthRefreshResult))]
[JsonSerializable(typeof(MarkazorOAuthState))]
[JsonSerializable(typeof(MarkazorRefreshTokenCookie))]
public sealed partial class MarkazorApiJsonSerializerContext : JsonSerializerContext;
