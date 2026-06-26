using System.Net;

namespace Markazor.Core.GitHub;

public sealed record MarkazorGitHubClientResult<T>(MarkazorGitHubClientResultKind Kind, T? Value, HttpStatusCode StatusCode, string? Message)
{
    public bool Succeeded => Kind == MarkazorGitHubClientResultKind.Success;
}
