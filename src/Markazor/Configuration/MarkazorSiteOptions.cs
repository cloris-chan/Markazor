namespace Markazor.Configuration;

public sealed class MarkazorSiteOptions
{
    public string Name { get; set; } = "Markazor Site";

    public string Description { get; set; } = "A repository-native site powered by Markazor.";

    public Uri? BaseUrl { get; set; }

    public int PageSize { get; set; } = 10;
}
