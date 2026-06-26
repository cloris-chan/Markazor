namespace Markazor.Core.Setup;

public sealed record MarkazorSetupStatus(bool Ready, IReadOnlyList<string> MissingSettings, MarkazorSitePublicSettings Site, MarkazorGitHubSettings GitHub, MarkazorRepositoryStatus Repository, MarkazorThemeSettings Theme, MarkazorStaticWebAppsBuildSettings ExpectedStaticWebAppsBuildSettings);
