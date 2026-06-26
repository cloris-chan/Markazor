using Markazor.Core.GitHub;

namespace Markazor.Editing;

public sealed record MarkazorEditorOperationResult(bool Succeeded, string? Message, MarkazorGitHubClientResultKind? Kind = null, MarkazorEditorConflict? Conflict = null);

public sealed record MarkazorEditorOperationResult<T>(bool Succeeded, T? Value, string? Message, MarkazorGitHubClientResultKind? Kind = null, MarkazorEditorConflict? Conflict = null);
