using System.Text.Json.Serialization;

namespace Markazor.Core.Setup;

public sealed class MarkazorSiteSettings
{
    public MarkazorSitePublicSettings Site { get; init; } = new();

    [JsonPropertyName("github")]
    public MarkazorGitHubSettings GitHub { get; init; } = new();

    public MarkazorRepositorySettings Repository { get; init; } = new();

    public MarkazorThemeSettings Theme { get; init; } = new();
}

public sealed class MarkazorSitePublicSettings
{
    public const string DefaultName = "Markazor";

    public const string DefaultDescription = "A repository-native site powered by Markazor.";

    [JsonPropertyName("name")]
    public string Name { get; init; } = DefaultName;

    public string Description { get; init; } = DefaultDescription;

    public IReadOnlyList<Uri> BaseUrls { get; init; } = [];

    [JsonIgnore]
    public Uri? PrimaryBaseUrl => BaseUrls.Count == 0 ? null : BaseUrls[0];

    public static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
    }

    public static string NormalizeDescription(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultDescription : value.Trim();
    }
}

public sealed class MarkazorGitHubSettings
{
    public string ClientId { get; init; } = string.Empty;
}

public sealed class MarkazorRepositorySettings
{
    public string Owner { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DefaultBranch { get; init; } = "main";
}

public sealed class MarkazorThemeSettings
{
    public string Name { get; init; } = "default";
}
