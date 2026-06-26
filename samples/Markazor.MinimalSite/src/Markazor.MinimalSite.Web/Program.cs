using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Markazor;
using Markazor.Generated;
using Markazor.MinimalSite.Web;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMarkazor(options =>
{
    options.Articles = SiteIndex.Articles;
    options.Site.Name = string.IsNullOrWhiteSpace(SiteIndex.Site.Name) ? "Markazor Minimal Site" : SiteIndex.Site.Name;
    options.Site.Description = string.IsNullOrWhiteSpace(SiteIndex.Site.Description) ? "Notes on building a small, repository-native publishing workflow." : SiteIndex.Site.Description;
    options.Site.Language = "en";
});

await builder.Build().RunAsync().ConfigureAwait(false);
