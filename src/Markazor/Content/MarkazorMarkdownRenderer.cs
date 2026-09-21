namespace Markazor.Content;

public sealed class MarkazorMarkdownRenderer : IMarkazorMarkdownRenderer
{
    public MarkdownRenderResult Render(string markdown, string? articleTitle = null)
    {
        return MarkdownContent.Render(markdown, articleTitle);
    }
}
