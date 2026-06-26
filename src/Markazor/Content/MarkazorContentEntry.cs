namespace Markazor.Content;

public sealed record MarkazorContentEntry(ArticleMeta Article, string? Sha, bool ExistsOnGitHub)
{
    public string RelativePath => Article.RelativePath;

    public string Title => Article.Title;

    public bool IsDraft => Article.IsDraft;
}
