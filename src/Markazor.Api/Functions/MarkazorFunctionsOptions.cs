using Markazor.Core.Setup;

namespace Markazor.Api.Functions;

public sealed class MarkazorFunctionsOptions
{
    public MarkazorStaticWebAppsBuildSettings ExpectedStaticWebAppsBuildSettings { get; set; } = new("src/MarkazorSite.Web", "src/MarkazorSite.Functions", "wwwroot");

    public string? SiteSettingsFilePath { get; set; } = MarkazorSiteSettingsLoader.PublishedFileName;
}
