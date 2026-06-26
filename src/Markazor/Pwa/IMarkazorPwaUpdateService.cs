namespace Markazor.Pwa;

public interface IMarkazorPwaUpdateService
{
    Task<bool> WaitForUpdateAsync(CancellationToken cancellationToken = default);

    Task ActivateUpdateAsync(CancellationToken cancellationToken = default);
}
