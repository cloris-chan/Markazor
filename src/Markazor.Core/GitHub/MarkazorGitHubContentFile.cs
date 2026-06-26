namespace Markazor.Core.GitHub;

public sealed record MarkazorGitHubContentFile(string Path, string Sha, string Encoding, string EncodedContent, string ContentText);
