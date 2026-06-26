namespace Markazor.Core.GitHub;

public sealed record MarkazorGitHubTreeFileChange(string Path, string? Content, string? Sha = null, string Mode = "100644", string Type = "blob", bool Delete = false)
{
    public static MarkazorGitHubTreeFileChange Upsert(string path, string content)
    {
        return new MarkazorGitHubTreeFileChange(path, content);
    }

    public static MarkazorGitHubTreeFileChange UpsertBlob(string path, string sha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);

        return new MarkazorGitHubTreeFileChange(path, null, Sha: sha);
    }

    public static MarkazorGitHubTreeFileChange Remove(string path)
    {
        return new MarkazorGitHubTreeFileChange(path, null, Delete: true);
    }
}
