namespace Markazor.Content;

public interface IMarkazorMarkdownRenderer
{
    string ToSafeHtml(string markdown);
}
