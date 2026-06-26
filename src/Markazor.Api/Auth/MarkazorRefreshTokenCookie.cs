namespace Markazor.Api.Auth;

public sealed record MarkazorRefreshTokenCookie(string RefreshToken, DateTimeOffset? ExpiresAtUtc, string ClientId);
