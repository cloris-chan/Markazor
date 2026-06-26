using Markazor.Api.Functions;
using Markazor.Api.Setup;
using Markazor.Core.Setup;

namespace Markazor.Api.Tests;

public sealed class MarkazorSetupStatusServiceTests
{
    [Fact]
    public void FunctionsUsePublishedSiteSettingsPathByDefault()
    {
        MarkazorFunctionsOptions options = new();

        Assert.Equal(MarkazorSiteSettingsLoader.PublishedFileName, options.SiteSettingsFilePath);
    }

    [Fact]
    public void ReportsMissingRequiredSettingsWithoutLeakingSecretValues()
    {
        Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GITHUB_APP_CLIENT_SECRET"] = "super-secret",
        };

        MarkazorSetupStatus status = CreateService(
            environment,
            new MarkazorSiteSettings
            {
                GitHub = new MarkazorGitHubSettings { ClientId = "client-id" },
                Repository = new MarkazorRepositorySettings { Owner = "cloris" },
            }).GetStatus();

        Assert.False(status.Ready);
        Assert.Equal(["repository.name"], status.MissingSettings);
        Assert.Equal("cloris", status.Repository.Owner);
        Assert.Equal(string.Empty, status.Repository.Name);
        Assert.DoesNotContain("super-secret", status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UsesConfiguredValuesAndDefaults()
    {
        Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GITHUB_APP_CLIENT_SECRET"] = "secret",
        };

        MarkazorSetupStatus status = CreateService(
            environment,
            new MarkazorSiteSettings
            {
                GitHub = new MarkazorGitHubSettings { ClientId = "client-id" },
                Repository = new MarkazorRepositorySettings
                {
                    Owner = "owner",
                    Name = "repo",
                },
            }).GetStatus();

        Assert.True(status.Ready);
        Assert.Empty(status.MissingSettings);
        Assert.Equal("main", status.Repository.DefaultBranch);
        Assert.Equal("src/Test.Web", status.ExpectedStaticWebAppsBuildSettings.AppLocation);
    }

    [Fact]
    public void EnvironmentValuesOverrideSiteSettings()
    {
        Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GITHUB_APP_CLIENT_SECRET"] = "secret",
            ["MARKAZOR_REPO_OWNER"] = "env-owner",
            ["MARKAZOR_REPO_NAME"] = "env-repo",
            ["MARKAZOR_DEFAULT_BRANCH"] = "preview",
        };

        MarkazorSetupStatus status = CreateService(
            environment,
            new MarkazorSiteSettings
            {
                GitHub = new MarkazorGitHubSettings { ClientId = "client-id" },
                Repository = new MarkazorRepositorySettings
                {
                    Owner = "settings-owner",
                    Name = "settings-repo",
                    DefaultBranch = "main",
                },
            }).GetStatus();

        Assert.True(status.Ready);
        Assert.Equal("env-owner", status.Repository.Owner);
        Assert.Equal("env-repo", status.Repository.Name);
        Assert.Equal("preview", status.Repository.DefaultBranch);
    }

    [Fact]
    public void TreatsNullSiteSettingsSectionsAsDefaults()
    {
        Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GITHUB_APP_CLIENT_SECRET"] = "secret",
        };

        MarkazorSetupStatus status = CreateService(
            environment,
            new MarkazorSiteSettings
            {
                GitHub = null!,
                Repository = null!,
            }).GetStatus();

        Assert.False(status.Ready);
        Assert.Equal(["github.clientId", "repository.owner", "repository.name"], status.MissingSettings);
        Assert.Equal("main", status.Repository.DefaultBranch);
    }

    [Fact]
    public void LoadsSiteSettingsFromExplicitFilePath()
    {
        string settingsPath = WriteSettingsFile();
        MarkazorSiteSettings settings = MarkazorSiteSettingsLoader.Load(settingsPath);
        Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GITHUB_APP_CLIENT_SECRET"] = "secret",
        };

        Assert.Equal([new Uri("https://example.test/")], settings.Site.BaseUrls);
        Assert.Equal("File Site", settings.Site.Name);
        Assert.Equal("File description", settings.Site.Description);

        MarkazorSetupStatus status = new MarkazorSetupStatusService(
            name => environment.TryGetValue(name, out string? value) ? value : null,
            new MarkazorSetupStatusOptions
            {
                SiteSettingsFilePath = settingsPath,
            }).GetStatus();

        Assert.True(status.Ready);
        Assert.Empty(status.MissingSettings);
        Assert.Equal("File Site", status.Site.Name);
        Assert.Equal("file-owner", status.Repository.Owner);
        Assert.Equal("file-repo", status.Repository.Name);
    }

    [Fact]
    public void LoadsRelativeSiteSettingsFromCurrentDirectoryFallback()
    {
        string settingsDirectory = CreateTemporaryDirectory();
        string settingsPath = Path.Combine(settingsDirectory, MarkazorSiteSettingsLoader.DefaultFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, SettingsJson);
        string originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(settingsDirectory);
            Dictionary<string, string?> environment = new(StringComparer.OrdinalIgnoreCase)
            {
                ["GITHUB_APP_CLIENT_SECRET"] = "secret",
            };

            MarkazorSetupStatus status = new MarkazorSetupStatusService(
                name => environment.TryGetValue(name, out string? value) ? value : null).GetStatus();

            Assert.True(status.Ready);
            Assert.Equal("file-owner", status.Repository.Owner);
            Assert.Equal("file-repo", status.Repository.Name);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void ReturnsDefaultsWhenSiteSettingsJsonIsInvalid()
    {
        string settingsPath = Path.Combine(CreateTemporaryDirectory(), MarkazorSiteSettingsLoader.DefaultFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{");

        MarkazorSiteSettings settings = MarkazorSiteSettingsLoader.Load(settingsPath);

        Assert.Empty(settings.Site.BaseUrls);
        Assert.Equal(string.Empty, settings.GitHub.ClientId);
        Assert.Equal(string.Empty, settings.Repository.Owner);
        Assert.Equal(string.Empty, settings.Repository.Name);
    }

    private static MarkazorSetupStatusService CreateService(
        Dictionary<string, string?> environment,
        MarkazorSiteSettings? siteSettings = null)
    {
        return new MarkazorSetupStatusService(
            name => environment.TryGetValue(name, out string? value) ? value : null,
            new MarkazorSetupStatusOptions
            {
                SiteSettings = siteSettings,
                ExpectedStaticWebAppsBuildSettings = new MarkazorStaticWebAppsBuildSettings(
                    "src/Test.Web",
                    "src/Test.Functions",
                    "wwwroot"),
            });
    }

    private static string WriteSettingsFile()
    {
        string settingsPath = Path.Combine(CreateTemporaryDirectory(), MarkazorSiteSettingsLoader.DefaultFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, SettingsJson);

        return settingsPath;
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "markazor-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
    }

    private const string SettingsJson = """
        {
          "site": {
            "name": "File Site",
            "description": "File description",
            "baseUrls": [
              "https://example.test/"
            ]
          },
          "github": {
            "clientId": "file-client"
          },
          "repository": {
            "owner": "file-owner",
            "name": "file-repo",
            "defaultBranch": "main"
          }
        }
        """;
}
