using System.Text.Json;
using Markazor.Core.Serialization;

namespace Markazor.Core.Setup;

public static class MarkazorSiteSettingsLoader
{
    public const string DefaultFileName = MarkazorRepositoryPaths.SettingsPath;

    public const string PublishedFileName = "markazor.settings.json";

    public static MarkazorSiteSettings Load(string? filePath = null)
    {
        string resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
        {
            return new MarkazorSiteSettings();
        }

        try
        {
            using FileStream stream = File.OpenRead(resolvedPath);
            MarkazorSiteSettings? settings = JsonSerializer.Deserialize(stream, MarkazorCoreJsonSerializerContext.Default.MarkazorSiteSettings);

            return Normalize(settings);
        }
        catch (IOException)
        {
            return new MarkazorSiteSettings();
        }
        catch (JsonException)
        {
            return new MarkazorSiteSettings();
        }
    }

    private static string ResolvePath(string? filePath)
    {
        string settingsPath = string.IsNullOrWhiteSpace(filePath) ? DefaultFileName : filePath;

        if (Path.IsPathRooted(settingsPath))
        {
            return settingsPath;
        }

        foreach (string root in GetCandidateRoots())
        {
            string candidate = Path.Combine(root, settingsPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, settingsPath);
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        string? scriptRoot = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot");
        if (!string.IsNullOrWhiteSpace(scriptRoot))
        {
            yield return scriptRoot;
        }
    }

    private static MarkazorSiteSettings Normalize(MarkazorSiteSettings? settings)
    {
        if (settings is null)
        {
            return new MarkazorSiteSettings();
        }

        return new MarkazorSiteSettings
        {
            Site = Normalize(settings.Site),
            GitHub = settings.GitHub ?? new MarkazorGitHubSettings(),
            Repository = settings.Repository ?? new MarkazorRepositorySettings(),
            Theme = settings.Theme ?? new MarkazorThemeSettings(),
        };
    }

    private static MarkazorSitePublicSettings Normalize(MarkazorSitePublicSettings? settings)
    {
        if (settings is null)
        {
            return new MarkazorSitePublicSettings();
        }

        return new MarkazorSitePublicSettings
        {
            Name = MarkazorSitePublicSettings.NormalizeName(settings.Name),
            Description = MarkazorSitePublicSettings.NormalizeDescription(settings.Description),
            BaseUrls = settings.BaseUrls ?? [],
        };
    }
}
