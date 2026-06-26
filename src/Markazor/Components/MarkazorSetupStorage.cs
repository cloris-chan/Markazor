using Microsoft.JSInterop;

namespace Markazor.Components;

internal static class MarkazorSetupStorage
{
    public const string AuthReturnPathLocalStorageKey = "authReturnPath";

    public static async Task<MarkazorSettingsDraft?> LoadSettingsDraftAsync(
        IJSObjectReference? module)
    {
        if (module is null)
        {
            return null;
        }

        string? serializedDraft = await GetLocalValueAsync(
            module,
            MarkazorSettingsDraft.LocalStorageKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(serializedDraft))
        {
            MarkazorSettingsDraft? draft = MarkazorSettingsDraft.FromStorage(serializedDraft);
            if (draft is not null)
            {
                return draft;
            }
        }

        return null;
    }

    public static async Task<string?> LoadClientIdAsync(IJSObjectReference? module)
    {
        if (module is null)
        {
            return null;
        }

        MarkazorSettingsDraft? draft = await LoadSettingsDraftAsync(module).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(draft?.GitHubClientId))
        {
            return draft.GitHubClientId;
        }

        return await GetLocalValueAsync(
            module,
            MarkazorSettingsDraft.ClientIdLocalStorageKey).ConfigureAwait(false);
    }

    public static Task SaveClientIdAsync(IJSObjectReference? module, string clientId)
    {
        return SetLocalValueAsync(
            module,
            MarkazorSettingsDraft.ClientIdLocalStorageKey,
            clientId);
    }

    public static Task SaveSettingsDraftAsync(
        IJSObjectReference? module,
        MarkazorSettingsDraft draft)
    {
        return SetLocalValueAsync(
            module,
            MarkazorSettingsDraft.LocalStorageKey,
            draft.ToStorage());
    }

    public static Task ClearSettingsDraftAsync(IJSObjectReference? module)
    {
        return SetLocalValueAsync(module, MarkazorSettingsDraft.LocalStorageKey, string.Empty);
    }

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
