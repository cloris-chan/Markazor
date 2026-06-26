using Markazor.Client;
using Markazor.Configuration;
using Markazor.Content;
using Markazor.Editing;
using Markazor.Pwa;
using Markazor.Reading;
using Microsoft.Extensions.DependencyInjection;

namespace Markazor;

public static class MarkazorServiceCollectionExtensions
{
    public static IServiceCollection AddMarkazor(
        this IServiceCollection services,
        Action<MarkazorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        MarkazorOptions options = new();
        configure?.Invoke(options);

        _ = services.AddSingleton(options);
        _ = services.AddScoped<MarkazorClientSession>();
        _ = services.AddScoped<IMarkazorClientSession>(
            static provider => provider.GetRequiredService<MarkazorClientSession>());
        _ = services.AddScoped<MarkazorSetupDiagnosticsService>();
        _ = services.AddScoped<IMarkazorSetupDiagnosticsService>(
            static provider => provider.GetRequiredService<MarkazorSetupDiagnosticsService>());
        _ = services.AddScoped<MarkazorSettingsSyncService>();
        _ = services.AddScoped<IMarkazorSettingsSyncService>(
            static provider => provider.GetRequiredService<MarkazorSettingsSyncService>());
        _ = services.AddScoped<MarkazorContentCatalog>();
        _ = services.AddScoped<IMarkazorContentCatalog>(
            static provider => provider.GetRequiredService<MarkazorContentCatalog>());
        _ = services.AddSingleton<IMarkazorMarkdownRenderer, MarkazorMarkdownRenderer>();
        _ = services.AddScoped<MarkazorReaderService>();
        _ = services.AddScoped<IMarkazorReaderService>(
            static provider => provider.GetRequiredService<MarkazorReaderService>());
        _ = services.AddScoped<MarkazorPwaUpdateService>();
        _ = services.AddScoped<IMarkazorPwaUpdateService>(
            static provider => provider.GetRequiredService<MarkazorPwaUpdateService>());
        _ = services.AddScoped<MarkazorEditorService>();
        _ = services.AddScoped<IMarkazorEditorService>(
            static provider => provider.GetRequiredService<MarkazorEditorService>());

        return services;
    }
}
