using Bunit;
using Markazor.Client;
using Markazor.Content;
using Markazor.Core.Auth;
using Markazor.Core.GitHub;
using Markazor.Core.Setup;
using Markazor.Editing;
using Markazor.Pwa;
using Markazor.Reading;
using Microsoft.Extensions.DependencyInjection;

namespace Markazor.Components.Tests;

public sealed class MarkazorComponentTests
{
    [Fact]
    public void ArticleListShowsAnAccessibleEmptyState()
    {
        using BunitContext context = new();
        IRenderedComponent<ArticleList> component = context.Render<ArticleList>(parameters => parameters
            .Add(static item => item.Page, new MarkazorArticlePage([], 1, 10, 0, 1))
            .Add(static item => item.EmptyMessage, "No articles have been published."));

        Assert.Contains("No articles have been published.", component.Find("[role='status']").TextContent, StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".markazor-reader-card"));
        Assert.Empty(component.FindAll(".markazor-pagination"));
    }

    [Fact]
    public void ArticleListContinuesNumberingAcrossPages()
    {
        using BunitContext context = new();
        IRenderedComponent<ArticleList> component = context.Render<ArticleList>(parameters => parameters
            .Add(static item => item.Page, new MarkazorArticlePage([ReaderArticle("second", "Second", null, [])], 2, 1, 2, 2))
            .Add(static item => item.PageLinkFactory, static number => MarkazorReaderRoutes.Page("/posts", number)));

        Assert.Equal("02", component.Find(".markazor-reader-entry-number").TextContent);
        Assert.Equal("true", component.Find(".markazor-reader-entry-number").GetAttribute("aria-hidden"));
        Assert.Equal("/posts", component.Find(".markazor-pagination a[rel='prev']").GetAttribute("href"));
        Assert.Empty(component.FindAll(".markazor-pagination a[rel='next']"));
    }

    [Fact]
    public void ReaderHomeUsesConfiguredIdentityAndPublishedContent()
    {
        using BunitContext context = new();
        using HttpClient httpClient = new();
        Markazor.Configuration.MarkazorOptions options = new()
        {
            Articles =
            [
                ReaderArticle("first", "First post", null, []),
                ReaderArticle("second", "Second post", null, []),
                ReaderArticle("third", "Third post", null, []),
                ReaderArticle("private", "Private draft", null, []) with { IsDraft = true },
            ],
        };
        options.Site.Name = "A personal journal";
        options.Site.Description = "Writing from my own corner.";
        options.Site.PageSize = 1;
        context.Services.AddSingleton<IMarkazorReaderService>(new MarkazorReaderService(httpClient, new FakeMarkdownRenderer(), options));

        IRenderedComponent<ReaderHome> component = context.Render<ReaderHome>(parameters => parameters.Add(static item => item.Page, 2));

        Assert.Contains(options.Site.Name, component.Find("h1").TextContent, StringComparison.Ordinal);
        Assert.Equal(options.Site.Description, component.Find(".reader-intro-description").TextContent);
        Assert.Equal("03", component.Find(".reader-colophon-index a[href='/posts'] small").TextContent);
        Assert.Single(component.FindAll(".markazor-reader-card"));
        Assert.Equal("/posts/second", component.Find(".markazor-reader-card h2 a").GetAttribute("href"));
        Assert.DoesNotContain("Private draft", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderMenuSupportsEscapeAndClosesAfterNavigation()
    {
        using BunitContext context = new();
        using HttpClient httpClient = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorReaderService>(new MarkazorReaderService(httpClient, new FakeMarkdownRenderer(), new Markazor.Configuration.MarkazorOptions()));
        context.Services.AddSingleton<IMarkazorPwaUpdateService>(new FakePwaUpdateService());
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/posts");

        IRenderedComponent<ReaderShell> component = context.Render<ReaderShell>(parameters => parameters.AddChildContent("<h1>Posts</h1>"));

        Assert.Equal("page", component.Find("#reader-navigation a[href='/posts']").GetAttribute("aria-current"));
        Assert.Equal("false", component.Find(".site-menu-toggle").GetAttribute("aria-expanded"));
        component.Find(".site-menu-toggle").Click();
        Assert.Equal("true", component.Find(".site-menu-toggle").GetAttribute("aria-expanded"));
        component.Find(".site-header").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Equal("false", component.Find(".site-menu-toggle").GetAttribute("aria-expanded"));
        component.Find(".site-menu-toggle").Click();
        component.Find("#reader-navigation a[href='/tags']").Click();
        Assert.Equal("false", component.Find(".site-menu-toggle").GetAttribute("aria-expanded"));
        Assert.Equal("#main-content", component.Find(".markazor-skip-link").GetAttribute("href"));
        Assert.NotNull(component.Find("main#main-content"));
    }

    [Fact]
    public void StudioPageFollowsLocationChangesBetweenSections()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession());
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());
        context.Services.AddSingleton<IMarkazorContentCatalog>(new FakeCatalog());
        context.Services.AddSingleton<IMarkazorMarkdownRenderer>(new FakeMarkdownRenderer());
        context.Services.AddSingleton<IMarkazorEditorService>(new FakeEditor());
        context.Services.AddSingleton<IMarkazorPwaUpdateService>(new FakePwaUpdateService());
        Microsoft.AspNetCore.Components.NavigationManager navigation =
            context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/studio/settings");

        IRenderedComponent<StudioLayout> component = context.Render<StudioLayout>(parameters => parameters
            .Add(static item => item.Body, builder =>
            {
                builder.OpenComponent<StudioPage>(0);
                builder.CloseComponent();
            }));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Settings", component.Find("h1").TextContent);
            Assert.Equal("page", component.Find("a[href='/studio/settings']").GetAttribute("aria-current"));
            Assert.Null(component.Find("a[href='/studio/setup']").GetAttribute("aria-current"));
        });

        navigation.NavigateTo("/studio/write");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Write", component.Find("h1").TextContent);
            Assert.Equal("page", component.Find("a[href='/studio/write']").GetAttribute("aria-current"));
            Assert.Null(component.Find("a[href='/studio/settings']").GetAttribute("aria-current"));
        });

        navigation.NavigateTo("/studio/settings");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Settings", component.Find("h1").TextContent);
            Assert.Equal("page", component.Find("a[href='/studio/settings']").GetAttribute("aria-current"));
            Assert.Null(component.Find("a[href='/studio/write']").GetAttribute("aria-current"));
        });
    }

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
            .Add(static item => item.PageLinkFactory, static number => MarkazorReaderRoutes.Page("/", number)));

        Assert.Contains("href=\"/posts/hello\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/categories/general\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/tags/intro\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\" href=\"/\"", component.Markup, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\" href=\"/page/3\"", component.Markup, StringComparison.Ordinal);
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
            .Add(static item => item.Content, new MarkdownRenderResult("<h2>Rendered body</h2>", []))
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
            .Add(static item => item.Content, new MarkdownRenderResult("<p>Rendered body</p>", []))
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
    public void MarkdownEditorRendersOneArticleCanvasAndRecoverySurface()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string editedMarkdown = string.Empty;

        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>(parameters => parameters
            .Add(static editor => editor.Value, "# Hello")
            .Add(static editor => editor.ValueChanged, value => editedMarkdown = value));

        Assert.Contains("Article tools", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Heading 1", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("More", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Bold", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Insert block", component.Markup, StringComparison.Ordinal);
        Assert.Equal(5, component.FindAll(".markazor-markdown-toolbar button").Count);
        Assert.Contains("markazor-muya-host", component.Markup, StringComparison.Ordinal);
        Assert.Equal("# Hello", component.Find(".markazor-editor-textarea").GetAttribute("value"));

        component.Find(".markazor-editor-textarea").Input("Updated");

        Assert.Equal("Updated", editedMarkdown);
    }

    [Fact]
    public void MarkdownToolbarUsesNativeInsertAndClearsSearchWhenClosed()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./_content/Markazor/markazor-markdown-editor.js");
        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>();

        component.Find("button[aria-label='Insert block']").Click();
        Assert.Single(module.Invocations["showInsertMenu"]);

        component.Find("button[aria-label='Find in article']").Click();
        Assert.True(component.Find("button[aria-label='Previous match']").HasAttribute("disabled"));
        Assert.True(component.Find("button[aria-label='Next match']").HasAttribute("disabled"));
        component.Find("input[aria-label='Find text']").Input("Word");
        Assert.False(component.Find("button[aria-label='Next match']").HasAttribute("disabled"));
        component.Find("input[aria-label='Find text']").KeyDown("Escape");
        Assert.Empty(component.FindAll(".markazor-editor-search"));
        Assert.Equal(string.Empty, module.Invocations["search"][^1].Arguments[1]);
    }

    [Fact]
    public async Task MarkdownEditorFlushesTheLatestBrowserValueBeforeSaving()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule("./_content/Markazor/markazor-markdown-editor.js");
        module.Setup<string>("getValue", _ => true).SetResult("Latest browser text");
        string current = "Original text";
        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>(parameters => parameters
            .Add(static editor => editor.Value, current)
            .Add(static editor => editor.ValueChanged, value => current = value));

        await component.InvokeAsync(component.Instance.FlushAsync);

        Assert.Equal("Latest browser text", current);
    }

    [Fact]
    public async Task MarkdownEditorSendsPastedImagesThroughTheExistingUploadCallback()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        byte[]? uploaded = null;
        string? uploadedName = null;
        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>(parameters => parameters
            .Add(static editor => editor.ImageUploadRequested, async args =>
            {
                uploadedName = args.File.Name;
                uploaded = new byte[args.File.Size];
                using Stream stream = args.File.OpenReadStream();
                await stream.ReadExactlyAsync(uploaded).ConfigureAwait(false);
            }));

        await component.InvokeAsync(() => component.Instance.NotifyImageFile("clipboard.png", "image/png", [1, 2, 3]));

        Assert.Equal("clipboard.png", uploadedName);
        Assert.Equal([1, 2, 3], uploaded);
    }

    [Fact]
    public async Task MarkdownEditorIgnoresCallbacksFromThePreviousDocument()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string current = "Current article";
        bool uploaded = false;
        IRenderedComponent<MarkazorMarkdownEditor> component = context.Render<MarkazorMarkdownEditor>(parameters => parameters
            .Add(static editor => editor.DocumentKey, "posts/current.md")
            .Add(static editor => editor.Value, current)
            .Add(static editor => editor.ValueChanged, value => current = value)
            .Add(static editor => editor.ImageUploadRequested, _ => uploaded = true));

        await component.InvokeAsync(() => component.Instance.NotifyEditorChanged("Previous article", "posts/previous.md"));
        await component.InvokeAsync(() => component.Instance.NotifyImageFile("old.png", "image/png", [1], "posts/previous.md"));

        Assert.Equal("Current article", current);
        Assert.False(uploaded);

        await component.InvokeAsync(() => component.Instance.NotifyEditorChanged("Current edit", "posts/current.md"));
        Assert.Equal("Current edit", current);
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
        Assert.Contains("2 images to upload on save", component.Markup, StringComparison.Ordinal);
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
            AngleSharp.Dom.IElement openWrite = component.FindAll("button")
                .Single(button => button.TextContent.Contains("Open Write", StringComparison.Ordinal));
            Assert.True(openWrite.HasAttribute("disabled"));
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
            Assert.Contains("http://localhost/studio/setup/github-callback", component.Markup, StringComparison.Ordinal);
            Assert.Contains("callback_urls%5B%5D=http%3A%2F%2Flocalhost%2Fstudio%2Fsetup%2Fgithub-callback", component.Markup, StringComparison.Ordinal);
            Assert.Contains("setup_url=http%3A%2F%2Flocalhost%2Fstudio%2Fsetup", component.Markup, StringComparison.Ordinal);
            Assert.Contains("webhook_active=false", component.Markup, StringComparison.Ordinal);
            Assert.Contains("request_oauth_on_install=false", component.Markup, StringComparison.Ordinal);
            Assert.Contains("contents=write", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Only select repositories", component.Find("#setup-authorize").TextContent, StringComparison.Ordinal);
            Assert.Contains("owner/repo", component.Find("#setup-authorize").TextContent, StringComparison.Ordinal);
            Assert.Contains("Client ID", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GitHub App Client ID", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Client secret", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GITHUB_APP_CLIENT_SECRET", component.Markup, StringComparison.Ordinal);
            Assert.Equal(4, component.FindAll(".markazor-setup-step").Count);
            Assert.Contains("Environment variables", component.Find("#setup-secret").TextContent, StringComparison.Ordinal);
            Assert.Contains("MARKAZOR_AUTH_COOKIE_SECRET", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Open Settings", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain(component.FindAll("button"), button => button.TextContent.Trim() == "Save Settings");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupAndSettingsRestoreSignInBeforeRepositoryIsConfigured(bool openSettings)
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = CreateUnconfiguredSession(restoreSession: true);
        FakeDiagnosticsService diagnostics = new(CreateDiagnostics(ready: true, canPush: true));
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(diagnostics);
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        if (openSettings)
        {
            IRenderedComponent<StudioSettingsPanel> component = context.Render<StudioSettingsPanel>();
            component.WaitForAssertion(() => Assert.Contains("Public Settings", component.Markup, StringComparison.Ordinal));
        }
        else
        {
            IRenderedComponent<SetupPage> component = context.Render<SetupPage>();
            component.WaitForAssertion(() =>
            {
                Assert.False(component.FindAll("button").Single(button => button.TextContent.Trim() == "Open Settings").HasAttribute("disabled"));
                Assert.True(component.FindAll("button").Single(button => button.TextContent.Trim() == "Open Write").HasAttribute("disabled"));
            });
        }

        Assert.True(session.IsAuthenticated);
        Assert.True(session.GetAccessTokenCount > 0);
        Assert.Equal(0, diagnostics.RunCount);
    }

    [Fact]
    public void SetupRemembersClientIdForAuthorizationAndSettings()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        BunitJSModuleInterop module = context.JSInterop.SetupModule("./_content/Markazor/markazor-setup.js");
        JSRuntimeInvocationHandler<string> rememberedClientId = module.Setup<string>("getLocalValue", "clientId");
        rememberedClientId.SetResult("remembered-client");
        FakeSession session = CreateUnconfiguredSession(restoreSession: true);
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<SetupPage> setup = context.Render<SetupPage>();
        setup.WaitForAssertion(() => Assert.Equal("remembered-client", GetInput(setup, "GitHub App Client ID").GetAttribute("value")));
        SetInput(setup, "GitHub App Client ID", " edited-client ");
        setup.FindAll("button").Single(button => button.TextContent.Trim() == "Authorize again").Click();
        setup.WaitForAssertion(() =>
        {
            Assert.Equal("edited-client", session.LastAuthorizationRequest?.ClientId);
            Assert.Contains(module.Invocations["setLocalValue"], invocation =>
                invocation.Arguments.SequenceEqual(new object?[] { "clientId", "edited-client" }));
        });

        rememberedClientId.SetResult("edited-client");
        IRenderedComponent<StudioSettingsPanel> settings = context.Render<StudioSettingsPanel>();
        settings.WaitForAssertion(() =>
        {
            Assert.Equal("edited-client", GetInput(settings, "GitHub App Client ID").GetAttribute("value"));
            Assert.Equal("main", GetInput(settings, "Default Branch").GetAttribute("value"));
            Assert.Contains("http://localhost/", settings.Find("textarea").GetAttribute("value"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SetupRechecksSecretWithoutDiscardingClientId()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = CreateUnconfiguredSession();
        session.SetupStatus = session.SetupStatus! with
        {
            MissingSettings = [.. session.SetupStatus!.MissingSettings, "GITHUB_APP_CLIENT_SECRET"],
        };
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        IRenderedComponent<SetupPage> component = context.Render<SetupPage>();

        SetInput(component, "GitHub App Client ID", "bootstrap-client");
        Assert.True(component.FindAll("button").Single(button => button.TextContent.Trim() == "Authorize GitHub").HasAttribute("disabled"));
        session.SetupStatus = session.SetupStatus! with
        {
            MissingSettings = ["github.clientId", "repository.owner", "repository.name"],
        };
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Check again").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("bootstrap-client", GetInput(component, "GitHub App Client ID").GetAttribute("value"));
            Assert.Contains("Secret found", component.Find("#setup-secret").TextContent, StringComparison.Ordinal);
            Assert.False(component.FindAll("button").Single(button => button.TextContent.Trim() == "Authorize GitHub").HasAttribute("disabled"));
            Assert.Contains("Step 3 of 4", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SetupUsesDeployedClientIdInsteadOfAnOldBrowserValue()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        BunitJSModuleInterop module = context.JSInterop.SetupModule("./_content/Markazor/markazor-setup.js");
        module.Setup<string>("getLocalValue", "clientId").SetResult("old-client");
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(isAuthenticated: false));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));

        IRenderedComponent<SetupPage> component = context.Render<SetupPage>();
        component.WaitForAssertion(() => Assert.Equal("client-id", GetInput(component, "GitHub App Client ID").GetAttribute("value")));
    }

    [Fact]
    public void StudioSetupIsAvailableBeforeAuthorizationAndEnvironmentConfiguration()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(
            isAuthenticated: false,
            missingSettings: ["GITHUB_APP_CLIENT_SECRET"]));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: false, canPush: false)));
        context.Services.AddSingleton<IMarkazorPwaUpdateService>(new FakePwaUpdateService());
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/studio/setup");

        IRenderedComponent<StudioLayout> component = context.Render<StudioLayout>(parameters => parameters
            .Add(static item => item.Body, builder =>
            {
                builder.OpenComponent<SetupPage>(0);
                builder.CloseComponent();
            }));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Setup", component.Find("h1").TextContent);
            Assert.Equal("page", component.Find("a[href='/studio/setup']").GetAttribute("aria-current"));
            Assert.Null(component.Find("a[href='/studio/settings']").GetAttribute("aria-current"));
            Assert.Contains("Create App", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GitHub App Client ID", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GITHUB_APP_CLIENT_SECRET", component.Markup, StringComparison.Ordinal);
            Assert.Single(component.FindAll("main#studio-content"));
            Assert.Equal("#studio-content", component.Find(".markazor-skip-link").GetAttribute("href"));
        });
    }

    [Fact]
    public void GitHubCallbackReturnsToStudioSetupAfterAuthorization()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession());
        Microsoft.AspNetCore.Components.NavigationManager navigation =
            context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/studio/setup/github-callback?code=code-1&state=state-1");

        IRenderedComponent<GitHubCallbackPage> component = context.Render<GitHubCallbackPage>();

        component.WaitForAssertion(() =>
            Assert.Equal("http://localhost/studio/setup?auth=github", navigation.Uri));
    }

    [Fact]
    public void GitHubCallbackOffersReturnToSetupWhenSignInIsCancelled()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(isAuthenticated: false));
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/studio/setup/github-callback?error=access_denied");

        IRenderedComponent<GitHubCallbackPage> component = context.Render<GitHubCallbackPage>();

        Assert.Equal("/studio/setup", component.Find("a").GetAttribute("href"));
        Assert.Contains("try again", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsRoutesSignedOutSetupGapsBackToSetup()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(
            isAuthenticated: false,
            missingSettings: ["GITHUB_APP_CLIENT_SECRET"]));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<StudioSettingsPanel> component = context.Render<StudioSettingsPanel>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Setup Required", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Open Setup", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorize GitHub", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Public Settings", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Repository Diagnostics", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Deployment Workflow", component.Markup, StringComparison.Ordinal);
        });

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Open Setup", StringComparison.Ordinal))
            .Click();

        Assert.Equal("http://localhost/studio/setup",
            context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public void SettingsShowsAuthorizationWhenSetupIsCompleteButSignedOut()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(new FakeSession(isAuthenticated: false));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<StudioSettingsPanel> component = context.Render<StudioSettingsPanel>();

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
    public void SettingsShowsApiTimeoutInsteadOfStayingLoading()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IMarkazorClientSession>(
            new FakeSession(loadSetupException: new TaskCanceledException("Timed out")));
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(new FakeSettingsSyncService());

        IRenderedComponent<StudioSettingsPanel> component = context.Render<StudioSettingsPanel>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("The Markazor API did not respond before the request timed out.", component.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Loading settings status", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SettingsSavesEditedPublicSettings()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        FakeSession session = new();
        FakeSettingsSyncService settingsSync = new();
        context.Services.AddSingleton<IMarkazorClientSession>(session);
        context.Services.AddSingleton<IMarkazorSetupDiagnosticsService>(
            new FakeDiagnosticsService(CreateDiagnostics(ready: true, canPush: true)));
        context.Services.AddSingleton<IMarkazorSettingsSyncService>(settingsSync);

        IRenderedComponent<StudioSettingsPanel> component = context.Render<StudioSettingsPanel>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Public Settings", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Not saved", component.Markup, StringComparison.Ordinal);
            Assert.Equal(string.Empty, GetInput(component, "Site Title").GetAttribute("value") ?? string.Empty);
            Assert.Equal(string.Empty, GetInput(component, "Repository Owner").GetAttribute("value") ?? string.Empty);
        });

        SetTextarea(component, "Site Base URLs", "https://site.example.test/\nhttps://www.example.test/");
        SetInput(component, "Site Title", "Edited Site");
        SetTextarea(component, "Site Description", "Edited site description.");
        Assert.Contains("Site Icon", component.Markup, StringComparison.Ordinal);
        Assert.Equal("image/png", component.Find(".markazor-site-icon-field input").GetAttribute("accept"));
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
            Assert.Equal("Saved", component.Find(".markazor-section-header .markazor-status-pill").TextContent);
            Assert.Empty(component.FindAll(".markazor-warning"));
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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("GitHub authorization is required.", component.Markup, StringComparison.Ordinal);
            Assert.Contains("GitHub required", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Connect GitHub in Setup to load your content.", component.Markup, StringComparison.Ordinal);
            Assert.Empty(component.FindAll(".markazor-document-state"));
            Assert.Empty(component.FindAll(".markazor-authoring-grid"));
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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();

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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();

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
    public void EditorCreatesDraftWithSuggestedMetadata()
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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();

        component.WaitForAssertion(() =>
            Assert.Contains("No documents yet. Create a draft to get started.", component.Markup, StringComparison.Ordinal));

        component.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "New Draft", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("drafts/new-draft-", component.Markup, StringComparison.Ordinal);
            Assert.Equal("Start writing.", component.Find(".markazor-editor-textarea").GetAttribute("value"));
            Assert.Equal("New Draft", GetMetadataInput(component, "Title").GetAttribute("value"));
            Assert.StartsWith("new-draft-", GetMetadataInput(component, "Slug").GetAttribute("value"), StringComparison.Ordinal);
            Assert.Empty(component.FindAll("input[type='radio']"));
            Assert.Contains("Draft ready.", component.Markup, StringComparison.Ordinal);
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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();
        component.WaitForAssertion(() =>
            Assert.Contains("No documents yet. Create a draft to get started.", component.Markup, StringComparison.Ordinal));

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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();

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

        IRenderedComponent<StudioWriterPanel> component = context.Render<StudioWriterPanel>();

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
        GetInput(component, labelText).Input(value);
    }

    private static AngleSharp.Dom.IElement GetInput<TComponent>(
        IRenderedComponent<TComponent> component,
        string labelText)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        AngleSharp.Dom.IElement? input = component.FindAll("label")
            .Single(label => label.TextContent.Contains(labelText, StringComparison.Ordinal))
            .QuerySelector("input");

        return Assert.IsAssignableFrom<AngleSharp.Dom.IElement>(input);
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
        IRenderedComponent<StudioWriterPanel> component,
        string labelText)
    {
        AngleSharp.Dom.IElement? input = component.Find(".markazor-metadata-toolbar")
            .QuerySelectorAll("label")
            .Single(label => label.TextContent.Contains(labelText, StringComparison.Ordinal))
            .QuerySelector("input");

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
        bool existsOnGitHub = true)
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
                "/posts/" + slug,
                isDraft),
            sha,
            existsOnGitHub);
    }

    private sealed class FakeDiagnosticsService(
        MarkazorRepositoryDiagnostics diagnostics) : IMarkazorSetupDiagnosticsService
    {
        public int RunCount { get; private set; }

        public Task<MarkazorRepositoryDiagnostics> RunAsync(
            CancellationToken cancellationToken = default)
        {
            RunCount++;
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
        IReadOnlyList<string>? missingSettings = null,
        bool restoreSession = false) : IMarkazorClientSession
    {
        private bool authenticated = isAuthenticated;

        public int GetAccessTokenCount { get; private set; }

        public MarkazorGitHubAuthorizationRequest? LastAuthorizationRequest { get; private set; }

        public MarkazorSetupStatus? SetupStatus { get; set; } = new(
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

        public string? AccessToken => authenticated ? "token" : null;

        public DateTimeOffset? AccessTokenExpiresAtUtc => authenticated
            ? DateTimeOffset.UtcNow.AddHours(1)
            : null;

        public bool IsReady => true;

        public bool IsAuthenticated => authenticated;

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
            LastAuthorizationRequest = request;
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
            GetAccessTokenCount++;
            authenticated |= restoreSession;
            return Task.FromResult(AccessToken);
        }

        public Task<MarkazorGitHubClientResult<T>> ExecuteGitHubAsync<T>(
            Func<MarkazorGitHubRepositoryClient, string, CancellationToken, Task<MarkazorGitHubClientResult<T>>> operation,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private static FakeSession CreateUnconfiguredSession(bool restoreSession = false)
    {
        FakeSession session = new(isAuthenticated: false, restoreSession: restoreSession);
        session.SetupStatus = session.SetupStatus! with
        {
            Ready = false,
            MissingSettings = ["github.clientId", "repository.owner", "repository.name"],
            GitHub = new MarkazorGitHubSettings { ClientId = string.Empty },
            Repository = new MarkazorRepositoryStatus(string.Empty, string.Empty, "main"),
        };
        return session;
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
        public MarkdownRenderResult Render(string markdown, string? articleTitle = null)
        {
            return new MarkdownRenderResult("<p>Preview</p>", []);
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
