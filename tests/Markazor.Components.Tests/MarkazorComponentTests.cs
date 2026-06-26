using Bunit;
using Markazor.Core.Auth;
using Markazor.Core.GitHub;
using Markazor.Core.Setup;
using Markazor.Client;
using Markazor.Components;
using Markazor.Content;
using Markazor.Editing;
using Markazor.Reading;
using Markazor.Pwa;
using Microsoft.Extensions.DependencyInjection;

namespace Markazor.Components.Tests;

public sealed class MarkazorComponentTests
{
    [Fact]
    public void ArticleListRendersMetadataTaxonomyAndPagination()
    {
        using BunitContext context = new();
        MarkazorArticlePage page = new(
            [ReaderArticle("hello", "Hello", "General", ["intro"])],
            PageNumber: 2,
            PageSize: 1,
            TotalItems: 3,
            TotalPages: 3);

        IRenderedComponent<ArticleList> component = context.Render<ArticleList>(parameters => parameters
            .Add(static item => item.Page, page)
            .Add(static item => item.PageLinkFactory, static number => $"/?page={number}"));

        Assert.Contains("href=\"/posts/hello\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/categories?category=General\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/tags?tag=intro\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\" href=\"/?page=1\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\" href=\"/?page=3\"", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ArticleViewRendersSafeHtmlAndAdjacentNavigation()
    {
        using BunitContext context = new();
        ArticleMeta article = ReaderArticle("current", "Current", "General", ["dotnet"]);
        MarkazorArticleNavigation navigation = new(
            ReaderArticle("older", "Older", null, []),
            ReaderArticle("newer", "Newer", null, []));

        IRenderedComponent<ArticleView> component = context.Render<ArticleView>(parameters => parameters
            .Add(static item => item.Article, article)
            .Add(static item => item.Html, "<h2>Rendered body</h2>")
            .Add(static item => item.Navigation, navigation));

        Assert.Contains("<h2>Rendered body</h2>", component.Markup, StringComparison.Ordinal);
        Assert.Contains("class=\"markazor-article-navigation-previous\" rel=\"prev\" href=\"/posts/older\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("class=\"markazor-article-navigation-next\" rel=\"next\" href=\"/posts/newer\"", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ArticleViewMarksNextNavigationWhenPreviousArticleIsMissing()
    {
        using BunitContext context = new();
        ArticleMeta article = ReaderArticle("current", "Current", "General", ["dotnet"]);
        MarkazorArticleNavigation navigation = new(null, ReaderArticle("newer", "Newer", null, []));

        IRenderedComponent<ArticleView> component = context.Render<ArticleView>(parameters => parameters
            .Add(static item => item.Article, article)
            .Add(static item => item.Html, "<p>Rendered body</p>")
            .Add(static item => item.Navigation, navigation));

        Assert.DoesNotContain("markazor-article-navigation-previous", component.Markup, StringComparison.Ordinal);
        Assert.Contains("class=\"markazor-article-navigation-next\" rel=\"next\" href=\"/posts/newer\"", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PwaPromptWaitsForConsentBeforeActivatingUpdate()
    {
        using BunitContext context = new();
        FakePwaUpdateService updates = new();
        context.Services.AddSingleton<IMarkazorPwaUpdateService>(updates);

        IRenderedComponent<PwaUpdatePrompt> component = context.Render<PwaUpdatePrompt>();

        component.WaitForAssertion(() =>
            Assert.Contains("A new version is ready", component.Markup, StringComparison.Ordinal));
        Assert.False(updates.Activated);

        component.Find("button").Click();

        component.WaitForAssertion(() => Assert.True(updates.Activated));
    }

    [Fact]
    public void PwaPromptRestoresButtonWhenActivationReturnsWithoutNavigation()
    {
        using BunitContext context = new();
        FakePwaUpdateService updates = new();
        context.Services.AddSingleton<IMarkazorPwaUpdateService>(updates);

        IRenderedComponent<PwaUpdatePrompt> component = context.Render<PwaUpdatePrompt>();

        component.WaitForAssertion(() =>
            Assert.Contains("A new version is ready", component.Markup, StringComparison.Ordinal));

        component.Find("button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.True(updates.Activated);
            Assert.Equal("Reload", component.Find("button").TextContent.Trim());
        });
    }

    [Fact]
    public void PwaPromptShowsLoadingStateWhileActivationIsPending()
    {
        using BunitContext context = new();
        FakePwaUpdateService updates = new();
        context.Services.AddSingleton<IMarkazorPwaUpdateService>(updates);

        IRenderedComponent<PwaUpdatePrompt> component = context.Render<PwaUpdatePrompt>();

        component.WaitForAssertion(() =>
            Assert.Contains("A new version is ready", component.Markup, StringComparison.Ordinal));

        component.Find("button").Click();

        component.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement button = component.Find("button");
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal("true", button.GetAttribute("aria-busy"));
            Assert.Contains("is-loading", button.GetAttribute("class"), StringComparison.Ordinal);
            Assert.Contains("Reloading...", button.TextContent, StringComparison.Ordinal);
            Assert.NotNull(component.Find(".markazor-update-spinner"));
            Assert.False(updates.Activated);
        });

        component.WaitForAssertion(() => Assert.True(updates.Activated));

        updates.CompleteActivation();

        component.WaitForAssertion(() =>
            Assert.Equal("Reload", component.Find("button").TextContent.Trim()));
    }

    [Fact]
    public void MarkdownEditorRendersToolbarAndFallbackSurface()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string editedMarkdown = string.Empty;

        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>(parameters => parameters
            .Add(static editor => editor.Value, "# Hello")
            .Add(static editor => editor.ValueChanged, value => editedMarkdown = value));

        Assert.Contains("Markdown tools", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Heading 1", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Code block", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Table", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Image", component.Markup, StringComparison.Ordinal);
        Assert.Contains("markazor-codemirror-host", component.Markup, StringComparison.Ordinal);
        Assert.Equal("# Hello", component.Find(".markazor-editor-textarea").GetAttribute("value"));

        component.Find(".markazor-editor-textarea").Input("Updated");

        Assert.Equal("Updated", editedMarkdown);
    }

    [Fact]
    public void MarkdownEditorRendersImageAssetControlsInsideToolbar()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool externalImageRequested = false;

        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>(parameters => parameters
            .Add(static editor => editor.Value, "# Hello")
            .Add(static editor => editor.ExternalImageRequested, () => externalImageRequested = true)
            .Add(static editor => editor.ImageUploadRequested, _ => { })
            .Add(static editor => editor.AcceptedImageTypes, "image/png,image/jpeg")
            .Add(static editor => editor.PendingAssetCount, 2));

        Assert.Contains("Insert image", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("From URL", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload local image", component.Markup, StringComparison.Ordinal);
        Assert.Contains("2 pending asset(s)", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".markazor-asset-tools"));

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Image", StringComparison.Ordinal))
            .Click();

        Assert.Contains("From URL", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Upload local image", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".markazor-markdown-menu-backdrop"));
        Assert.Equal("image/png,image/jpeg", component.Find(".markazor-markdown-upload-button input").GetAttribute("accept"));
        Assert.Equal("Upload local image", component.Find(".markazor-markdown-upload-button span").TextContent.Trim());

        component.Find(".markazor-markdown-menu-backdrop").Click();

        Assert.DoesNotContain("From URL", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload local image", component.Markup, StringComparison.Ordinal);

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Image", StringComparison.Ordinal))
            .Click();
        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "From URL", StringComparison.Ordinal))
            .Click();

        Assert.True(externalImageRequested);
    }

    [Fact]
    public void SetupDisablesEditorWhenRepositoryDiagnosticsFail()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: false, canPush: false)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<SetupPage> component = context.Render<SetupPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("cannot push", component.Markup, StringComparison.Ordinal);
            AngleSharp.Dom.IElement openEditor = component.FindAll("button")
                .Single(button => button.TextContent.Contains("Open Editor", StringComparison.Ordinal));
            Assert.True(openEditor.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void SetupShowsGitHubAppRegistrationGuide()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession());
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: false, canPush: false)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<SetupPage> component = context.Render<SetupPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("GitHub App", component.Markup, StringComparison.Ordinal);
            Assert.Contains("https://github.com/settings/apps/new?name=Markazor%20repo", component.Markup, StringComparison.Ordinal);
            Assert.Contains("https://github.com/organizations/owner/settings/apps/new?name=Markazor%20repo", component.Markup, StringComparison.Ordinal);
            Assert.Contains("http://localhost/setup/github-callback", component.Markup, StringComparison.Ordinal);
            Assert.Contains("callback_urls%5B%5D=http%3A%2F%2Flocalhost%2Fsetup%2Fgithub-callback", component.Markup, StringComparison.Ordinal);
            Assert.Contains("setup_url=http%3A%2F%2Flocalhost%2Fsetup", component.Markup, StringComparison.Ordinal);
            Assert.Contains("webhook_active=false", component.Markup, StringComparison.Ordinal);
            Assert.Contains("contents=write", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Only select repositories: owner/repo", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Client ID", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GitHub App Client ID", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Client secret", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GITHUB_APP_CLIENT_SECRET", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Required SWA secret", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Recommended SWA secret", component.Markup, StringComparison.Ordinal);
            Assert.Contains("protects OAuth state and refresh cookies independently", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Open Manage", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Save Settings", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ManageRoutesSignedOutSetupGapsBackToSetup()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(
            isAuthenticated: false,
            missingSettings: ["GITHUB_APP_CLIENT_SECRET"]));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<ManagePage> component = context.Render<ManagePage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Setup Required", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Open Setup", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorize GitHub", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Public Settings", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Repository Diagnostics", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Deployment Workflow", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ManageShowsAuthorizationWhenSetupIsCompleteButSignedOut()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(isAuthenticated: false));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<ManagePage> component = context.Render<ManagePage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("GitHub Authorization", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Authorize GitHub", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Public Settings", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Repository Diagnostics", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Deployment Workflow", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ManageShowsApiTimeoutInsteadOfStayingLoading()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(
            new FakeSession(loadSetupException: new TaskCanceledException("Timed out")));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<ManagePage> component = context.Render<ManagePage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("The Markazor API did not respond before the request timed out.", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Loading management status", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ManageSavesEditedPublicSettingsDraft()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeSettingsSyncService settingsSync = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(settingsSync);

        IRenderedComponent<ManagePage> component = context.Render<ManagePage>();

        component.WaitForAssertion(() =>
            Assert.Contains("Public Settings", component.Markup, StringComparison.Ordinal));

        SetTextarea(component, "Site Base URLs", "https://site.example.test/\nhttps://www.example.test/");
        SetInput(component, "Site Title", "Edited Site");
        SetTextarea(component, "Site Description", "Edited site description.");
        Assert.Contains("Site Icon", component.Markup, StringComparison.Ordinal);
        Assert.Contains("assets/site-icon.png", component.Markup, StringComparison.Ordinal);
        SetInput(component, "GitHub App Client ID", "edited-client");
        SetInput(component, "Repository Owner", "edited-owner");
        SetInput(component, "Repository Name", "edited-repo");
        SetInput(component, "Default Branch", "trunk");
        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Save Settings", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            MarkazorSiteSettings saved = Assert.IsType<MarkazorSiteSettings>(settingsSync.LastSettings);
            Assert.Equal("Edited Site", saved.Site.Name);
            Assert.Equal("Edited site description.", saved.Site.Description);
            Assert.Equal([new Uri("https://site.example.test/"), new Uri("https://www.example.test/")], saved.Site.BaseUrls);
            Assert.Equal("edited-client", saved.GitHub.ClientId);
            Assert.Equal("edited-owner", saved.Repository.Owner);
            Assert.Equal("edited-repo", saved.Repository.Name);
            Assert.Equal("trunk", saved.Repository.DefaultBranch);
        });
    }

    [Fact]
    public void EditorRequiresGitHubBeforeShowingBuildIndexContent()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new(isAuthenticated: false);
        FakeCatalog catalog = new(
            [ContentEntry("posts/build-only.md", "Build only", isDraft: false, sha: null, existsOnGitHub: false)]);
        FakeEditor editor = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("GitHub authorization is required.", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GitHub required", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Authorize GitHub and pass repository diagnostics to load the live content tree.", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("posts/build-only.md", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Build index", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EditorShowsConflictRecoveryAndAllowsDirtyPublish()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new();
        FakeEditor editor = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
            Assert.Contains("drafts/hello.md", component.Markup, StringComparison.Ordinal));
        component.Find(".markazor-editor-textarea").Input("Local edit");
        AngleSharp.Dom.IElement publish = component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Publish", StringComparison.Ordinal));
        Assert.False(publish.HasAttribute("disabled"));

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Save", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("GitHub changed this file", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Use GitHub version", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Save editor over latest", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EditorSplitsMetadataFromBodyAndComposesDocumentOnSave()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new();
        FakeEditor editor = new() { SaveSucceeds = true };
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("drafts/hello.md", component.Markup, StringComparison.Ordinal);
            Assert.Equal("Draft body", component.Find(".markazor-editor-textarea").GetAttribute("value"));
            Assert.Equal("Hello", GetMetadataInput(component, "Title").GetAttribute("value"));
            Assert.DoesNotContain("---", component.Find(".markazor-editor-textarea").GetAttribute("value"), StringComparison.Ordinal);
            Assert.Contains("Insert image", component.Markup, StringComparison.Ordinal);
            Assert.Empty(component.FindAll(".markazor-asset-tools"));
        });

        GetMetadataInput(component, "Title").Input("Edited title");
        component.Find(".markazor-editor-textarea").Input("Edited body");
        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Save", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("drafts/hello.md", editor.LastSavedPath);
            Assert.Contains("title: \"Edited title\"", editor.LastSavedMarkdown, StringComparison.Ordinal);
            Assert.Contains("draft: true", editor.LastSavedMarkdown, StringComparison.Ordinal);
            Assert.Contains("Edited body", editor.LastSavedMarkdown, StringComparison.Ordinal);
            Assert.StartsWith("---", editor.LastSavedMarkdown, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EditorCreatesPostDraftWithMatchingKindMetadata()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new([]);
        FakeEditor editor = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
            Assert.Contains("No GitHub content files are available under the configured roots.", component.Markup, StringComparison.Ordinal));

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "New Draft", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("drafts/new-draft-", component.Markup, StringComparison.Ordinal);
            Assert.Equal("Start writing.", component.Find(".markazor-editor-textarea").GetAttribute("value"));
            Assert.Equal("New Draft", GetMetadataInput(component, "Title").GetAttribute("value"));
            Assert.StartsWith("new-draft-", GetMetadataInput(component, "Slug").GetAttribute("value"), StringComparison.Ordinal);
            Assert.True(GetMetadataKindInput(component, MarkazorArticleKind.Post).HasAttribute("checked"));
        });
    }

    [Fact]
    public void EditorCreatesNoteDraftWithMatchingKindMetadata()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new([]);
        FakeEditor editor = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
            Assert.Contains("No GitHub content files are available under the configured roots.", component.Markup, StringComparison.Ordinal));

        component.Find("input[value=\"note\"]").Change("note");
        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "New Draft", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("drafts/new-draft-", component.Markup, StringComparison.Ordinal);
            Assert.Equal("Start writing.", component.Find(".markazor-editor-textarea").GetAttribute("value"));
            Assert.Equal("New Draft", GetMetadataInput(component, "Title").GetAttribute("value"));
            Assert.StartsWith("new-draft-", GetMetadataInput(component, "Slug").GetAttribute("value"), StringComparison.Ordinal);
            Assert.True(GetMetadataKindInput(component, MarkazorArticleKind.Note).HasAttribute("checked"));
            Assert.Contains("Note draft ready.", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EditorSyncsGeneratedDraftSlugWithTitleUntilPathIsEdited()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new([]);
        FakeEditor editor = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();
        component.WaitForAssertion(() =>
            Assert.Contains("No GitHub content files are available under the configured roots.", component.Markup, StringComparison.Ordinal));

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "New Draft", StringComparison.Ordinal))
            .Click();
        component.WaitForAssertion(() =>
            Assert.StartsWith("new-draft-", GetMetadataInput(component, "Slug").GetAttribute("value"), StringComparison.Ordinal));
        string generatedSlug = GetMetadataInput(component, "Slug").GetAttribute("value") ?? string.Empty;
        string timestamp = generatedSlug["new-draft-".Length..];

        GetMetadataInput(component, "Title").Input("Better Draft");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("better-draft-" + timestamp, GetMetadataInput(component, "Slug").GetAttribute("value"));
            Assert.Contains("drafts/better-draft-" + timestamp + ".md", component.Markup, StringComparison.Ordinal);
        });

        GetMetadataInput(component, "Path").Input("drafts/custom.md");
        GetMetadataInput(component, "Title").Input("Another Title");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("another-title-" + timestamp, GetMetadataInput(component, "Slug").GetAttribute("value"));
            Assert.Contains("drafts/custom.md", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EditorRemovesStaleEntryWhenGitHubLoadReturnsNotFound()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new();
        FakeEditor editor = new()
        {
            LoadResult = new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                false,
                null,
                "The file was not found on GitHub.",
                MarkazorGitHubClientResultKind.NotFound),
        };
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("This file no longer exists on GitHub.", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("drafts/hello.md", component.Markup, StringComparison.Ordinal);
            Assert.Empty(catalog.Entries);
            Assert.True(catalog.RefreshCount >= 2);
        });
    }

    [Fact]
    public void EditorRefreshesLiveCatalogAfterSinglePublish()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new();
        catalog.EnqueueRefresh(
            [ContentEntry("drafts/hello.md", "Hello", isDraft: true, sha: "draft-sha")]);
        catalog.EnqueueRefresh(
            [ContentEntry("posts/hello.md", "Hello", isDraft: false, sha: "post-sha")]);
        FakeEditor editor = new()
        {
            PublishResult = new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                true,
                new MarkazorEditorDocument(
                    "posts/hello.md",
                    """
                    ---
                    title: Hello
                    draft: false
                    ---

                    Draft body
                    """,
                    "post-sha"),
                null),
        };
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
            Assert.Contains("drafts/hello.md", component.Markup, StringComparison.Ordinal));

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Publish", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Static Web Apps deployment is pending.", component.Markup, StringComparison.Ordinal);
            Assert.Contains("posts/hello.md", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("drafts/hello.md", component.Markup, StringComparison.Ordinal);
            Assert.Equal("drafts/hello.md", editor.LastPublishedDraftPath);
            Assert.Equal("draft-sha", editor.LastPublishedDraftSha);
            Assert.Contains("Draft body", editor.LastPublishedMarkdown, StringComparison.Ordinal);
            Assert.Equal(2, catalog.RefreshCount);
            Assert.Equal(["posts/hello.md"], catalog.Entries.Select(static entry => entry.RelativePath));
        });
    }

    [Fact]
    public void EditorShowsArticleKindInLiveContentList()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeCatalog catalog = new(
            [ContentEntry("notes/idea.md", "Idea", isDraft: false, sha: "note-sha", kind: MarkazorArticleKind.Note)]);
        FakeEditor editor = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(catalog);
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(editor);

        IRenderedComponent<EditorPage> component = context.Render<EditorPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("notes/idea.md", component.Markup, StringComparison.Ordinal);
            Assert.Contains("markazor-article-kind", component.Markup, StringComparison.Ordinal);
            Assert.Contains(">Note<", component.Markup, StringComparison.Ordinal);
        });
    }

    private static MarkazorRepositoryDiagnostics CreateDiagnostics(bool ready, bool canPush)
    {
        return new MarkazorRepositoryDiagnostics(
            Ready: ready,
            RepositoryAccessible: true,
            CanPull: true,
            CanPush: canPush,
            BranchAccessible: true,
            TreeReadable: true,
            GitHubDefaultBranch: "main",
            Errors: canPush ? [] : ["The GitHub authorization cannot push repository content."],
            Warnings: []);
    }

    private static void SetInput<TComponent>(
        IRenderedComponent<TComponent> component,
        string labelText,
        string value)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        component.FindAll("label")
            .Single(label => label.TextContent.Contains(labelText, StringComparison.Ordinal))
            .QuerySelector("input")
            ?.Input(value);
    }

    private static void SetTextarea<TComponent>(
        IRenderedComponent<TComponent> component,
        string labelText,
        string value)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        component.FindAll("label")
            .Single(label => label.TextContent.Contains(labelText, StringComparison.Ordinal))
            .QuerySelector("textarea")
            ?.Input(value);
    }

    private static AngleSharp.Dom.IElement GetMetadataInput(
        IRenderedComponent<EditorPage> component,
        string labelText)
    {
        AngleSharp.Dom.IElement? input = component.Find(".markazor-metadata-toolbar")
            .QuerySelectorAll("label")
            .Single(label => label.TextContent.Contains(labelText, StringComparison.Ordinal))
            .QuerySelector("input");

        return Assert.IsAssignableFrom<AngleSharp.Dom.IElement>(input);
    }

    private static AngleSharp.Dom.IElement GetMetadataKindInput(
        IRenderedComponent<EditorPage> component,
        string kind)
    {
        AngleSharp.Dom.IElement? input = component.Find(".markazor-metadata-kind")
            .QuerySelectorAll("input")
            .Single(item => string.Equals(item.GetAttribute("value"), kind, StringComparison.Ordinal));

        return Assert.IsAssignableFrom<AngleSharp.Dom.IElement>(input);
    }

    private static ArticleMeta ReaderArticle(
        string slug,
        string title,
        string? category,
        IReadOnlyList<string> tags)
    {
        return new ArticleMeta(
            slug,
            title,
            "Summary",
            new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero),
            tags,
            category,
            $"posts/{slug}.md",
            "/posts/" + slug,
            IsDraft: false);
    }

    private static MarkazorContentEntry ContentEntry(
        string path,
        string title,
        bool isDraft,
        string? sha,
        bool existsOnGitHub = true,
        string kind = MarkazorArticleKind.Post)
    {
        string slug = Path.GetFileNameWithoutExtension(path);

        return new MarkazorContentEntry(
            new ArticleMeta(
                slug,
                title,
                string.Empty,
                DateTimeOffset.UnixEpoch,
                [],
                null,
                path,
                (string.Equals(kind, MarkazorArticleKind.Note, StringComparison.Ordinal) ? "/notes/" : "/posts/") + slug,
                isDraft,
                kind),
            sha,
            existsOnGitHub);
    }

    private sealed class FakeDiagnosticsService(
        MarkazorRepositoryDiagnostics diagnostics) : IMarkazorSetupDiagnosticsService
    {
        public Task<MarkazorRepositoryDiagnostics> RunAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(diagnostics);
        }
    }

    private sealed class FakeSettingsSyncService : IMarkazorSettingsSyncService
    {
        public MarkazorSiteSettings? LastSettings { get; private set; }

        public MarkazorSettingsAsset? LastSiteIcon { get; private set; }

        public Task<MarkazorSettingsSyncResult> SaveAsync(
            MarkazorSiteSettings settings,
            MarkazorSettingsAsset? siteIcon = null,
            CancellationToken cancellationToken = default)
        {
            LastSettings = settings;
            LastSiteIcon = siteIcon;

            return Task.FromResult(new MarkazorSettingsSyncResult(true, "commit-sha", null));
        }
    }

    private sealed class FakeSession(
        bool isAuthenticated = true,
        Exception? loadSetupException = null,
        IReadOnlyList<string>? missingSettings = null) : IMarkazorClientSession
    {
        public MarkazorSetupStatus? SetupStatus { get; } = new(
            Ready: true,
            MissingSettings: missingSettings ?? [],
            Site: new MarkazorSitePublicSettings
            {
                Name = "Component Test Site",
                Description = "Component test description.",
                BaseUrls = [new Uri("http://localhost/")],
            },
            GitHub: new MarkazorGitHubSettings { ClientId = "client-id" },
            Repository: new MarkazorRepositoryStatus("owner", "repo", "main"),
            Theme: new MarkazorThemeSettings { Name = "default" },
            ExpectedStaticWebAppsBuildSettings: new MarkazorStaticWebAppsBuildSettings(
                "src/App",
                "src/Api",
                "wwwroot"));

        public string? AccessToken => isAuthenticated ? "token" : null;

        public DateTimeOffset? AccessTokenExpiresAtUtc => isAuthenticated
            ? DateTimeOffset.UtcNow.AddHours(1)
            : null;

        public bool IsReady => true;

        public bool IsAuthenticated => isAuthenticated;

        public Task<MarkazorSetupStatus?> LoadSetupStatusAsync(CancellationToken cancellationToken = default)
        {
            if (loadSetupException is not null)
            {
                return Task.FromException<MarkazorSetupStatus?>(loadSetupException);
            }

            return Task.FromResult(SetupStatus);
        }

        public Task<MarkazorGitHubAuthorizationResponse> StartGitHubAuthorizationAsync(
            MarkazorGitHubAuthorizationRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MarkazorGitHubAuthorizationResponse(
                new Uri("https://github.test/authorize"),
                DateTimeOffset.UtcNow.AddMinutes(10)));
        }

        public Task<bool> CompleteGitHubAuthorizationAsync(
            MarkazorGitHubCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(isAuthenticated ? "token" : null);
        }

        public Task<MarkazorGitHubClientResult<T>> ExecuteGitHubAsync<T>(
            Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeCatalog : IMarkazorContentCatalog
    {
        private readonly Queue<IReadOnlyList<MarkazorContentEntry>> refreshEntries = [];
        private List<MarkazorContentEntry> entries;

        public FakeCatalog()
            : this([ContentEntry("drafts/hello.md", "Hello", isDraft: true, sha: "draft-sha")])
        {
        }

        public FakeCatalog(IReadOnlyList<MarkazorContentEntry> entries)
        {
            this.entries = [.. entries];
        }

        public IReadOnlyList<MarkazorContentEntry> Entries => entries;

        public bool IsBuildIndexFallback => false;

        public string? Warning => null;

        public int RefreshCount { get; private set; }

        public void EnqueueRefresh(IReadOnlyList<MarkazorContentEntry> nextEntries)
        {
            refreshEntries.Enqueue(nextEntries);
        }

        public Task<IReadOnlyList<MarkazorContentEntry>> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            if (refreshEntries.TryDequeue(out IReadOnlyList<MarkazorContentEntry>? nextEntries))
            {
                entries = [.. nextEntries];
            }

            return Task.FromResult(Entries);
        }

        public void UpsertDocument(string path, string markdown, string? sha)
        {
            entries.RemoveAll(entry => string.Equals(entry.RelativePath, path, StringComparison.Ordinal));
            entries.Add(new MarkazorContentEntry(
                MarkdownContent.ParseArticleMeta(path, markdown),
                sha,
                ExistsOnGitHub: !string.IsNullOrWhiteSpace(sha)));
        }

        public void Remove(string path)
        {
            entries.RemoveAll(entry => string.Equals(entry.RelativePath, path, StringComparison.Ordinal));
        }
    }

    private sealed class FakeEditor : IMarkazorEditorService
    {
        public MarkazorEditorOperationResult<MarkazorEditorDocument>? LoadResult { get; init; }

        public MarkazorEditorOperationResult<MarkazorEditorDocument>? PublishResult { get; init; }

        public bool SaveSucceeds { get; init; }

        public string? LastSavedPath { get; private set; }

        public string? LastSavedMarkdown { get; private set; }

        public string? LastSavedSha { get; private set; }

        public IReadOnlyList<MarkazorEditorAsset>? LastSavedAssets { get; private set; }

        public string? LastPublishedDraftPath { get; private set; }

        public string? LastPublishedMarkdown { get; private set; }

        public string? LastPublishedDraftSha { get; private set; }

        public IReadOnlyList<MarkazorEditorAsset>? LastPublishedAssets { get; private set; }

        public Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> LoadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (LoadResult is not null)
            {
                return Task.FromResult(LoadResult);
            }

            return Task.FromResult(new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                true,
                new MarkazorEditorDocument(
                    path,
                    """
                    ---
                    title: Hello
                    draft: true
                    ---

                    Draft body
                    """,
                    "draft-sha"),
                null));
        }

        public Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> SaveAsync(
            string path,
            string markdown,
            string? sha,
            IReadOnlyList<MarkazorEditorAsset>? assets = null,
            CancellationToken cancellationToken = default)
        {
            LastSavedPath = path;
            LastSavedMarkdown = markdown;
            LastSavedSha = sha;
            LastSavedAssets = assets;

            if (SaveSucceeds)
            {
                return Task.FromResult(new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                    true,
                    new MarkazorEditorDocument(path, markdown, "saved-sha"),
                    null));
            }

            return Task.FromResult(new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                false,
                null,
                "The file changed on GitHub.",
                MarkazorGitHubClientResultKind.Conflict,
                new MarkazorEditorConflict(path, markdown, "Remote edit", "remote-sha")));
        }

        public Task<MarkazorEditorOperationResult> DeleteAsync(
            string path,
            string? sha,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MarkazorEditorOperationResult<MarkazorEditorDocument>> PublishDraftAsync(
            string draftPath,
            string markdown,
            string? draftSha,
            IReadOnlyList<MarkazorEditorAsset>? assets = null,
            CancellationToken cancellationToken = default)
        {
            LastPublishedDraftPath = draftPath;
            LastPublishedMarkdown = markdown;
            LastPublishedDraftSha = draftSha;
            LastPublishedAssets = assets;

            return Task.FromResult(PublishResult ?? new MarkazorEditorOperationResult<MarkazorEditorDocument>(
                false,
                null,
                "Publish is not configured."));
        }

        public Task<MarkazorBatchPublishResult> PublishDraftsAsync(
            IReadOnlyList<string> draftPaths,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeMarkdownRenderer : IMarkazorMarkdownRenderer
    {
        public string ToSafeHtml(string markdown)
        {
            return "<p>Preview</p>";
        }
    }

    private sealed class FakePwaUpdateService : IMarkazorPwaUpdateService
    {
        private TaskCompletionSource? activationCompletion;

        public bool Activated { get; private set; }

        public Task<bool> WaitForUpdateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task ActivateUpdateAsync(CancellationToken cancellationToken = default)
        {
            Activated = true;
            return activationCompletion?.Task ?? Task.CompletedTask;
        }

        public void HoldActivation()
        {
            activationCompletion = new TaskCompletionSource();
        }

        public void CompleteActivation()
        {
            activationCompletion?.SetResult();
        }
    }
}
