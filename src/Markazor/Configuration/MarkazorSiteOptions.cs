namespace Markazor.Configuration;

public sealed class MarkazorSiteOptions
{
    public string Name { get; set; } = "Markazor Site";

    public string Description { get; set; } = "A repository-native site powered by Markazor.";

    public string Language { get; set; } = "en";

    public int PageSize { get; set; } = 10;
}
