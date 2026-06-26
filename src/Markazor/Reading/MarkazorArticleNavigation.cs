using Markazor.Content;

namespace Markazor.Reading;

public sealed record MarkazorArticleNavigation(ArticleMeta? PreviousArticle, ArticleMeta? NextArticle);
