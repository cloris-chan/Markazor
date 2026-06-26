using System.Diagnostics.CodeAnalysis;

namespace MarkazorSite.Web;

[SuppressMessage(
    "Maintainability",
    "CA1515",
    Justification = "Razor generates the root component as a public partial type.")]
public partial class App
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Home))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Posts))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Post))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Notes))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Note))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Categories))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Tags))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Archive))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Pages.NotFound))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Markazor.Components.SetupPage))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Markazor.Components.ManagePage))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Markazor.Components.EditorPage))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(Markazor.Components.GitHubCallbackPage))]
    public App()
    {
    }
}
