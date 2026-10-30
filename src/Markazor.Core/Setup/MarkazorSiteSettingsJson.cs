using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Markazor.Core.Setup;

static partial class MarkazorSiteSettingsJson
{
    public static MarkazorSiteSettings Parse(string json)
    {
        MarkazorSiteSettings settings = JsonSerializer.Deserialize(json, SiteSettingsJsonContext.Default.MarkazorSiteSettings)
            ?? throw new JsonException("Site settings must be an object.");
        Validate(settings);
        return settings;
    }

    public static string Serialize(MarkazorSiteSettings settings)
    {
        Validate(settings);
        return JsonSerializer.Serialize(settings, SiteSettingsJsonContext.Default.MarkazorSiteSettings) + "\n";
    }

    public static void Validate(MarkazorSiteSettings settings)
    {
        if (settings is null || settings.Site is null || settings.GitHub is null || settings.Repository is null || settings.Theme is null)
        {
            throw new JsonException("Site settings sections must be objects.");
        }

        if (settings.Site.BaseUrls is null || settings.Site.BaseUrls.Any(static uri => uri is null || !uri.IsAbsoluteUri || (uri.Scheme != "https" && uri.Scheme != "http")))
        {
            throw new JsonException("Site base URLs must be absolute HTTP or HTTPS URLs.");
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    AllowDuplicateProperties = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(MarkazorSiteSettings))]
internal sealed partial class SiteSettingsJsonContext : JsonSerializerContext;
