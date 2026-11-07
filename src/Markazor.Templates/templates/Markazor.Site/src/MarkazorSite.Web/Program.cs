using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Markazor;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.UseMarkazor();

await builder.Build().RunAsync().ConfigureAwait(false);
