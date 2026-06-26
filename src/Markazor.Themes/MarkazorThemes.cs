namespace Markazor.Themes;

public static class MarkazorThemes
{
    public const string DefaultThemeName = "default";

    public const string NoneThemeName = "none";

    public static IReadOnlyList<MarkazorThemeDescriptor> All { get; } =
    [
        new(DefaultThemeName, "Default", "The built-in Markazor visual theme."),
        new(NoneThemeName, "None", "Disable built-in theme output."),
    ];

    public static MarkazorThemeDescriptor? Find(string name)
    {
        return All.FirstOrDefault(theme =>
            string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
