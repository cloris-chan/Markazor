using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Markazor;
using Markazor.Generated;
using MarkazorSite.Web;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMarkazor(options =>
{
    options.Articles = SiteIndex.Articles;
    options.Site.Name = string.IsNullOrWhiteSpace(SiteIndex.Site.Name) ? "MarkazorSite" : SiteIndex.Site.Name;
    options.Site.Description = string.IsNullOrWhiteSpace(SiteIndex.Site.Description) ? "A repository-native site powered by Markazor." : SiteIndex.Site.Description;
    options.Site.Language = "en";
});

await builder.Build().RunAsync().ConfigureAwait(false);
