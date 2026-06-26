using Markazor.Core.Setup;

namespace Markazor.Client;

internal static class MarkazorSetupReadiness
{
    private const string PersistedGitHubClientIdSetting = "github.clientId";
    private const string RepositoryOwnerSetting = "repository.owner";
    private const string RepositoryNameSetting = "repository.name";

    public static bool HasRepositoryConfiguration(MarkazorSetupStatus? status)
    {
        return status is not null
            && status.MissingSettings.All(static setting =>
                string.Equals(setting, PersistedGitHubClientIdSetting, StringComparison.Ordinal));
    }

    public static bool IsPersistedGitHubClientIdMissing(MarkazorSetupStatus? status)
    {
        return status?.MissingSettings.Contains(PersistedGitHubClientIdSetting, StringComparer.Ordinal) == true;
    }

    public static bool HasAuthorizationConfiguration(MarkazorSetupStatus? status)
    {
        return status is not null
            && status.MissingSettings.All(static setting =>
                string.Equals(setting, PersistedGitHubClientIdSetting, StringComparison.Ordinal)
                || string.Equals(setting, RepositoryOwnerSetting, StringComparison.Ordinal)
                || string.Equals(setting, RepositoryNameSetting, StringComparison.Ordinal));
    }
}
