using Markazor.Reading;

namespace Markazor.Tests;

public sealed class MarkazorReaderRoutesTests
{
    [Fact]
    public void TaxonomyRoutesAreCanonicalAndUriEscaped()
    {
        Assert.Equal("/tags/c%23%20notes", MarkazorReaderRoutes.Tag(" C# Notes "));
        Assert.Equal("/categories/reading%20list", MarkazorReaderRoutes.Category("Reading List"));
    }
}
