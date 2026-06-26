using System.Diagnostics.CodeAnalysis;
using Markazor.Core.Setup;

namespace Markazor.Core.Security;

public sealed class ContentPathPolicy
{
    public static bool IsRepositoryWriteAllowed(string relativePath)
    {
        if (!TryNormalize(relativePath, out string? normalizedPath))
        {
            return false;
        }

        if (IsUnderRoot(normalizedPath, ".github/workflows/"))
        {
            return false;
        }

        if (IsForbiddenPublicPath(normalizedPath))
        {
            return false;
        }

        return IsUnderRoot(normalizedPath, MarkazorRepositoryPaths.DraftsRoot)
            || IsUnderRoot(normalizedPath, MarkazorRepositoryPaths.PostsRoot)
            || IsUnderRoot(normalizedPath, MarkazorRepositoryPaths.NotesRoot)
            || IsUnderRoot(normalizedPath, MarkazorRepositoryPaths.AssetsRoot)
            || IsUnderRoot(normalizedPath, MarkazorRepositoryPaths.PublicRoot);
    }

    public static string EnsureRepositoryWriteAllowed(string relativePath)
    {
        if (!TryNormalize(relativePath, out string? normalizedPath) || !IsRepositoryWriteAllowed(normalizedPath))
        {
            throw new InvalidOperationException($"Path '{relativePath}' is not allowed for Markazor content writes.");
        }

        return normalizedPath;
    }

    public static string NormalizeGitHubPath(string relativePath)
    {
        return TryNormalize(relativePath, out string? normalizedPath) ? normalizedPath : throw new ArgumentException("Path must be a safe relative repository path.", nameof(relativePath));
    }

    private static bool IsUnderRoot(string normalizedPath, string root)
    {
        if (!TryNormalize(root, out string? normalizedRoot))
        {
            return false;
        }

        if (!normalizedRoot.EndsWith('/'))
        {
            normalizedRoot += "/";
        }

        return normalizedPath.Length > normalizedRoot.Length && normalizedPath.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static bool IsForbiddenPublicPath(string normalizedPath)
    {
        if (!normalizedPath.StartsWith(MarkazorRepositoryPaths.PublicRoot, StringComparison.Ordinal))
        {
            return false;
        }

        string publicRelativePath = normalizedPath[MarkazorRepositoryPaths.PublicRoot.Length..];

        return IsAtOrUnderRoot(publicRelativePath, "_framework/")
            || IsAtOrUnderRoot(publicRelativePath, "_content/")
            || IsAtOrUnderRoot(publicRelativePath, "_markazor/")
            || IsAtOrUnderRoot(publicRelativePath, "assets/")
            || string.Equals(publicRelativePath, "service-worker-assets.js", StringComparison.Ordinal);
    }

    private static bool IsAtOrUnderRoot(string normalizedPath, string root)
    {
        if (!TryNormalize(root, out string? normalizedRoot))
        {
            return false;
        }

        normalizedRoot = normalizedRoot.TrimEnd('/');

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal) || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.Ordinal);
    }

    private static bool TryNormalize(string? path, [NotNullWhen(true)] out string? normalizedPath)
    {
        normalizedPath = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith('/') || path.StartsWith('\\'))
        {
            return false;
        }

        string[] segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        foreach (string segment in segments)
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        normalizedPath = string.Join('/', segments);

        return true;
    }
}
