using Markazor.Core.Setup;

namespace Markazor.Client;

public interface IMarkazorSettingsSyncService
{
    Task<MarkazorSettingsSyncResult> SaveAsync(
        MarkazorSiteSettings settings,
        MarkazorSettingsAsset? siteIcon = null,
        CancellationToken cancellationToken = default);
}
