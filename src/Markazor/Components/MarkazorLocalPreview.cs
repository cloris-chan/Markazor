using Microsoft.AspNetCore.Components;
using Markazor.Client;
using Markazor.Content;
using Markazor.Core.Setup;
using Markazor.Editing;

namespace Markazor.Components;

internal static class MarkazorLocalPreview
{
    public const string QueryParameterName = "markazor-preview";

    public static bool IsEnabled(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        Uri uri = navigation.ToAbsoluteUri(navigation.Uri);
        return IsLoopbackHost(uri) && HasPreviewQuery(uri.Query);
    }

    public static MarkazorSetupStatus CreateSetupStatus(string baseUri)
    {
        Uri siteBaseUri = new(baseUri, UriKind.Absolute);
        return new MarkazorSetupStatus(
            Ready: true,
            MissingSettings: [],
            Site: new MarkazorSitePublicSettings
            {
                Name = "Markazor Preview",
                Description = "Local preview workspace for Markazor authoring surfaces.",
                BaseUrls = [siteBaseUri],
            },
            GitHub: new MarkazorGitHubSettings { ClientId = "preview-client-id", },
            Repository: new MarkazorRepositoryStatus("cloris-chan", "Markazor.Preview", "main"),
            Theme: new MarkazorThemeSettings { Name = "default", },
            ExpectedStaticWebAppsBuildSettings: new MarkazorStaticWebAppsBuildSettings(
                "src/MarkazorSite.Web",
                "src/MarkazorSite.Functions",
                "wwwroot"));
    }

    public static MarkazorRepositoryDiagnostics CreateDiagnostics()
    {
        return new MarkazorRepositoryDiagnostics(
            Ready: true,
            RepositoryAccessible: true,
            CanPull: true,
            CanPush: true,
            BranchAccessible: true,
            TreeReadable: true,
            GitHubDefaultBranch: "main",
            Errors: [],
            Warnings: []);
    }

    public static IReadOnlyList<MarkazorContentEntry> CreateEditorEntries()
    {
        return
        [
            new MarkazorContentEntry(
                MarkdownContent.ParseArticleMeta(
                    "drafts/workbench-redesign.md",
                    CreateDraftMarkdown()),
                "preview-draft",
                ExistsOnGitHub: true),
            new MarkazorContentEntry(
                MarkdownContent.ParseArticleMeta(
                    "posts/hello-workbench.md",
                    CreatePublishedMarkdown()),
                "preview-post",
                ExistsOnGitHub: true),
        ];
    }

    public static bool TryCreateEditorDocument(string path, out MarkazorEditorDocument document)
    {
        document = path switch
        {
            "drafts/workbench-redesign.md" => new MarkazorEditorDocument(path, CreateDraftMarkdown(), "preview-draft"),
            "posts/hello-workbench.md" => new MarkazorEditorDocument(path, CreatePublishedMarkdown(), "preview-post"),
            _ => new MarkazorEditorDocument(string.Empty, string.Empty, null),
        };

        return !string.IsNullOrWhiteSpace(document.Path);
    }

    private static bool IsLoopbackHost(Uri uri)
    {
        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPreviewQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        ReadOnlySpan<char> remaining = query.AsSpan();
        if (remaining[0] == '?')
        {
            remaining = remaining[1..];
        }

        while (remaining.Length > 0)
        {
            int separator = remaining.IndexOf('&');
            ReadOnlySpan<char> pair = separator < 0 ? remaining : remaining[..separator];
            int equals = pair.IndexOf('=');
            ReadOnlySpan<char> name = equals < 0 ? pair : pair[..equals];
            ReadOnlySpan<char> value = equals < 0 ? ReadOnlySpan<char>.Empty : pair[(equals + 1)..];
            if (name.SequenceEqual(QueryParameterName.AsSpan()) && value.SequenceEqual("1".AsSpan()))
            {
                return true;
            }

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..];
        }

        return false;
    }

    private static string CreateDraftMarkdown()
    {
        return string.Join(
            '\n',
            "---",
            "slug: workbench-redesign",
            "title: \"Workbench Redesign\"",
            "kind: post",
            "summary: \"A local preview document for tuning the Markazor editor surface.\"",
            "publishedAt: 2026-06-24",
            "draft: true",
            "tags: [design, preview]",
            "category: Design",
            "---",
            string.Empty,
            "# Workbench Redesign",
            string.Empty,
            "This preview lets the editor and management views render without a live GitHub session.",
            string.Empty,
            "Use it to check spacing, metadata controls, markdown editing, preview typography, and action density before shipping the theme.");
    }

    private static string CreatePublishedMarkdown()
    {
        return string.Join(
            '\n',
            "---",
            "slug: hello-workbench",
            "title: \"Hello Workbench\"",
            "kind: note",
            "summary: \"Published content shown in the local preview browser.\"",
            "publishedAt: 2026-06-23",
            "draft: false",
            "tags: [markazor, editor]",
            "category: Notes",
            "---",
            string.Empty,
            "# Hello Workbench",
            string.Empty,
            "The default theme should feel coherent across reading, setup, management, and editing.");
    }
}
