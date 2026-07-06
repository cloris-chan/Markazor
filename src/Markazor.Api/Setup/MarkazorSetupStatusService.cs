using Markazor.Core.Setup;

namespace Markazor.Api.Setup;

public sealed class MarkazorSetupStatusService(
    Func<string, string?>? readEnvironment = null,
    MarkazorSetupStatusOptions? options = null)
{
    private readonly Func<string, string?> readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    private readonly MarkazorSetupStatusOptions options = options ?? new MarkazorSetupStatusOptions();

    public MarkazorSetupStatus GetStatus()
    {
        (MarkazorSiteSettings siteSettings, bool settingsFileExists) = GetSiteSettings();
        MarkazorSitePublicSettings sitePublicSettings = Normalize(siteSettings.Site);
        MarkazorGitHubSettings gitHubSettings = siteSettings.GitHub ?? new MarkazorGitHubSettings();
        MarkazorRepositorySettings repositorySettings = siteSettings.Repository ?? new MarkazorRepositorySettings();
        MarkazorThemeSettings themeSettings = siteSettings.Theme ?? new MarkazorThemeSettings();
        string clientId = gitHubSettings.ClientId;
        string repositoryOwner = repositorySettings.Owner;
        string repositoryName = repositorySettings.Name;
        string defaultBranch = repositorySettings.DefaultBranch;
        string[] missingSettings = [.. GetMissingSettings(clientId, repositoryOwner, repositoryName)];

        return new MarkazorSetupStatus(
            missingSettings.Length == 0,
            missingSettings,
            sitePublicSettings,
            new MarkazorGitHubSettings { ClientId = clientId, },
            new MarkazorRepositoryStatus(repositoryOwner, repositoryName, string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch),
            new MarkazorThemeSettings { Name = string.IsNullOrWhiteSpace(themeSettings.Name) ? "default" : themeSettings.Name, },
            options.ExpectedStaticWebAppsBuildSettings,
            settingsFileExists);
    }

    private (MarkazorSiteSettings Settings, bool SettingsFileExists) GetSiteSettings()
    {
        if (options.SiteSettings is not null)
        {
            return (options.SiteSettings, true);
        }

        return (
            MarkazorSiteSettingsLoader.Load(options.SiteSettingsFilePath),
            MarkazorSiteSettingsLoader.Exists(options.SiteSettingsFilePath));
    }

    private static MarkazorSitePublicSettings Normalize(MarkazorSitePublicSettings? settings)
    {
        if (settings is null)
        {
            return new MarkazorSitePublicSettings();
        }

        return new MarkazorSitePublicSettings
        {
            Name = MarkazorSitePublicSettings.NormalizeName(settings.Name),
            Description = MarkazorSitePublicSettings.NormalizeDescription(settings.Description),
            BaseUrls = settings.BaseUrls ?? [],
        };
    }

    private IEnumerable<string> GetMissingSettings(string clientId, string repositoryOwner, string repositoryName)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            yield return "github.clientId";
        }

        if (string.IsNullOrWhiteSpace(readEnvironment("GITHUB_APP_CLIENT_SECRET")))
        {
            yield return "GITHUB_APP_CLIENT_SECRET";
        }

        if (string.IsNullOrWhiteSpace(repositoryOwner))
        {
            yield return "repository.owner";
        }

        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            yield return "repository.name";
        }
    }

}
