namespace Markazor.Core.GitHub;

public sealed record MarkazorGitHubBlob(string Sha, string Encoding, string EncodedContent, string ContentText);
