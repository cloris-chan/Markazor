using System.Diagnostics.CodeAnalysis;
using Markazor.Api.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace MarkazorSite.Functions.Functions;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Azure Functions worker activates function classes through metadata and dependency injection.")]
internal sealed class GitHubAuthFunctions(MarkazorFunctionsEndpointService endpoints)
{
    [Function("GitHubAuthStart")]
    public HttpResponseData Start([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/github/start")] HttpRequestData request)
    {
        return endpoints.StartGitHubAuth(request);
    }

    [Function("GitHubAuthCallback")]
    public Task<HttpResponseData> CallbackAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/github/callback")] HttpRequestData request, CancellationToken cancellationToken)
    {
        return endpoints.CompleteGitHubAuthAsync(request, cancellationToken);
    }

    [Function("GitHubAuthRefresh")]
    public Task<HttpResponseData> RefreshAsync([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/github/refresh")] HttpRequestData request, CancellationToken cancellationToken)
    {
        return endpoints.RefreshGitHubAuthAsync(request, cancellationToken);
    }
}
