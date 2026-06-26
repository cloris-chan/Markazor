using Markazor.Api.Functions;
using Markazor.Core.Setup;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.UseMarkazor(options =>
{
    options.ExpectedStaticWebAppsBuildSettings = new MarkazorStaticWebAppsBuildSettings("src/MarkazorSite.Web", "src/MarkazorSite.Functions", "wwwroot");
});

using IHost host = builder.Build();
host.Run();
