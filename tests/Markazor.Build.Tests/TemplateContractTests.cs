using System.Text;
using System.Xml.Linq;

namespace Markazor.Build.Tests;

public sealed class TemplateContractTests
{
    private static readonly string TemplateRoot = Path.Combine(AppContext.BaseDirectory, "Template");

    [Theory]
    [InlineData("Web", "Markazor", "Markazor.Themes")]
    [InlineData("Functions", "Markazor.Api")]
    public void ProjectsDeclareNet10AndTheirOwnPackageDependencies(string project, params string[] expectedPackages)
    {
        XDocument document = XDocument.Load(Path.Combine(TemplateRoot, "src", $"MarkazorSite.{project}", $"MarkazorSite.{project}.csproj"));
        Assert.Equal("net10.0", Assert.Single(document.Descendants("TargetFramework")).Value);
        XElement[] references = document.Descendants("PackageReference")
            .Where(static reference => reference.Attribute("Include")!.Value.StartsWith("Markazor", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expectedPackages, references.Select(static reference => reference.Attribute("Include")!.Value).Order(StringComparer.Ordinal));
        Assert.All(references, static reference => Assert.Null(reference.Attribute("ExcludeAssets")));
        Assert.Equal(project == "Web" ? "true" : null, document.Descendants("MarkazorEnableSiteBuild").SingleOrDefault()?.Value);
    }

    [Fact]
    public void TemplateContainsOnlyApplicationScaffolding()
    {
        string[] absent =
        [
            "global.json", "NuGet.config", "README.md", ".gitignore", "public", "content", "posts", "drafts", "assets",
            "src/MarkazorSite.Web/wwwroot", "src/MarkazorSite.Web/staticwebapp.config.json",
            "src/MarkazorSite.Functions/local.settings.json", "src/MarkazorSite.Functions/host.json",
        ];
        Assert.All(absent, path => Assert.False(Path.Exists(Path.Combine(TemplateRoot, path)), path));
        XDocument properties = XDocument.Load(Path.Combine(TemplateRoot, "Directory.Build.props"));
        Assert.Equal("preview", Assert.Single(properties.Descendants("LangVersion")).Value);
    }

    [Fact]
    public void MarkazorDependenciesShareTheTemplateVersion()
    {
        XDocument packages = XDocument.Load(Path.Combine(TemplateRoot, "Directory.Packages.props"));
        foreach (string name in new[] { "Markazor", "Markazor.Api", "Markazor.Themes" })
        {
            XElement package = Assert.Single(packages.Descendants("PackageVersion"), package => package.Attribute("Include")!.Value == name);
            Assert.Equal("MARKAZOR_PACKAGE_VERSION", package.Attribute("Version")!.Value);
        }
    }

    [Fact]
    public void TemplateSourcesUseConsistentLineEndings()
    {
        string[] files = Directory.GetFiles(TemplateRoot, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Contains((byte)0))
            {
                continue;
            }

            string text = Encoding.Latin1.GetString(bytes);
            string unpaired = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
            Assert.False(unpaired.Contains('\r', StringComparison.Ordinal) || (text.Contains("\r\n", StringComparison.Ordinal) && unpaired.Contains('\n', StringComparison.Ordinal)), file);
        }
    }
}
