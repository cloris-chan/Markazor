using Microsoft.JSInterop;

namespace Markazor.Components;

internal static class MarkazorSetupStorage
{
    public const string AuthReturnPathLocalStorageKey = "authReturnPath";

    public static Task<string?> LoadAuthorizationReturnPathAsync(IJSObjectReference? module)
    {
        return GetLocalValueAsync(module, AuthReturnPathLocalStorageKey);
    }

    public static Task SaveAuthorizationReturnPathAsync(IJSObjectReference? module, string returnPath)
    {
        return SetLocalValueAsync(module, AuthReturnPathLocalStorageKey, returnPath);
    }

    public static Task ClearAuthorizationReturnPathAsync(IJSObjectReference? module)
    {
        return SetLocalValueAsync(module, AuthReturnPathLocalStorageKey, string.Empty);
    }

    public static async Task<string?> GetLocalValueAsync(
        IJSObjectReference? module,
        string key)
    {
        if (module is null)
        {
            return null;
        }

        try
        {
            return await module.InvokeAsync<string?>("getLocalValue", key).ConfigureAwait(false);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public static async Task SetLocalValueAsync(
        IJSObjectReference? module,
        string key,
        string value)
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("setLocalValue", key, value).ConfigureAwait(false);
        }
        catch (JSException)
        {
        }
    }
}
