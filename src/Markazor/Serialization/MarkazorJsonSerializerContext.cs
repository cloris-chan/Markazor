using System.Text.Json.Serialization;
using Markazor.Core.Auth;
using Markazor.Core.Setup;

namespace Markazor.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MarkazorGitHubAccessTokenResponse))]
[JsonSerializable(typeof(MarkazorGitHubAuthorizationRequest))]
[JsonSerializable(typeof(MarkazorGitHubAuthorizationResponse))]
[JsonSerializable(typeof(MarkazorGitHubCallbackRequest))]
[JsonSerializable(typeof(MarkazorSiteSettings))]
[JsonSerializable(typeof(MarkazorSetupStatus))]
public sealed partial class MarkazorJsonSerializerContext : JsonSerializerContext;
