using Markazor.Core.Setup;

namespace Markazor.Components;

internal sealed record MarkazorSettingsDraft(
    string SiteBaseUrls,
    string SiteName,
    string SiteDescription,
    string GitHubClientId,
    string RepositoryOwner,
    string RepositoryName,
    string DefaultBranch,
    string ThemeName)
{
    private const char StorageSeparator = '\t';

    public const string LocalStorageKey = "settingsDraft";

    public const string ClientIdLocalStorageKey = "githubClientId";

    public static MarkazorSettingsDraft Empty { get; } = CreateDefaults(string.Empty);

    public static MarkazorSettingsDraft CreateDefaults(string siteBaseUrl)
    {
        return new MarkazorSettingsDraft(
            siteBaseUrl,
            MarkazorSitePublicSettings.DefaultName,
            MarkazorSitePublicSettings.DefaultDescription,
            string.Empty,
            string.Empty,
            string.Empty,
            "main",
            "default");
    }

    public static MarkazorSettingsDraft FromStatus(
        MarkazorSetupStatus status,
        string fallbackSiteBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new MarkazorSettingsDraft(
            FormatBaseUrls(status.Site.BaseUrls, fallbackSiteBaseUrl),
            MarkazorSitePublicSettings.NormalizeName(status.Site.Name),
            MarkazorSitePublicSettings.NormalizeDescription(status.Site.Description),
            status.GitHub.ClientId,
            status.Repository.Owner,
            status.Repository.Name,
            status.Repository.DefaultBranch,
            status.Theme.Name);
    }

    public MarkazorSiteSettings ToSettings()
    {
        IReadOnlyList<Uri> baseUrls = ParseBaseUrls(SiteBaseUrls);

        return new MarkazorSiteSettings
        {
            Site = new MarkazorSitePublicSettings
            {
                Name = MarkazorSitePublicSettings.NormalizeName(SiteName),
                Description = MarkazorSitePublicSettings.NormalizeDescription(SiteDescription),
                BaseUrls = baseUrls,
            },
            GitHub = new MarkazorGitHubSettings
            {
                ClientId = GitHubClientId.Trim(),
            },
            Repository = new MarkazorRepositorySettings
            {
                Owner = RepositoryOwner.Trim(),
                Name = RepositoryName.Trim(),
                DefaultBranch = DefaultBranch.Trim(),
            },
            Theme = new MarkazorThemeSettings
            {
                Name = string.IsNullOrWhiteSpace(ThemeName) ? "default" : ThemeName.Trim(),
            },
        };
    }

    public string? Validate()
    {
        string? invalidBaseUrl = FindInvalidBaseUrl(SiteBaseUrls);
        if (invalidBaseUrl is not null)
        {
            return $"Site Base URL '{invalidBaseUrl}' is not a valid HTTP or HTTPS URL.";
        }

        if (ParseBaseUrls(SiteBaseUrls).Count == 0)
        {
            return "At least one Site Base URL is required.";
        }

        if (string.IsNullOrWhiteSpace(SiteName))
        {
            return "Site Title is required.";
        }

        if (string.IsNullOrWhiteSpace(SiteDescription))
        {
            return "Site Description is required.";
        }

        if (string.IsNullOrWhiteSpace(GitHubClientId))
        {
            return "GitHub App Client ID is required.";
        }

        if (string.IsNullOrWhiteSpace(RepositoryOwner))
        {
            return "Repository Owner is required.";
        }

        if (string.IsNullOrWhiteSpace(RepositoryName))
        {
            return "Repository Name is required.";
        }

        if (string.IsNullOrWhiteSpace(DefaultBranch))
        {
            return "Default Branch is required.";
        }

        return null;
    }

    public string ToStorage()
    {
        string[] values =
        [
            SiteBaseUrls,
            SiteName,
            SiteDescription,
            GitHubClientId,
            RepositoryOwner,
            RepositoryName,
            DefaultBranch,
            ThemeName,
        ];

        return string.Join(StorageSeparator, values.Select(static value =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))));
    }

    public static MarkazorSettingsDraft? FromStorage(string value)
    {
        try
        {
            string[] parts = value.Split(StorageSeparator);
            if (parts.Length != 8)
            {
                return null;
            }

            string[] values = [.. parts.Select(static part =>
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(part)))];

            return new MarkazorSettingsDraft(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                values[6],
                values[7]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static Uri? ParseBaseUrl(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? uri
            : null;
    }

    public static IReadOnlyList<Uri> ParseBaseUrls(string value)
    {
        return
        [
            .. value
                .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static candidate => ParseBaseUrl(candidate))
                .Where(static uri => uri is not null && uri.IsAbsoluteUri)
                .Select(static uri => uri!)
                .DistinctBy(static uri => uri.ToString().TrimEnd('/'), StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static string? FindInvalidBaseUrl(string value)
    {
        foreach (string candidate in value.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParseBaseUrl(candidate) is null)
            {
                return candidate;
            }
        }

        return null;
    }

    public static string FormatBaseUrls(IReadOnlyList<Uri>? baseUrls, string fallback)
    {
        return baseUrls is null || baseUrls.Count == 0
            ? fallback
            : string.Join(Environment.NewLine, baseUrls.Select(static url => url.ToString()));
    }

}
