using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Markazor.Components;
using Markazor.Configuration;
using Markazor.Content;
using Markazor.Core.Setup;
using Markazor.Pwa;
using Markazor.Reading;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Markazor.Build;

internal static class StaticSiteGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> GenerateAsync(string repositoryRoot, string webRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(webRoot);
        string source = Path.GetFullPath(repositoryRoot);
        string output = Path.GetFullPath(webRoot);
        string shellPath = Path.Combine(output, "index.html");
        string shell = PrepareApplicationShell(await File.ReadAllTextAsync(shellPath, cancellationToken).ConfigureAwait(false));
        string settingsPath = Path.Combine(source, "public", "markazor.settings.json");
        MarkazorSiteSettings settings = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<MarkazorSiteSettings>(await File.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false), JsonOptions) ?? throw new InvalidDataException("Site settings are empty.")
            : new MarkazorSiteSettings();
        MarkazorSitePublicSettings site = settings.Site ?? throw new InvalidDataException("The site settings object is required.");
        MarkazorOptions options = new();
        options.Site.Name = MarkazorSitePublicSettings.NormalizeName(site.Name);
        options.Site.Description = MarkazorSitePublicSettings.NormalizeDescription(site.Description);
        options.Site.BaseUrl = ValidateBaseUrl(site.PrimaryBaseUrl);

        List<ArticleMeta> articles = [];
        Dictionary<string, string> markdownFiles = new(StringComparer.Ordinal);
        HashSet<string> articleRoutes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string kind in new[] { "posts", "notes" })
        {
            string directory = Path.Combine(source, kind);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            EnumerationOptions enumeration = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive };
            foreach (string file in Directory.EnumerateFiles(directory, "*.md", enumeration).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                string markdown = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                ArticleMeta article = MarkdownContent.ParseArticleMeta(relative, markdown);
                if (article.IsDraft)
                {
                    throw new InvalidDataException($"Published content '{relative}' declares draft: true. Move it to drafts/.");
                }

                ValidateSegment(article.Slug, "slug", relative);
                if (!articleRoutes.Add(article.Route))
                {
                    throw new InvalidDataException($"Duplicate published route '{article.Route}' in '{relative}'.");
                }

                if (article.Category is { Length: > 0 } category)
                {
                    ValidateSegment(category, "category", relative);
                }

                foreach (string tag in article.Tags)
                {
                    ValidateSegment(tag, "tag", relative);
                }

                articles.Add(article);
                markdownFiles.Add(article.ContentPath, markdown);
            }
        }

        options.Articles = articles.AsReadOnly();
        using LocalMarkdownHandler handler = new(markdownFiles);
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://markazor-build.invalid/") };
        MarkazorReaderService reader = new(client, new MarkazorMarkdownRenderer(), options);
        List<StaticPage> pages = CreatePages(reader, articles);
        Directory.CreateDirectory(Path.Combine(output, "_markazor"));
        await File.WriteAllTextAsync(Path.Combine(output, "_markazor", "app.html"), shell, cancellationToken).ConfigureAwait(false);
        foreach (string route in new[] { "studio", "studio/write", "studio/settings", "setup", "setup/github-callback" })
        {
            string path = Path.Combine(output, route.Replace('/', Path.DirectorySeparatorChar), "index.html");
            EnsureGeneratedPathAvailable(path);
            await File.WriteAllTextAsync(path, shell, cancellationToken).ConfigureAwait(false);
        }
        Dictionary<string, string> routes = new(StringComparer.Ordinal);
        foreach (StaticPage page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string markup = await RenderPageAsync(reader, page).ConfigureAwait(false);
            string html = CreateDocument(shell, markup, page, options.Site);
            string routePath = page.Route == "/" ? "/index.html" : page.Route + "/index.html";
            string path = Path.Combine(output, Uri.UnescapeDataString(routePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (page.Route != "/")
            {
                EnsureGeneratedPathAvailable(path);
            }

            await File.WriteAllTextAsync(path, html, cancellationToken).ConfigureAwait(false);
            routes.Add(page.Route, routePath);
        }

        await File.WriteAllTextAsync(Path.Combine(output, "_markazor", "routes.json"), JsonSerializer.Serialize(routes, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, "_markazor", "routes.js"), "self.markazorRoutes = " + JsonSerializer.Serialize(routes, JsonOptions) + ";\n", cancellationToken).ConfigureAwait(false);
        UpdateDeploymentConfiguration(output);
        WriteSitemap(output, pages, options.Site.BaseUrl);
        return pages.Count;
    }

    private static Uri? ValidateBaseUrl(Uri? uri)
    {
        if (uri is not null && (!uri.IsAbsoluteUri || (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/"))
        {
            throw new InvalidDataException("The primary site base URL must be an HTTP(S) origin, without credentials, a path, query, or fragment.");
        }

        return uri;
    }

    private static void ValidateSegment(string value, string name, string file)
    {
        string stem = value.Split('.')[0];
        bool reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ')
            || reserved || value.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            || value.Any(static character => char.IsControl(character) || character is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*'))
        {
            throw new InvalidDataException($"The {name} in '{file}' must be a portable path segment (1–120 characters), without slashes, reserved file names, trailing dots/spaces or file-system punctuation.");
        }
    }

    private static void EnsureGeneratedPathAvailable(string path)
    {
        if (File.Exists(path))
        {
            throw new InvalidDataException($"A public asset conflicts with generated reader page '{path}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    private static List<StaticPage> CreatePages(MarkazorReaderService reader, List<ArticleMeta> articles)
    {
        List<StaticPage> pages = [];
        AddListing(MarkazorReaderPageKind.Home, null, "/", reader.Site.Name, reader.Site.Description, reader.GetArticles());
        AddListing(MarkazorReaderPageKind.Posts, null, "/posts", "Posts", "Long-form articles and release notes.", reader.GetPosts());
        AddListing(MarkazorReaderPageKind.Notes, null, "/notes", "Notes", "Short updates, field notes, and working thoughts.", reader.GetNotes());
        pages.Add(new(MarkazorReaderPageKind.Categories, null, 1, "/categories", "Categories", reader.Site.Description));
        pages.Add(new(MarkazorReaderPageKind.Tags, null, 1, "/tags", "Tags", reader.Site.Description));
        pages.Add(new(MarkazorReaderPageKind.Archive, null, 1, "/archive", "Archive", "Published articles grouped by month."));
        foreach (MarkazorTaxonomyItem category in reader.GetCategories())
        {
            AddListing(MarkazorReaderPageKind.Categories, category.Name, MarkazorReaderRoutes.Category(category.Name), category.Name, reader.Site.Description, reader.GetCategory(category.Name));
        }

        foreach (MarkazorTaxonomyItem tag in reader.GetTags())
        {
            AddListing(MarkazorReaderPageKind.Tags, tag.Name, MarkazorReaderRoutes.Tag(tag.Name), "#" + tag.Name, reader.Site.Description, reader.GetTag(tag.Name));
        }

        foreach (ArticleMeta article in articles)
        {
            MarkazorReaderPageKind kind = article.Kind == MarkazorArticleKind.Note ? MarkazorReaderPageKind.Note : MarkazorReaderPageKind.Post;
            pages.Add(new(kind, article.Slug, 1, article.Route, article.Title, article.Summary, article));
        }

        return pages;

        void AddListing(MarkazorReaderPageKind kind, string? value, string root, string title, string description, MarkazorArticlePage first)
        {
            for (int number = 1; number <= first.TotalPages; number++)
            {
                pages.Add(new(kind, value, number, MarkazorReaderRoutes.Page(root, number), title, description));
            }
        }
    }

    private static async Task<string> RenderPageAsync(MarkazorReaderService reader, StaticPage page)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IMarkazorReaderService>(reader);
        services.AddSingleton<NavigationManager>(new BuildNavigationManager(page.Route));
        services.AddSingleton<IJSRuntime>(new BuildJavaScriptRuntime());
        services.AddSingleton<IMarkazorPwaUpdateService, MarkazorPwaUpdateService>();
        ServiceProvider provider = services.BuildServiceProvider();
        await using var providerLifetime = provider.ConfigureAwait(false);
        HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await using var rendererLifetime = renderer.ConfigureAwait(false);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment body = builder =>
            {
                builder.OpenComponent<ReaderPage>(0);
                builder.AddAttribute(1, nameof(ReaderPage.Kind), page.Kind);
                builder.AddAttribute(2, nameof(ReaderPage.Value), page.Value);
                builder.AddAttribute(3, nameof(ReaderPage.PageNumber), page.Number);
                builder.CloseComponent();
            };
            ParameterView parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ReaderShell.ChildContent)] = body });
            var result = await renderer.RenderComponentAsync<ReaderShell>(parameters).ConfigureAwait(true);
            return result.ToHtmlString();
        }).ConfigureAwait(false);
    }

    private static string CreateDocument(string shell, string markup, StaticPage page, MarkazorSiteOptions site)
    {
        using IHtmlDocument document = new HtmlParser().ParseDocument(shell);
        IElement app = document.QuerySelector("#app") ?? throw new InvalidDataException("The site shell must contain an element with id=app.");
        app.InnerHtml = markup;
        document.DocumentElement.SetAttribute("data-markazor-static", string.Empty);
        foreach (IElement anchor in document.QuerySelectorAll("a[href^='#']"))
        {
            anchor.SetAttribute("href", page.Route + anchor.GetAttribute("href"));
        }

        document.QuerySelector("#blazor-error-ui")?.Remove();
        foreach (IElement script in document.QuerySelectorAll("script[src]"))
        {
            if (script.GetAttribute("src")!.Contains("_framework/blazor", StringComparison.Ordinal))
            {
                script.Remove();
            }
        }

        foreach (IElement old in document.QuerySelectorAll("title, meta[name='description'], link[rel='canonical'], meta[property^='og:'], meta[name^='twitter:'], script[type='application/ld+json']"))
        {
            old.Remove();
        }

        string title = page.Kind == MarkazorReaderPageKind.Home && page.Number == 1 ? site.Name : page.Title + (page.Number > 1 ? " — " + page.Number.ToString(CultureInfo.InvariantCulture) : string.Empty) + " | " + site.Name;
        IElement titleElement = document.CreateElement("title");
        titleElement.TextContent = title;
        document.Head!.AppendChild(titleElement);
        string description = string.IsNullOrWhiteSpace(page.Description) ? site.Description : page.Description;
        AddMeta(document, "name", "description", description);
        AddMeta(document, "property", "og:title", title);
        AddMeta(document, "property", "og:description", description);
        AddMeta(document, "property", "og:site_name", site.Name);
        AddMeta(document, "property", "og:type", page.Article is null ? "website" : "article");
        AddMeta(document, "name", "twitter:card", "summary");
        AddMeta(document, "name", "twitter:title", title);
        AddMeta(document, "name", "twitter:description", description);
        if (site.BaseUrl is not null)
        {
            string canonical = new Uri(site.BaseUrl, page.Route).AbsoluteUri;
            IElement link = document.CreateElement("link");
            link.SetAttribute("rel", "canonical");
            link.SetAttribute("href", canonical);
            document.Head.AppendChild(link);
            AddMeta(document, "property", "og:url", canonical);
            string icon = new Uri(site.BaseUrl, "/assets/site-icon.png").AbsoluteUri;
            AddMeta(document, "property", "og:image", icon);
            AddMeta(document, "property", "og:image:alt", site.Name);
            AddMeta(document, "name", "twitter:image", icon);
            if (page.Article is { } article)
            {
                string date = article.PublishedAtUtc.ToString("O", CultureInfo.InvariantCulture);
                AddMeta(document, "property", "article:published_time", date);
                JsonObject data = new()
                {
                    ["@context"] = "https://schema.org",
                    ["@type"] = "BlogPosting",
                    ["headline"] = article.Title,
                    ["description"] = description,
                    ["url"] = canonical,
                    ["mainEntityOfPage"] = canonical,
                    ["datePublished"] = date,
                    ["inLanguage"] = "en",
                    ["image"] = icon,
                };
                IElement json = document.CreateElement("script");
                json.SetAttribute("type", "application/ld+json");
                json.TextContent = data.ToJsonString(JsonOptions);
                document.Head.AppendChild(json);
            }
        }

        return "<!DOCTYPE html>\n" + document.DocumentElement.OuterHtml;
    }

    private static void AddMeta(IHtmlDocument document, string attribute, string name, string content)
    {
        IElement meta = document.CreateElement("meta");
        meta.SetAttribute(attribute, name);
        meta.SetAttribute("content", content);
        document.Head!.AppendChild(meta);
    }

    private static string PrepareApplicationShell(string shell)
    {
        using IHtmlDocument document = new HtmlParser().ParseDocument(shell);
        document.DocumentElement.SetAttribute("lang", "en");
        if (document.QuerySelector("script[src$='markazor-reader.js']") is null)
        {
            IElement enhancement = document.CreateElement("script");
            enhancement.SetAttribute("type", "module");
            enhancement.SetAttribute("src", "/_content/Markazor/markazor-reader.js");
            document.Body!.AppendChild(enhancement);
        }

        return "<!DOCTYPE html>\n" + document.DocumentElement.OuterHtml;
    }

    private static void UpdateDeploymentConfiguration(string output)
    {
        string file = Path.Combine(output, "staticwebapp.config.json");
        JsonObject configuration = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("The deployment configuration must be an object.");
        configuration["trailingSlash"] = "never";
        JsonObject fallback = configuration["navigationFallback"]?.AsObject() ?? new JsonObject();
        fallback["rewrite"] = "/_markazor/app.html";
        JsonArray exclude = fallback["exclude"]?.AsArray() ?? [];
        foreach (string pattern in new[] { "/posts/*", "/notes/*", "/tags/*", "/categories/*", "/page/*", "/_markazor/*", "/sitemap.xml" })
        {
            if (!exclude.Any(item => item?.GetValue<string>() == pattern))
            {
                exclude.Add(pattern);
            }
        }

        fallback["exclude"] = exclude;
        configuration["navigationFallback"] = fallback;
        File.WriteAllText(file, configuration.ToJsonString(JsonOptions));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Build diagnostics use English consistently with the command-line host.")]
    private static void WriteSitemap(string output, List<StaticPage> pages, Uri? baseUrl)
    {
        if (baseUrl is null)
        {
            Console.WriteLine("Markazor: no primary site base URL is configured; canonical URLs, social image URLs and sitemap.xml are omitted.");
            return;
        }

        using XmlWriter writer = XmlWriter.Create(Path.Combine(output, "sitemap.xml"), new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
        foreach (StaticPage page in pages)
        {
            writer.WriteStartElement("url");
            writer.WriteElementString("loc", new Uri(baseUrl, page.Route).AbsoluteUri);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private sealed record StaticPage(MarkazorReaderPageKind Kind, string? Value, int Number, string Route, string Title, string Description, ArticleMeta? Article = null);

    private sealed class LocalMarkdownHandler(Dictionary<string, string> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = files.TryGetValue(path, out string? markdown)
                ? new(HttpStatusCode.OK) { Content = new StringContent(markdown, Encoding.UTF8, "text/markdown") }
                : new(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    private sealed class BuildNavigationManager : NavigationManager
    {
        public BuildNavigationManager(string route) => Initialize("https://markazor-build.invalid/", "https://markazor-build.invalid" + route);
    }

    private sealed class BuildJavaScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("JavaScript cannot run during static generation.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("JavaScript cannot run during static generation.");
    }
}
