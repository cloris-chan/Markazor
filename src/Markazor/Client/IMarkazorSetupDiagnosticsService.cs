namespace Markazor.Client;

public interface IMarkazorSetupDiagnosticsService
{
    Task<MarkazorRepositoryDiagnostics> RunAsync(
        CancellationToken cancellationToken = default);
}
