namespace Markazor.Core.Setup;

public static class MarkazorSiteSettingsLoader
{
    public const string DefaultFileName = MarkazorRepositoryPaths.SettingsPath;

    public const string PublishedFileName = "markazor.settings.json";

    public static bool Exists(string? filePath = null)
    {
        return File.Exists(ResolvePath(filePath));
    }

    public static MarkazorSiteSettings Load(string? filePath = null)
    {
        string resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
        {
            return new MarkazorSiteSettings();
        }

        return MarkazorSiteSettingsJson.Parse(File.ReadAllText(resolvedPath));
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

}
