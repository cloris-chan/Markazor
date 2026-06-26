namespace Markazor.Editing;

public sealed record MarkazorEditorConflict(string Path, string LocalMarkdown, string RemoteMarkdown, string RemoteSha);
