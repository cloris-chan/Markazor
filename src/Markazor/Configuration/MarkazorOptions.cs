using Markazor.Content;

namespace Markazor.Configuration;

public sealed class MarkazorOptions
{
    public MarkazorSiteOptions Site { get; } = new();

    public string SetupPath { get; set; } = "/setup";

    public string EditorPath { get; set; } = "/editor";

    public IReadOnlyList<ArticleMeta> Articles { get; set; } = [];
}
