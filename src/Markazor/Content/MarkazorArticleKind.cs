namespace Markazor.Content;

public static class MarkazorArticleKind
{
    public const string Post = "post";

    public const string Note = "note";

    public static string Normalize(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return Post;
        }

        string trimmedKind = kind.Trim();

        if (string.Equals(trimmedKind, Post, StringComparison.OrdinalIgnoreCase))
        {
            return Post;
        }

        if (string.Equals(trimmedKind, Note, StringComparison.OrdinalIgnoreCase))
        {
            return Note;
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Supported Markazor article kinds are 'post' and 'note'.");
    }
}
