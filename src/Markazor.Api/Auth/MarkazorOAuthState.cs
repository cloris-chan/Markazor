namespace Markazor.Api.Auth;

public sealed record MarkazorOAuthState(string State, string CodeVerifier, DateTimeOffset ExpiresAtUtc, string ClientId, Uri? CallbackUri);
