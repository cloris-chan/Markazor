using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Markazor.Api.Functions;

public static class MarkazorFunctionsApplicationBuilderExtensions
{
    public static FunctionsApplicationBuilder UseMarkazor(this FunctionsApplicationBuilder builder, Action<MarkazorFunctionsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        MarkazorFunctionsOptions options = new();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<MarkazorFunctionsEndpointService>();

        return builder;
    }
}
