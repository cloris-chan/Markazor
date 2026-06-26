using System.Text.Json.Serialization;

namespace Markazor.Api.Auth;

public sealed class GitHubOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresInSeconds { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("refresh_token_expires_in")]
    public int? RefreshTokenExpiresInSeconds { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}
