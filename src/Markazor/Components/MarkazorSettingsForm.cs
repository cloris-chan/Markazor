using Markazor.Core.Setup;

namespace Markazor.Components;

internal sealed record MarkazorSettingsForm(
    string SiteBaseUrls,
    string SiteName,
    string SiteDescription,
    string GitHubClientId,
    string RepositoryOwner,
    string RepositoryName,
    string DefaultBranch,
    string ThemeName)
{
    public static MarkazorSettingsForm Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "default");

    public static MarkazorSettingsForm FromStatus(MarkazorSetupStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new MarkazorSettingsForm(
            FormatBaseUrls(status.Site.BaseUrls),
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
            return $"Site Base URL '{invalidBaseUrl}' must be an HTTP or HTTPS origin without a path, credentials, query, or fragment.";
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

    public static Uri? ParseBaseUrl(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
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

    public static string FormatBaseUrls(IReadOnlyList<Uri>? baseUrls)
    {
        return baseUrls is null || baseUrls.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, baseUrls.Select(static url => url.ToString()));
    }

}
