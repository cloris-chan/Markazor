using Markazor.Content;

namespace Markazor.Reading;

public sealed record MarkazorArchiveGroup(int Year, int Month, IReadOnlyList<ArticleMeta> Articles);
