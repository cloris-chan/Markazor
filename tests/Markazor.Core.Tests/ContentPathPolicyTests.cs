using Markazor.Core.Configuration;
using Markazor.Core.Security;

namespace Markazor.Core.Tests;

public sealed class ContentPathPolicyTests
{
    [Theory]
    [InlineData("posts/hello.md")]
    [InlineData("notes/quick.md")]
    [InlineData("drafts\\draft.md")]
    [InlineData("assets/image.png")]
    public void AllowsFixedContentRoots(string path)
    {
        Assert.True(ContentPathPolicy.IsRepositoryWriteAllowed(path));
    }

    [Theory]
    [InlineData("posts-evil/hello.md")]
    [InlineData("notes-evil/hello.md")]
    [InlineData("assets-evil/image.png")]
    [InlineData("assets")]
    [InlineData("posts")]
    [InlineData("notes")]
    [InlineData("posts/../drafts/escape.md")]
    [InlineData("/posts/absolute.md")]
    public void RejectsBoundaryBypassAndUnsafePaths(string path)
    {
        Assert.False(ContentPathPolicy.IsRepositoryWriteAllowed(path));
    }

    [Theory]
    [InlineData(".github/workflows/deploy.yml")]
    [InlineData(".github/markazor.md")]
    [InlineData("public/_framework/blazor.js")]
    [InlineData("public/_content/Markazor/markazor.css")]
    [InlineData("public/_markazor/content/posts/hello.md")]
    [InlineData("public/assets/image.png")]
    [InlineData("public/service-worker-assets.js")]
    public void RejectsReservedAndInternalPaths(string path)
    {
        Assert.False(ContentPathPolicy.IsRepositoryWriteAllowed(path));
    }

}
