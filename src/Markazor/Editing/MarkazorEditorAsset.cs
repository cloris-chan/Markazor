namespace Markazor.Editing;

public sealed record MarkazorEditorAsset(string RepositoryPath, string MarkdownPath, string ContentType, ReadOnlyMemory<byte> Content);
