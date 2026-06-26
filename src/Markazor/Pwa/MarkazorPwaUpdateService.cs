using Microsoft.JSInterop;

namespace Markazor.Pwa;

public sealed class MarkazorPwaUpdateService(IJSRuntime jsRuntime) : IMarkazorPwaUpdateService, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Markazor/markazor-pwa.js").AsTask());

    public async Task<bool> WaitForUpdateAsync(CancellationToken cancellationToken = default)
    {
        IJSObjectReference moduleReference = await module.Value.ConfigureAwait(false);

        return await moduleReference.InvokeAsync<bool>("waitForUpdate", cancellationToken).ConfigureAwait(false);
    }

    public async Task ActivateUpdateAsync(CancellationToken cancellationToken = default)
    {
        IJSObjectReference moduleReference = await module.Value.ConfigureAwait(false);
        await moduleReference.InvokeVoidAsync("activateUpdate", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!module.IsValueCreated)
        {
            return;
        }

        try
        {
            IJSObjectReference moduleReference = await module.Value.ConfigureAwait(false);
            await moduleReference.InvokeVoidAsync("dispose").ConfigureAwait(false);
            await moduleReference.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
