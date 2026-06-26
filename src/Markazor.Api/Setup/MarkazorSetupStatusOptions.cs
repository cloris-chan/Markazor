using Markazor.Core.Setup;

namespace Markazor.Api.Setup;

public sealed class MarkazorSetupStatusOptions
{
    public MarkazorStaticWebAppsBuildSettings ExpectedStaticWebAppsBuildSettings { get; init; } = new("src/MarkazorSite.Web", "src/MarkazorSite.Functions", "wwwroot");

    public MarkazorSiteSettings? SiteSettings { get; init; }

    public string? SiteSettingsFilePath { get; init; } = MarkazorSiteSettingsLoader.DefaultFileName;
}
