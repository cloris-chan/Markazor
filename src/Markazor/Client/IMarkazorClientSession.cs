using Markazor.Core.Auth;
using Markazor.Core.GitHub;
using Markazor.Core.Setup;

namespace Markazor.Client;

public interface IMarkazorClientSession
{
    MarkazorSetupStatus? SetupStatus { get; }

    string? AccessToken { get; }

    DateTimeOffset? AccessTokenExpiresAtUtc { get; }

    bool IsReady { get; }

    bool IsAuthenticated { get; }

    Task<MarkazorSetupStatus?> LoadSetupStatusAsync(CancellationToken cancellationToken = default);

    Task<MarkazorGitHubAuthorizationResponse> StartGitHubAuthorizationAsync(
        MarkazorGitHubAuthorizationRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteGitHubAuthorizationAsync(
        MarkazorGitHubCallbackRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<MarkazorGitHubClientResult<T>> ExecuteGitHubAsync<T>(
        Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
        CancellationToken cancellationToken = default);
}
