using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Markazor.Core.Setup;

sealed partial class MarkazorSiteSettings
{
    public MarkazorSitePublicSettings Site { get; set; } = new();

    [JsonPropertyName("github")]
    public MarkazorGitHubSettings GitHub { get; set; } = new();

    public MarkazorRepositorySettings Repository { get; set; } = new();

    public MarkazorThemeSettings Theme { get; set; } = new();
}

sealed partial class MarkazorSitePublicSettings
{
    public const string DefaultName = "Markazor";

    public const string DefaultDescription = "A repository-native site powered by Markazor.";

    [JsonPropertyName("name")]
    public string Name { get; set => field = NormalizeName(value); } = DefaultName;

    public string Description { get; set => field = NormalizeDescription(value); } = DefaultDescription;

    public IReadOnlyList<Uri> BaseUrls { get; set; } = [];

    [JsonIgnore]
    public Uri? PrimaryBaseUrl => BaseUrls is { Count: > 0 } ? BaseUrls[0] : null;

    public static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultName : value!.Trim();
    }

    public static string NormalizeDescription(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DefaultDescription : value!.Trim();
    }
}

sealed partial class MarkazorGitHubSettings
{
    public string ClientId { get; set => field = value?.Trim() ?? string.Empty; } = string.Empty;
}

sealed partial class MarkazorRepositorySettings
{
    public string Owner { get; set => field = value?.Trim() ?? string.Empty; } = string.Empty;

    public string Name { get; set => field = value?.Trim() ?? string.Empty; } = string.Empty;

    public string DefaultBranch { get; set => field = string.IsNullOrWhiteSpace(value) ? "main" : value.Trim(); } = "main";
}

sealed partial class MarkazorThemeSettings
{
    public string Name { get; set => field = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim(); } = "default";
}
