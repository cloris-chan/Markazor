namespace Markazor.Themes;

public sealed record MarkazorThemeDescriptor(
    string Name,
    string DisplayName,
    string Description,
    bool IsBuiltIn = true);
