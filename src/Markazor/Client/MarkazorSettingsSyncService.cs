using System.Text.Json;
using Markazor.Core.GitHub;
using Markazor.Core.Security;
using Markazor.Core.Setup;

namespace Markazor.Client;

public sealed class MarkazorSettingsSyncService(
    MarkazorClientSession session) : IMarkazorSettingsSyncService
{
    private const string SettingsPath = MarkazorRepositoryPaths.SettingsPath;

    public async Task<MarkazorSettingsSyncResult> SaveAsync(
        MarkazorSiteSettings settings,
        MarkazorSettingsAsset? siteIcon = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (session.SetupStatus is null)
        {
            _ = await session.LoadSetupStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!HasRepositoryConfiguration(settings))
        {
            return Failed("Repository setup is incomplete.");
        }

        string? accessToken = await session.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Failed("GitHub authorization is required.", MarkazorGitHubClientResultKind.Unauthorized);
        }

        string content = MarkazorSiteSettingsJson.Serialize(settings);

        if (siteIcon is not null)
        {
            return await SaveWithSiteIconAsync(settings, content, siteIcon, cancellationToken).ConfigureAwait(false);
        }

        MarkazorGitHubClientResult<MarkazorGitHubContentFile> current = await ExecuteGitHubAsync(
            settings,
            static (client, token, tokenCancellation) => client.ReadFileAsync(
                    SettingsPath,
                    token,
                    cancellationToken: tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (current.Succeeded && current.Value is not null)
        {
            if (SettingsMatch(current.Value.ContentText, content))
            {
                return new MarkazorSettingsSyncResult(true, null, "Settings already match the repository.");
            }

            return await UpdateAsync(
                content,
                current.Value.Sha,
                settings,
                cancellationToken).ConfigureAwait(false);
        }

        if (current.Kind != MarkazorGitHubClientResultKind.NotFound)
        {
            return Failed(current);
        }

        return await CreateAsync(content, settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MarkazorSettingsSyncResult> SaveWithSiteIconAsync(
        MarkazorSiteSettings settings,
        string content,
        MarkazorSettingsAsset siteIcon,
        CancellationToken cancellationToken)
    {
        string normalizedIconPath;
        try
        {
            normalizedIconPath = ContentPathPolicy.NormalizeGitHubPath(siteIcon.RepositoryPath);
        }
        catch (ArgumentException ex)
        {
            return Failed(ex.Message);
        }

        if (!string.Equals(normalizedIconPath, MarkazorRepositoryPaths.SiteIconPath, StringComparison.Ordinal))
        {
            return Failed($"Site icon must be saved to '{MarkazorRepositoryPaths.SiteIconPath}'.");
        }

        if (!string.Equals(siteIcon.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("Site icon must be a PNG file.");
        }

        if (!MarkazorPngSignature.Matches(siteIcon.Content))
        {
            return Failed("Site icon must be a valid PNG file.");
        }

        string branchName = settings.Repository.DefaultBranch;
        MarkazorGitHubClientResult<MarkazorGitHubRef> branch = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.GetBranchRefAsync(branchName, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!branch.Succeeded || string.IsNullOrWhiteSpace(branch.Value?.Sha))
        {
            return Failed(branch);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> baseCommit = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.GetCommitAsync(branch.Value.Sha, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!baseCommit.Succeeded || string.IsNullOrWhiteSpace(baseCommit.Value?.TreeSha))
        {
            return Failed(baseCommit);
        }

        MarkazorGitHubClientResult<MarkazorGitHubBlob> settingsBlob = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.CreateBlobAsync(System.Text.Encoding.UTF8.GetBytes(content), token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!settingsBlob.Succeeded || string.IsNullOrWhiteSpace(settingsBlob.Value?.Sha))
        {
            return Failed(settingsBlob);
        }

        MarkazorGitHubClientResult<MarkazorGitHubBlob> iconBlob = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.CreateBlobAsync(siteIcon.Content, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!iconBlob.Succeeded || string.IsNullOrWhiteSpace(iconBlob.Value?.Sha))
        {
            return Failed(iconBlob);
        }

        MarkazorGitHubClientResult<MarkazorGitHubTree> tree = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.CreateTreeAsync(
                baseCommit.Value.TreeSha,
                [
                    MarkazorGitHubTreeFileChange.UpsertBlob(SettingsPath, settingsBlob.Value.Sha),
                    MarkazorGitHubTreeFileChange.UpsertBlob(MarkazorRepositoryPaths.SiteIconPath, iconBlob.Value.Sha),
                ],
                token,
                tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!tree.Succeeded || string.IsNullOrWhiteSpace(tree.Value?.Sha))
        {
            return Failed(tree);
        }

        MarkazorGitHubClientResult<MarkazorGitHubCommit> commit = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.CreateCommitAsync("Configure Markazor settings", tree.Value.Sha, branch.Value.Sha, token, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.Value?.Sha))
        {
            return Failed(commit);
        }

        MarkazorGitHubClientResult<MarkazorGitHubRef> update = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => client.UpdateBranchRefAsync(branchName, commit.Value.Sha, token, cancellationToken: tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        return update.Succeeded
            ? new MarkazorSettingsSyncResult(true, commit.Value.Sha, null)
            : Failed(update);
    }

    private Task<MarkazorSettingsSyncResult> CreateAsync(
        string content,
        MarkazorSiteSettings settings,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            static (client, token, markdown, tokenCancellation) => client.CreateSettingsFileAsync(
                markdown,
                "Configure Markazor settings",
                token,
                cancellationToken: tokenCancellation),
            content,
            settings,
            cancellationToken);
    }

    private Task<MarkazorSettingsSyncResult> UpdateAsync(
        string content,
        string sha,
        MarkazorSiteSettings settings,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            (client, token, markdown, tokenCancellation) => client.UpdateSettingsFileAsync(
                markdown,
                "Configure Markazor settings",
                sha,
                token,
                cancellationToken: tokenCancellation),
            content,
            settings,
            cancellationToken);
    }

    private async Task<MarkazorSettingsSyncResult> MutateAsync(
        Func<MarkazorGitHubRepositoryClient, string, string, CancellationToken, Task<MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult>>> operation,
        string content,
        MarkazorSiteSettings settings,
        CancellationToken cancellationToken)
    {
        MarkazorGitHubClientResult<MarkazorGitHubContentMutationResult> result = await ExecuteGitHubAsync(
            settings,
            (client, token, tokenCancellation) => operation(client, token, content, tokenCancellation),
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? new MarkazorSettingsSyncResult(true, result.Value?.CommitSha, null)
            : Failed(result);
    }

    private async Task<MarkazorGitHubClientResult<T>> ExecuteGitHubAsync<T>(
        MarkazorSiteSettings settings,
        Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        string? accessToken = await session.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new MarkazorGitHubClientResult<T>(
                MarkazorGitHubClientResultKind.Unauthorized,
                default,
                System.Net.HttpStatusCode.Unauthorized,
                "GitHub authorization is required.");
        }

        MarkazorGitHubRepositoryClient client = session.CreateRepositoryClient(settings);
        MarkazorGitHubClientResult<T> result = await operation(
            client,
            accessToken,
            cancellationToken).ConfigureAwait(false);

        return result.Kind == MarkazorGitHubClientResultKind.Unauthorized
            ? await RetryAfterRefreshAsync(client, operation, cancellationToken).ConfigureAwait(false)
            : result;
    }

    private async Task<MarkazorGitHubClientResult<T>> RetryAfterRefreshAsync<T>(
        MarkazorGitHubRepositoryClient client,
        Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        session.ClearAccessTokenForRetry();
        bool refreshed = await session.RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!refreshed || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return new MarkazorGitHubClientResult<T>(
                MarkazorGitHubClientResultKind.Unauthorized,
                default,
                System.Net.HttpStatusCode.Unauthorized,
                "GitHub authorization is required.");
        }

        return await operation(client, session.AccessToken, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasRepositoryConfiguration(MarkazorSiteSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.Repository.Owner)
            && !string.IsNullOrWhiteSpace(settings.Repository.Name)
            && !string.IsNullOrWhiteSpace(settings.Repository.DefaultBranch);
    }

    private static bool SettingsMatch(
        string currentContent,
        string serializedSettings)
    {
        if (string.Equals(currentContent, serializedSettings, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return string.Equals(MarkazorSiteSettingsJson.Serialize(MarkazorSiteSettingsJson.Parse(currentContent)), serializedSettings, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static MarkazorSettingsSyncResult Failed<T>(MarkazorGitHubClientResult<T> result)
    {
        return Failed(
            string.IsNullOrWhiteSpace(result.Message) ? "Unable to save Markazor settings." : result.Message,
            result.Kind);
    }

    private static MarkazorSettingsSyncResult Failed(
        string message,
        MarkazorGitHubClientResultKind? kind = null)
    {
        return new MarkazorSettingsSyncResult(false, null, message, kind);
    }
}
