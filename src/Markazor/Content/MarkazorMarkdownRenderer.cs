namespace Markazor.Content;

public sealed class MarkazorMarkdownRenderer : IMarkazorMarkdownRenderer
{
    public string ToSafeHtml(string markdown)
    {
        return MarkdownContent.ToSafeHtml(markdown);
    }
}
