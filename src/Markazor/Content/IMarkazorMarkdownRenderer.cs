namespace Markazor.Content;

public interface IMarkazorMarkdownRenderer
{
    MarkdownRenderResult Render(string markdown, string? articleTitle = null);
}
