using Markazor.Content;

namespace Markazor.Configuration;

public sealed class MarkazorOptions
{
    public MarkazorSiteOptions Site { get; } = new();

    public IReadOnlyList<ArticleMeta> Articles { get; set; } = [];
}
